using ZwiftAccountSwitcher.Core.Launch;
using ZwiftAccountSwitcher.Windows.Launch;

namespace ZwiftAccountSwitcher.Windows.Tests;

public sealed class ProcessControllerTests
{
    [Fact]
    public async Task GetOrStartLauncher_RequestsWindowFromBackgroundInstance()
    {
        var clock = new FakeClock();
        var startRequested = false;
        var controller = new ZwiftProcessController(
            clock,
            () => false,
            () => startRequested
                ? new[] { new LauncherProcessSnapshot(42, true) }
                : new[] { new LauncherProcessSnapshot(42, false) },
            _ => startRequested = true,
            _ => true);

        var result = await controller.GetOrStartLauncherAsync(
            "C:\\Zwift\\ZwiftLauncher.exe",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.ProcessId);
        Assert.True(startRequested);
    }

    [Fact]
    public async Task GetOrStartLauncher_ReusesExistingVisibleWindow()
    {
        var clock = new FakeClock();
        var startCount = 0;
        var controller = new ZwiftProcessController(
            clock,
            () => false,
            () => new[] { new LauncherProcessSnapshot(57, true) },
            _ =>
            {
                startCount++;
                return true;
            },
            _ => true);

        var result = await controller.GetOrStartLauncherAsync(
            "C:\\Zwift\\ZwiftLauncher.exe",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(57, result.ProcessId);
        Assert.Equal(0, startCount);
    }

    [Fact]
    public async Task WaitForGameStart_RequiresStableProcessPresence()
    {
        var clock = new FakeClock();
        var controller = new ZwiftProcessController(clock, () => true);

        var result = await controller.WaitForGameStartAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(GameStartResult.Started, result);
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Elapsed);
    }

    [Fact]
    public async Task WaitForGameStart_ReportsProcessThatExitsDuringStartup()
    {
        var clock = new FakeClock();
        var checks = 0;
        var controller = new ZwiftProcessController(clock, () => checks++ == 0);

        var result = await controller.WaitForGameStartAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(GameStartResult.ExitedEarly, result);
        Assert.Equal(TimeSpan.FromMilliseconds(250), clock.Elapsed);
    }

    [Fact]
    public async Task WaitForGameStart_TimesOutWhenProcessNeverAppears()
    {
        var clock = new FakeClock();
        var controller = new ZwiftProcessController(clock, () => false);

        var result = await controller.WaitForGameStartAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(GameStartResult.TimedOut, result);
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Elapsed);
    }

    private sealed class FakeClock : ISystemClock
    {
        private readonly DateTimeOffset started = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public TimeSpan Elapsed => UtcNow - started;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }
}
