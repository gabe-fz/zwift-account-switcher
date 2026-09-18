using ZwiftAccountSwitcher.Core.Accounts;
using ZwiftAccountSwitcher.Core.Launch;
using ZwiftAccountSwitcher.Core.Secrets;

namespace ZwiftAccountSwitcher.Core.Tests;

public sealed class LoginOrchestratorTests
{
    [Fact]
    public async Task LoginOrchestrator_CompletesTheBoundedSequence()
    {
        var ports = new FakePorts();
        using var orchestrator = ports.CreateOrchestrator();
        var progress = new InlineProgress();

        var result = await orchestrator.RunAsync(CreateAccount(), null, progress);

        Assert.True(result.IsSuccess);
        Assert.True(ports.Automation.CredentialsEntered);
        Assert.True(ports.Automation.LetsGoActivated);
        Assert.Contains(progress.Items, item => item.State == LoginState.DisablingRememberMe);
        Assert.False(progress.Items.Single(item => item.State == LoginState.ActivatingLetsGo).CanCancel);
        Assert.Equal(1, ports.Credentials.RetrieveCount);
    }

    [Fact]
    public async Task LoginOrchestrator_RefusesToTouchAnActiveRide()
    {
        var ports = new FakePorts();
        ports.Process.ActiveGame = true;
        using var orchestrator = ports.CreateOrchestrator();

        var result = await orchestrator.RunAsync(CreateAccount(), null);

        Assert.Equal(LoginFailureKind.ActiveGame, result.Failure);
        Assert.Equal(0, ports.Locator.ResolveCount);
        Assert.Equal(0, ports.Credentials.RetrieveCount);
        Assert.False(ports.Automation.CredentialsEntered);
    }

    [Theory]
    [InlineData(AuthenticationPageResult.Rejected, LoginFailureKind.AuthenticationRejected)]
    [InlineData(AuthenticationPageResult.ManualIntervention, LoginFailureKind.ManualInterventionRequired)]
    [InlineData(AuthenticationPageResult.ControlsChanged, LoginFailureKind.ControlsChanged)]
    [InlineData(AuthenticationPageResult.TimedOut, LoginFailureKind.TimedOut)]
    public async Task LoginOrchestrator_MapsAuthenticationOutcomes(
        AuthenticationPageResult pageResult,
        LoginFailureKind expectedFailure)
    {
        var ports = new FakePorts();
        ports.Automation.AuthenticationResult = pageResult;
        using var orchestrator = ports.CreateOrchestrator();

        var result = await orchestrator.RunAsync(CreateAccount(), null);

        Assert.Equal(expectedFailure, result.Failure);
        Assert.False(ports.Automation.LetsGoActivated);
    }

    [Fact]
    public async Task LoginOrchestrator_ReportsMissingInstallation()
    {
        var ports = new FakePorts();
        ports.Locator.Result = new InstallationResult(null, InstallationFailure.NotFound);
        using var orchestrator = ports.CreateOrchestrator();

        var result = await orchestrator.RunAsync(CreateAccount(), null);

        Assert.Equal(LoginFailureKind.MissingInstallation, result.Failure);
        Assert.Equal(0, ports.Credentials.RetrieveCount);
    }

    [Fact]
    public async Task LoginOrchestrator_FailsClosedForChangedLoginSelectors()
    {
        var ports = new FakePorts();
        ports.Automation.PrepareResult = LoginControlResult.ControlsChanged;
        using var orchestrator = ports.CreateOrchestrator();

        var result = await orchestrator.RunAsync(CreateAccount(), null);

        Assert.Equal(LoginFailureKind.ControlsChanged, result.Failure);
        Assert.Equal(0, ports.Credentials.RetrieveCount);
    }

    [Fact]
    public async Task LoginOrchestrator_CancelsBeforeGameLaunch()
    {
        var ports = new FakePorts();
        ports.Automation.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var orchestrator = ports.CreateOrchestrator();
        using var cancellation = new CancellationTokenSource();
        var operation = orchestrator.RunAsync(CreateAccount(), null, cancellationToken: cancellation.Token);
        await ports.Automation.PrepareEntered.Task;

        cancellation.Cancel();
        var result = await operation;

        Assert.Equal(LoginFailureKind.Cancelled, result.Failure);
        Assert.False(ports.Automation.LetsGoActivated);
    }

    [Fact]
    public async Task LoginOrchestrator_ReportsWhenGameExitsDuringStartup()
    {
        var ports = new FakePorts();
        ports.Process.GameStartResult = GameStartResult.ExitedEarly;
        using var orchestrator = ports.CreateOrchestrator();

        var result = await orchestrator.RunAsync(CreateAccount(), null);

        Assert.Equal(LoginFailureKind.GameExitedEarly, result.Failure);
        Assert.Contains("closed before startup completed", result.Message);
    }

