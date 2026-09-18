using System.Diagnostics;
using System.Windows.Automation;
using ZwiftAccountSwitcher.Core.Launch;

namespace ZwiftAccountSwitcher.Windows.Launch;

public sealed class ZwiftLauncherAutomation : IZwiftLauncherAutomation
{
    internal const string ChangeUserId = "change-user-btn";
    internal const string LetsGoId = "lets-go-btn";
    internal const string UsernameId = "username";
    internal const string PasswordId = "password";
    internal const string RememberMeId = "rememberMe";
    internal const string SubmitId = "submit-button";

    private static readonly TimeSpan TogglePollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ToggleSettleTimeout = TimeSpan.FromSeconds(2);

    private static readonly string[] RejectionMarkers =
    {
        "invalid", "incorrect", "login failed", "unable to log", "wrong password", "credentials",
    };

    private static readonly string[] ManualInterventionMarkers =
    {
        "captcha", "verification code", "multi-factor", "two-factor", "authenticator", "verify your identity",
    };

    private readonly ISystemClock clock;
    private AutomationElement? root;
    private AutomationElement? usernameControl;
    private AutomationElement? passwordControl;
    private AutomationElement? submitControl;

    public ZwiftLauncherAutomation(ISystemClock? clock = null)
    {
        this.clock = clock ?? new SystemClock();
    }

    public async Task<LoginControlResult> PrepareLoginAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = clock.UtcNow + timeout;
        var changeUserInvoked = false;
        while (clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                root ??= TryGetRoot(processId);
                if (root is null)
                {
                    await clock.DelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var username = FindById(root, UsernameId);
                var password = FindById(root, PasswordId);
                var rememberMe = FindById(root, RememberMeId);
                var submit = FindById(root, SubmitId);
                if (username is not null && password is not null && rememberMe is not null && submit is not null)
                {
                    if (!HasPattern<ValuePattern>(username, ValuePattern.Pattern) ||
                        !HasPattern<ValuePattern>(password, ValuePattern.Pattern) ||
                        !password.Current.IsPassword ||
                        !HasPattern<TogglePattern>(rememberMe, TogglePattern.Pattern) ||
                        !HasPattern<InvokePattern>(submit, InvokePattern.Pattern))
                    {
                        return LoginControlResult.ControlsChanged;
                    }

                    var toggle = (TogglePattern)rememberMe.GetCurrentPattern(TogglePattern.Pattern);
                    var toggleDeadline = Min(deadline, clock.UtcNow + ToggleSettleTimeout);
                    if (!await EnsureToggleOffAsync(
                            () => toggle.Current.ToggleState,
                            toggle.Toggle,
                            toggleDeadline,
                            clock,
                            cancellationToken).ConfigureAwait(false))
                    {
                        return LoginControlResult.ControlsChanged;
                    }

                    usernameControl = username;
                    passwordControl = password;
                    submitControl = submit;
                    return LoginControlResult.Ready;
                }

                if (!changeUserInvoked)
                {
                    var changeUser = FindById(root, ChangeUserId);
                    if (changeUser is not null && HasPattern<InvokePattern>(changeUser, InvokePattern.Pattern))
                    {
                        ((InvokePattern)changeUser.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        changeUserInvoked = true;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return LoginControlResult.IntegrityMismatch;
            }
            catch (ElementNotAvailableException)
            {
                root = null;
            }

            await clock.DelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        return LoginControlResult.TimedOut;
    }

    public Task EnterCredentialsAsync(
        string username,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (usernameControl is null || passwordControl is null)
        {
            throw new InvalidOperationException("Login controls have not been prepared.");
        }

        var usernamePattern = (ValuePattern)usernameControl.GetCurrentPattern(ValuePattern.Pattern);
        var passwordPattern = (ValuePattern)passwordControl.GetCurrentPattern(ValuePattern.Pattern);
        usernamePattern.SetValue(username);
        string? transientPassword = new(password.Span);
        try
        {
            passwordPattern.SetValue(transientPassword);
        }
        finally
        {
            transientPassword = null;
        }

        return Task.CompletedTask;
    }

    public Task SubmitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (submitControl is null)
        {
            throw new InvalidOperationException("Login controls have not been prepared.");
        }

        ((InvokePattern)submitControl.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        return Task.CompletedTask;
    }

    public async Task<AuthenticationPageResult> WaitForAuthenticationAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (root is null)
        {
            return AuthenticationPageResult.ControlsChanged;
        }

        var deadline = clock.UtcNow + timeout;
        while (clock.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (FindById(root, LetsGoId) is not null)
                {
                    return AuthenticationPageResult.ReadyToLaunch;
                }

                var classified = ClassifyVisiblePage(root);
                if (classified.HasValue)
                {
                    return classified.Value;
                }
            }
            catch (ElementNotAvailableException)
            {
                return AuthenticationPageResult.ControlsChanged;
            }
            catch (UnauthorizedAccessException)
            {
                return AuthenticationPageResult.ControlsChanged;
            }

            await clock.DelayAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return AuthenticationPageResult.TimedOut;
    }

    public Task<bool> ActivateLetsGoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (root is null)
        {
            return Task.FromResult(false);
        }

        try
        {
            var letsGo = FindById(root, LetsGoId);
            if (letsGo is null || !HasPattern<InvokePattern>(letsGo, InvokePattern.Pattern))
            {
                return Task.FromResult(false);
            }

            ((InvokePattern)letsGo.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            return Task.FromResult(true);
        }
        catch (ElementNotAvailableException)
        {
            return Task.FromResult(false);
        }
    }

    internal static async Task<bool> EnsureToggleOffAsync(
        Func<ToggleState> readState,
        Action toggle,
        DateTimeOffset deadline,
        ISystemClock clock,
        CancellationToken cancellationToken)
    {
        if (readState() == ToggleState.Off)
        {
            return true;
        }

        toggle();
        while (clock.UtcNow < deadline)
        {
            await clock.DelayAsync(TogglePollInterval, cancellationToken).ConfigureAwait(false);
            if (readState() == ToggleState.Off)
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static AutomationElement? TryGetRoot(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Refresh();
        return process.MainWindowHandle == IntPtr.Zero
            ? null
            : AutomationElement.FromHandle(process.MainWindowHandle);
    }

    private static AutomationElement? FindById(AutomationElement parent, string automationId)
    {
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
        return parent.FindFirst(TreeScope.Descendants, condition);
    }

    private static bool HasPattern<T>(AutomationElement element, AutomationPattern pattern) where T : BasePattern =>
        element.TryGetCurrentPattern(pattern, out var value) && value is T;

    private static AuthenticationPageResult? ClassifyVisiblePage(AutomationElement parent)
    {
        var elements = parent.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement element in elements)
        {
            string name;
            try
            {
                if (element.Current.IsOffscreen)
                {
                    continue;
                }

                name = element.Current.Name;
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (ManualInterventionMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return AuthenticationPageResult.ManualIntervention;
            }

            if (RejectionMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return AuthenticationPageResult.Rejected;
            }
        }

        return null;
    }
}
