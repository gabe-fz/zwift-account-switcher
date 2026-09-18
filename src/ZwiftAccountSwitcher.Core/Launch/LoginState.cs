namespace ZwiftAccountSwitcher.Core.Launch;

public enum LoginState
{
    Idle,
    Preflight,
    StartingLauncher,
    ChangingUser,
    LocatingLoginControls,
    DisablingRememberMe,
    RetrievingCredential,
    EnteringCredential,
    SubmittingLogin,
    WaitingForAuthentication,
    ManualIntervention,
    ActivatingLetsGo,
    WaitingForGame,
    Completed,
    Failed,
    Cancelled,
}

public enum LoginFailureKind
{
    None,
    ActiveGame,
    MissingInstallation,
    InvalidLauncherPath,
    LauncherStartFailed,
    IntegrityMismatch,
    ControlsChanged,
    CredentialMissing,
    AuthenticationRejected,
    ManualInterventionRequired,
    GameExitedEarly,
    TimedOut,
    Cancelled,
    Unexpected,
}

public sealed record LoginResult(LoginState FinalState, LoginFailureKind Failure, string Message)
{
    public bool IsSuccess => FinalState == LoginState.Completed;

    public static LoginResult Success() => new(LoginState.Completed, LoginFailureKind.None, "Zwift started.");

    public static LoginResult Fail(LoginFailureKind failure, string message) => new(LoginState.Failed, failure, message);

    public static LoginResult Cancelled() => new(LoginState.Cancelled, LoginFailureKind.Cancelled, "Login was cancelled before game launch.");
}

public sealed record LoginProgress(LoginState State, string Message, bool CanCancel = true);
