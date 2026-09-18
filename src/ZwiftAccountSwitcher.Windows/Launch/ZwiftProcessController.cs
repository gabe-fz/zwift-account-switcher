using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ZwiftAccountSwitcher.Core.Launch;

namespace ZwiftAccountSwitcher.Windows.Launch;

public sealed class ZwiftProcessController : IZwiftProcessController
{
    private const string LauncherProcessName = "ZwiftLauncher";
    private const string GameProcessName = "ZwiftApp";
    private static readonly TimeSpan GamePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan GameStartupStability = TimeSpan.FromSeconds(2);
    private readonly ISystemClock clock;
    private readonly Func<bool> isGameRunning;
    private readonly Func<IReadOnlyList<LauncherProcessSnapshot>> getLauncherProcesses;
    private readonly Func<string, bool> startLauncher;
    private readonly Func<int, bool> hasCompatibleIntegrity;

    public ZwiftProcessController(ISystemClock? clock = null)
        : this(
            clock ?? new SystemClock(),
            DetectGameProcess,
            DetectLauncherProcesses,
            StartLauncher,
            HasCompatibleIntegrity)
    {
    }

    internal ZwiftProcessController(ISystemClock clock, Func<bool> isGameRunning)
        : this(clock, isGameRunning, DetectLauncherProcesses, StartLauncher, HasCompatibleIntegrity)
    {
    }

    internal ZwiftProcessController(
        ISystemClock clock,
        Func<bool> isGameRunning,
        Func<IReadOnlyList<LauncherProcessSnapshot>> getLauncherProcesses,
        Func<string, bool> startLauncher,
        Func<int, bool> hasCompatibleIntegrity)
    {
        this.clock = clock;
        this.isGameRunning = isGameRunning;
        this.getLauncherProcesses = getLauncherProcesses;
        this.startLauncher = startLauncher;
        this.hasCompatibleIntegrity = hasCompatibleIntegrity;
    }

    public bool IsGameRunning() => isGameRunning();

    public async Task<LauncherStartResult> GetOrStartLauncherAsync(
        string launcherPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var launchers = getLauncherProcesses();
            var visibleLauncher = launchers.FirstOrDefault(process => process.HasWindow);
            if (visibleLauncher.HasWindow)
            {
                return hasCompatibleIntegrity(visibleLauncher.Id)
                    ? new LauncherStartResult(visibleLauncher.Id)
                    : new LauncherStartResult(null, LauncherStartFailure.IntegrityMismatch);
            }

            // ZwiftLauncher commonly leaves a windowless background process behind. Starting the
            // executable again asks that existing instance to show a new window.
            if (!startLauncher(launcherPath))
            {
                return new LauncherStartResult(null, LauncherStartFailure.StartFailed);
            }

            var deadline = clock.UtcNow + timeout;
            while (clock.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                launchers = getLauncherProcesses();
                visibleLauncher = launchers.FirstOrDefault(process => process.HasWindow);
                if (visibleLauncher.HasWindow)
                {
                    return hasCompatibleIntegrity(visibleLauncher.Id)
                        ? new LauncherStartResult(visibleLauncher.Id)
                        : new LauncherStartResult(null, LauncherStartFailure.IntegrityMismatch);
                }

                await clock.DelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }

            return new LauncherStartResult(null, LauncherStartFailure.WindowTimedOut);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new LauncherStartResult(null, LauncherStartFailure.StartFailed);
        }
    }

    public async Task<GameStartResult> WaitForGameStartAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = clock.UtcNow + timeout;
        DateTimeOffset? firstObserved = null;
        while (clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isGameRunning())
            {
                firstObserved ??= clock.UtcNow;
                if (clock.UtcNow - firstObserved.Value >= GameStartupStability)
                {
                    return GameStartResult.Started;
                }
            }
            else if (firstObserved.HasValue)
            {
                return GameStartResult.ExitedEarly;
            }

            await clock.DelayAsync(GamePollInterval, cancellationToken).ConfigureAwait(false);
        }

        return GameStartResult.TimedOut;
    }

    private static IReadOnlyList<LauncherProcessSnapshot> DetectLauncherProcesses()
    {
        var launchers = Process.GetProcessesByName(LauncherProcessName);
        try
        {
            var snapshots = new List<LauncherProcessSnapshot>(launchers.Length);
            foreach (var launcher in launchers.OrderBy(process => process.Id))
            {
                try
                {
                    launcher.Refresh();
                    snapshots.Add(new LauncherProcessSnapshot(
                        launcher.Id,
                        launcher.MainWindowHandle != IntPtr.Zero));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while it was being inspected.
                }
            }

            return snapshots;
        }
        finally
        {
            foreach (var launcher in launchers)
            {
                launcher.Dispose();
            }
        }
    }

    private static bool StartLauncher(string launcherPath)
    {
        using var launcher = Process.Start(new ProcessStartInfo
        {
            FileName = launcherPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath),
        });
        return launcher is not null;
    }

    private static bool DetectGameProcess()
    {
        var games = Process.GetProcessesByName(GameProcessName);
        try
        {
            return games.Length > 0;
        }
        finally
        {
            foreach (var game in games)
            {
                game.Dispose();
            }
        }
    }

    private static bool HasCompatibleIntegrity(int processId)
    {
        var current = TryGetIntegrityLevel(Environment.ProcessId);
        var target = TryGetIntegrityLevel(processId);
        return current.HasValue && target.HasValue && target.Value <= current.Value;
    }

    private static int? TryGetIntegrityLevel(int processId)
    {
        const uint processQueryLimitedInformation = 0x1000;
        const uint tokenQuery = 0x0008;
        const int tokenIntegrityLevel = 25;

        var processHandle = OpenProcess(processQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!OpenProcessToken(processHandle, tokenQuery, out var tokenHandle))
            {
                return null;
            }

            try
            {
                _ = GetTokenInformation(tokenHandle, tokenIntegrityLevel, IntPtr.Zero, 0, out var size);
                if (size == 0)
                {
                    return null;
                }

                var buffer = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    if (!GetTokenInformation(tokenHandle, tokenIntegrityLevel, buffer, size, out _))
                    {
                        return null;
                    }

                    var sid = Marshal.ReadIntPtr(buffer);
                    var countPointer = GetSidSubAuthorityCount(sid);
                    if (countPointer == IntPtr.Zero)
                    {
                        return null;
                    }

                    var count = Marshal.ReadByte(countPointer);
                    if (count == 0)
                    {
                        return null;
                    }

                    var ridPointer = GetSidSubAuthority(sid, (uint)(count - 1));
                    return ridPointer == IntPtr.Zero ? null : Marshal.ReadInt32(ridPointer);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal readonly record struct LauncherProcessSnapshot(int Id, bool HasWindow);
