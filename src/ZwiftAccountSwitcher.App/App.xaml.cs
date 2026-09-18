using System.Windows;
using ZwiftAccountSwitcher.App.Logging;
using ZwiftAccountSwitcher.App.ViewModels;
using ZwiftAccountSwitcher.Core.Accounts;
using ZwiftAccountSwitcher.Core.Launch;
using ZwiftAccountSwitcher.Windows.Accounts;
using ZwiftAccountSwitcher.Windows.Launch;
using ZwiftAccountSwitcher.Windows.Secrets;
using ZwiftAccountSwitcher.Windows.Settings;

namespace ZwiftAccountSwitcher.App;

public partial class App : Application, IDisposable
{
    private AccountService? accountService;
    private LoginOrchestrator? loginOrchestrator;
    private MainViewModel? mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var repository = new JsonAccountRepository();
        var credentials = new WindowsCredentialStore();
        accountService = new AccountService(repository, credentials);
        loginOrchestrator = new LoginOrchestrator(
            new ZwiftInstallationLocator(),
            new ZwiftProcessController(),
            new ZwiftLauncherAutomation(),
            credentials);

        mainViewModel = new MainViewModel(
            accountService,
            loginOrchestrator,
            new AppSettingsStore(),
            new RedactingLogger());
        var window = new MainWindow(mainViewModel);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        mainViewModel?.Dispose();
        mainViewModel = null;
        loginOrchestrator?.Dispose();
        loginOrchestrator = null;
        accountService?.Dispose();
        accountService = null;
        GC.SuppressFinalize(this);
    }
}
