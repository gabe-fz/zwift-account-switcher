using System.Windows.Automation;
using ZwiftAccountSwitcher.Core.Launch;
using ZwiftAccountSwitcher.Windows.Launch;

namespace ZwiftAccountSwitcher.Windows.Tests;

public sealed class SelectorContractTests
{
    [Fact]
    public void SelectorContract_MatchesCharacterizedAutomationIds()
    {
        Assert.Equal("change-user-btn", ZwiftLauncherAutomation.ChangeUserId);
        Assert.Equal("lets-go-btn", ZwiftLauncherAutomation.LetsGoId);
        Assert.Equal("username", ZwiftLauncherAutomation.UsernameId);
        Assert.Equal("password", ZwiftLauncherAutomation.PasswordId);
        Assert.Equal("rememberMe", ZwiftLauncherAutomation.RememberMeId);
        Assert.Equal("submit-button", ZwiftLauncherAutomation.SubmitId);
    }

    [Fact]
    public async Task RememberMeToggle_WaitsForAsynchronousProviderUpdate()
    {
        var clock = new FakeClock();
        var reads = 0;
        var toggles = 0;

        var result = await ZwiftLauncherAutomation.EnsureToggleOffAsync(
            () => ++reads >= 3 ? ToggleState.Off : ToggleState.On,
            () => toggles++,
            clock.UtcNow.AddSeconds(2),
            clock,
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, toggles);
        Assert.Equal(2, clock.DelayCount);
    }

    [Fact]
    public async Task RememberMeToggle_FailsClosedWhenStateDoesNotSettle()
    {
        var clock = new FakeClock();
        var toggles = 0;

        var result = await ZwiftLauncherAutomation.EnsureToggleOffAsync(
            () => ToggleState.On,
            () => toggles++,
            clock.UtcNow.AddMilliseconds(200),
            clock,
            CancellationToken.None);

        Assert.False(result);
        Assert.Equal(1, toggles);
        Assert.Equal(2, clock.DelayCount);
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public int DelayCount { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UtcNow += delay;
            DelayCount++;
            return Task.CompletedTask;
        }
    }
}