    [Fact]
    public async Task LoginOrchestrator_CanCancelWhileWaitingForGameStartup()
    {
        var ports = new FakePorts();
        ports.Process.GameStartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var orchestrator = ports.CreateOrchestrator();
        using var cancellation = new CancellationTokenSource();
        var operation = orchestrator.RunAsync(CreateAccount(), null, cancellationToken: cancellation.Token);
        await ports.Process.GameStartEntered.Task;

        cancellation.Cancel();
        var result = await operation;

        Assert.Equal(LoginFailureKind.Cancelled, result.Failure);
    }

    [Fact]
    public async Task LoginOrchestrator_ReportsMissingCredentialWithoutSubmitting()
    {
        var ports = new FakePorts();
        ports.Credentials.ReturnMissing = true;
        using var orchestrator = ports.CreateOrchestrator();

        var result = await orchestrator.RunAsync(CreateAccount(), null);

        Assert.Equal(LoginFailureKind.CredentialMissing, result.Failure);
        Assert.False(ports.Automation.CredentialsEntered);
        Assert.False(ports.Automation.Submitted);
    }

    [Fact]
    public async Task LoginOrchestrator_AllowsOnlyOneOperationAtATime()
    {
        var ports = new FakePorts();
        ports.Automation.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var orchestrator = ports.CreateOrchestrator();
        var first = orchestrator.RunAsync(CreateAccount(), null);
        await ports.Automation.PrepareEntered.Task;

        var second = await orchestrator.RunAsync(CreateAccount(), null);
        ports.Automation.PrepareGate.SetResult();
        var firstResult = await first;

        Assert.True(firstResult.IsSuccess);
        Assert.Equal(LoginFailureKind.Unexpected, second.Failure);
    }

    private static AccountRecord CreateAccount() => new(Guid.NewGuid(), "Test Rider", "test@example.invalid");

    private sealed class FakePorts
    {
        public FakeLocator Locator { get; } = new();
        public FakeProcess Process { get; } = new();
        public FakeAutomation Automation { get; } = new();
        public FakeCredentialStore Credentials { get; } = new();

        public LoginOrchestrator CreateOrchestrator() => new(
            Locator,
            Process,
            Automation,
            Credentials,
            new LoginTimeouts(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
    }

    private sealed class FakeLocator : IZwiftInstallationLocator
    {
        public int ResolveCount { get; private set; }
        public InstallationResult Result { get; set; } = new("C:\\Zwift\\ZwiftLauncher.exe");

        public Task<InstallationResult> ResolveAsync(string? savedOverride, CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeProcess : IZwiftProcessController
    {
        public bool ActiveGame { get; set; }
        public GameStartResult GameStartResult { get; set; } = GameStartResult.Started;
        public TaskCompletionSource GameStartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? GameStartGate { get; set; }

        public bool IsGameRunning() => ActiveGame;

        public Task<LauncherStartResult> GetOrStartLauncherAsync(
            string launcherPath,
            TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(new LauncherStartResult(42));

        public async Task<GameStartResult> WaitForGameStartAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            GameStartEntered.TrySetResult();
            if (GameStartGate is not null)
            {
                await GameStartGate.Task.WaitAsync(cancellationToken);
            }

            return GameStartResult;
        }
    }

    private sealed class FakeAutomation : IZwiftLauncherAutomation
    {
        public AuthenticationPageResult AuthenticationResult { get; set; } = AuthenticationPageResult.ReadyToLaunch;
        public LoginControlResult PrepareResult { get; set; } = LoginControlResult.Ready;
        public bool CredentialsEntered { get; private set; }
        public bool Submitted { get; private set; }
        public bool LetsGoActivated { get; private set; }
        public TaskCompletionSource PrepareEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? PrepareGate { get; set; }

        public async Task<LoginControlResult> PrepareLoginAsync(
            int processId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            PrepareEntered.TrySetResult();
            if (PrepareGate is not null)
            {
                await PrepareGate.Task.WaitAsync(cancellationToken);
            }

            return PrepareResult;
        }

        public Task EnterCredentialsAsync(
            string username,
            ReadOnlyMemory<char> password,
            CancellationToken cancellationToken)
        {
            Assert.False(password.IsEmpty);
            CredentialsEntered = true;
            return Task.CompletedTask;
        }

        public Task SubmitAsync(CancellationToken cancellationToken)
        {
            Submitted = true;
            return Task.CompletedTask;
        }

        public Task<AuthenticationPageResult> WaitForAuthenticationAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(AuthenticationResult);

        public Task<bool> ActivateLetsGoAsync(CancellationToken cancellationToken)
        {
            LetsGoActivated = true;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public bool ReturnMissing { get; set; }
        public int RetrieveCount { get; private set; }

        public Task StoreAsync(
            Guid accountId,
            string username,
            ReadOnlyMemory<char> password,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<StoredCredential?> RetrieveAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            RetrieveCount++;
            if (ReturnMissing)
            {
                return Task.FromResult<StoredCredential?>(null);
            }

            var generated = Guid.NewGuid().ToString("N").ToCharArray();
            return Task.FromResult<StoredCredential?>(new StoredCredential("test@example.invalid", generated));
        }

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InlineProgress : IProgress<LoginProgress>
    {
        public List<LoginProgress> Items { get; } = new();

        public void Report(LoginProgress value) => Items.Add(value);
    }
}
