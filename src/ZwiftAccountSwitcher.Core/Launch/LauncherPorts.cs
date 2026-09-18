namespace ZwiftAccountSwitcher.Core.Launch;

public interface IZwiftInstallationLocator
{
    Task<InstallationResult> ResolveAsync(string? savedOverride, CancellationToken cancellationToken = default);
}

public sealed record InstallationResult(string? LauncherPath, InstallationFailure Failure = InstallationFailure.None)
{
    public bool IsSuccess => LauncherPath is not null && Failure == InstallationFailure.None;
}

public enum InstallationFailure
{
    None,
    NotFound,
    InvalidOverride,
}

public interface IZwiftProcessController
{
    bool IsGameRunning();

    Task<LauncherStartResult> GetOrStartLauncherAsync(
        string launcherPath,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<GameStartResult> WaitForGameStartAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public enum GameStartResult
{
    Started,
    ExitedEarly,
    TimedOut,
}

public sealed record LauncherStartResult(int? ProcessId, LauncherStartFailure Failure = LauncherStartFailure.None)
{
    public bool IsSuccess => ProcessId is not null && Failure == LauncherStartFailure.None;
}

public enum LauncherStartFailure
{
    None,
    StartFailed,
    WindowTimedOut,
    IntegrityMismatch,
}

public interface IZwiftLauncherAutomation
{
    Task<LoginControlResult> PrepareLoginAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken);

    Task EnterCredentialsAsync(string username, ReadOnlyMemory<char> password, CancellationToken cancellationToken);

    Task SubmitAsync(CancellationToken cancellationToken);

    Task<AuthenticationPageResult> WaitForAuthenticationAsync(TimeSpan timeout, CancellationToken cancellationToken);

    Task<bool> ActivateLetsGoAsync(CancellationToken cancellationToken);
}

public enum LoginControlResult
{
    Ready,
    ControlsChanged,
    IntegrityMismatch,
    TimedOut,
}

public enum AuthenticationPageResult
{
    ReadyToLaunch,
    Rejected,
    ManualIntervention,
    ControlsChanged,
    TimedOut,
}

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed record LoginTimeouts(
    TimeSpan LauncherWindow,
    TimeSpan LoginControls,
    TimeSpan Authentication,
    TimeSpan GameStart)
{
    public static LoginTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(2));
}
