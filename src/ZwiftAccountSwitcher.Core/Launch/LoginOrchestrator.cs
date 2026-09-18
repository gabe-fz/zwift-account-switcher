using ZwiftAccountSwitcher.Core.Accounts;
using ZwiftAccountSwitcher.Core.Secrets;

namespace ZwiftAccountSwitcher.Core.Launch;

public sealed class LoginOrchestrator : IDisposable
{
    private readonly IZwiftInstallationLocator installationLocator;
    private readonly IZwiftProcessController processController;
    private readonly IZwiftLauncherAutomation automation;
    private readonly ICredentialStore credentialStore;
    private readonly LoginTimeouts timeouts;
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public LoginOrchestrator(
        IZwiftInstallationLocator installationLocator,
        IZwiftProcessController processController,
        IZwiftLauncherAutomation automation,
        ICredentialStore credentialStore,
        LoginTimeouts? timeouts = null)
    {
        this.installationLocator = installationLocator;
        this.processController = processController;
        this.automation = automation;
        this.credentialStore = credentialStore;
        this.timeouts = timeouts ?? LoginTimeouts.Default;
    }

    public async Task<LoginResult> RunAsync(
        AccountRecord account,
        string? launcherOverride,
        IProgress<LoginProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var gateEntered = false;
        try
        {
            if (!await operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return LoginResult.Fail(LoginFailureKind.Unexpected, "Another login operation is already running.");
            }

            gateEntered = true;
            return await RunCoreAsync(account, launcherOverride, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report(progress, LoginState.Cancelled, "Cancelled before game launch.");
            return LoginResult.Cancelled();
        }
        catch (CredentialStoreException)
        {
            return LoginResult.Fail(
                LoginFailureKind.CredentialMissing,
                "The saved credential could not be read. Edit the account and save its password again.");
        }
        catch (Exception)
        {
            return LoginResult.Fail(
                LoginFailureKind.Unexpected,
                "The login operation failed unexpectedly. Close the launcher and try again.");
        }
        finally
        {
            if (gateEntered)
            {
                operationGate.Release();
            }
        }
    }

    public void Dispose()
    {
        operationGate.Dispose();
    }

    private async Task<LoginResult> RunCoreAsync(
        AccountRecord account,
        string? launcherOverride,
        IProgress<LoginProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, LoginState.Preflight, "Checking Zwift installation and running processes.");
        if (processController.IsGameRunning())
        {
            return LoginResult.Fail(
                LoginFailureKind.ActiveGame,
                "Zwift is already running. Finish and close the active ride before switching accounts.");
        }

        var installation = await installationLocator.ResolveAsync(launcherOverride, cancellationToken).ConfigureAwait(false);
        if (!installation.IsSuccess)
        {
            return installation.Failure == InstallationFailure.InvalidOverride
                ? LoginResult.Fail(LoginFailureKind.InvalidLauncherPath, "The selected launcher path is invalid. Browse to ZwiftLauncher.exe.")
                : LoginResult.Fail(LoginFailureKind.MissingInstallation, "Zwift Launcher was not found. Browse to ZwiftLauncher.exe.");
        }

        Report(progress, LoginState.StartingLauncher, "Opening Zwift Launcher.");
        var launcher = await processController.GetOrStartLauncherAsync(
            installation.LauncherPath!,
            timeouts.LauncherWindow,
            cancellationToken).ConfigureAwait(false);
        if (!launcher.IsSuccess)
        {
            return launcher.Failure switch
            {
                LauncherStartFailure.IntegrityMismatch => LoginResult.Fail(
                    LoginFailureKind.IntegrityMismatch,
                    "Zwift Launcher is running as administrator. Close it and reopen it normally before trying again."),
                LauncherStartFailure.WindowTimedOut => LoginResult.Fail(
                    LoginFailureKind.TimedOut,
                    "Zwift Launcher did not show a window in time. Check for an update or elevation prompt."),
                _ => LoginResult.Fail(
                    LoginFailureKind.LauncherStartFailed,
                    "Zwift Launcher could not be opened. Verify the installation and try again."),
            };
        }

        Report(progress, LoginState.ChangingUser, "Switching the launcher to its login screen.");
        var controls = await automation.PrepareLoginAsync(
            launcher.ProcessId!.Value,
            timeouts.LoginControls,
            cancellationToken).ConfigureAwait(false);
        if (controls != LoginControlResult.Ready)
        {
            return controls switch
            {
                LoginControlResult.IntegrityMismatch => LoginResult.Fail(
                    LoginFailureKind.IntegrityMismatch,
                    "The launcher is elevated and cannot be automated safely. Close it and reopen it normally."),
                LoginControlResult.TimedOut => LoginResult.Fail(
                    LoginFailureKind.TimedOut,
                    "The launcher login controls did not appear in time. Complete updates, then try again."),
                _ => LoginResult.Fail(
                    LoginFailureKind.ControlsChanged,
                    "The launcher login controls have changed. Update this application before trying again."),
            };
        }

        Report(progress, LoginState.DisablingRememberMe, "Remember me is disabled.");
        Report(progress, LoginState.RetrievingCredential, "Reading the selected credential from Windows Credential Manager.");
        using var credential = await credentialStore.RetrieveAsync(account.Id, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return LoginResult.Fail(
                LoginFailureKind.CredentialMissing,
                "The saved credential is missing. Edit the account and save its password again.");
        }

        Report(progress, LoginState.EnteringCredential, "Entering the selected account securely.");
        await automation.EnterCredentialsAsync(account.Username, credential.Password, cancellationToken).ConfigureAwait(false);
        Report(progress, LoginState.SubmittingLogin, "Submitting login.");
        await automation.SubmitAsync(cancellationToken).ConfigureAwait(false);

        Report(progress, LoginState.WaitingForAuthentication, "Waiting for the launcher to authenticate.");
        var authentication = await automation.WaitForAuthenticationAsync(
            timeouts.Authentication,
            cancellationToken).ConfigureAwait(false);
        switch (authentication)
        {
            case AuthenticationPageResult.Rejected:
                return LoginResult.Fail(
                    LoginFailureKind.AuthenticationRejected,
                    "Login was rejected. Edit the saved username or password and try again.");
            case AuthenticationPageResult.ManualIntervention:
                Report(progress, LoginState.ManualIntervention, "MFA, CAPTCHA, or another confirmation needs manual attention.");
                return LoginResult.Fail(
                    LoginFailureKind.ManualInterventionRequired,
                    "Complete the MFA, CAPTCHA, or confirmation in Zwift Launcher, then start again if needed.");
            case AuthenticationPageResult.ControlsChanged:
                return LoginResult.Fail(
                    LoginFailureKind.ControlsChanged,
                    "The post-login launcher controls have changed. Update this application before trying again.");
            case AuthenticationPageResult.TimedOut:
                return LoginResult.Fail(
                    LoginFailureKind.TimedOut,
                    "Authentication timed out. Check the launcher for a network, MFA, or CAPTCHA prompt.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, LoginState.ActivatingLetsGo, "Starting Zwift.", canCancel: false);
        if (!await automation.ActivateLetsGoAsync(CancellationToken.None).ConfigureAwait(false))
        {
            return LoginResult.Fail(
                LoginFailureKind.ControlsChanged,
                "The Let's Go control was not available. Close the launcher and try again.");
        }

        Report(progress, LoginState.WaitingForGame, "Waiting for Zwift to start.");
        var gameStart = await processController.WaitForGameStartAsync(
            timeouts.GameStart,
            cancellationToken).ConfigureAwait(false);
        if (gameStart == GameStartResult.ExitedEarly)
        {
            return LoginResult.Fail(
                LoginFailureKind.GameExitedEarly,
                "Zwift started but closed before startup completed. You can try again when ready.");
        }

        if (gameStart == GameStartResult.TimedOut)
        {
            return LoginResult.Fail(
                LoginFailureKind.TimedOut,
                "Zwift did not start in time. Check the launcher for an update or error.");
        }

        Report(progress, LoginState.Completed, "Zwift started.", canCancel: false);
        return LoginResult.Success();
    }

    private static void Report(
        IProgress<LoginProgress>? progress,
        LoginState state,
        string message,
        bool canCancel = true) => progress?.Report(new LoginProgress(state, message, canCancel));
}
