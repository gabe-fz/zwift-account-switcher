using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ZwiftAccountSwitcher.App.Logging;
using ZwiftAccountSwitcher.Core.Accounts;
using ZwiftAccountSwitcher.Core.Launch;
using ZwiftAccountSwitcher.Core.Secrets;
using ZwiftAccountSwitcher.Windows.Launch;
using ZwiftAccountSwitcher.Windows.Settings;

namespace ZwiftAccountSwitcher.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AccountService accountService;
    private readonly LoginOrchestrator loginOrchestrator;
    private readonly AppSettingsStore settingsStore;
    private readonly RedactingLogger logger;
    private AccountEditorViewModel? editor;
    private string? launcherPath;
    private string statusText = "Ready.";
    private bool isBusy;
    private bool canCancelLogin;
    private bool isEditorSaving;
    private CancellationTokenSource? loginCancellation;

    public MainViewModel(
        AccountService accountService,
        LoginOrchestrator loginOrchestrator,
        AppSettingsStore settingsStore,
        RedactingLogger logger)
    {
        this.accountService = accountService;
        this.loginOrchestrator = loginOrchestrator;
        this.settingsStore = settingsStore;
        this.logger = logger;

        AddCommand = new RelayCommand(_ => Editor = AccountEditorViewModel.CreateNew(), _ => CanEditAccounts);
        EditCommand = new RelayCommand(
            parameter => Editor = AccountEditorViewModel.Edit(((AccountCardViewModel)parameter!).Account),
            parameter => CanEditAccounts && parameter is AccountCardViewModel);
        RideCommand = new AsyncRelayCommand(
            parameter => RideAsync(((AccountCardViewModel)parameter!).Account),
            parameter => !IsBusy && parameter is AccountCardViewModel);
        RequestDeleteCommand = new RelayCommand(
            parameter => DeleteRequested?.Invoke((AccountCardViewModel)parameter!),
            parameter => CanEditAccounts && parameter is AccountCardViewModel);
        CancelLoginCommand = new RelayCommand(_ => loginCancellation?.Cancel(), _ => IsBusy && CanCancelLogin);
        CancelEditorCommand = new RelayCommand(_ => Editor = null, _ => !IsBusy && Editor is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<AccountCardViewModel>? DeleteRequested;

    public ObservableCollection<AccountCardViewModel> Accounts { get; } = new();

    public ICommand AddCommand { get; }

    public ICommand EditCommand { get; }

    public ICommand RideCommand { get; }

    public ICommand RequestDeleteCommand { get; }

    public ICommand CancelLoginCommand { get; }

    public ICommand CancelEditorCommand { get; }

    public AccountEditorViewModel? Editor
    {
        get => editor;
        private set
        {
            if (SetField(ref editor, value))
            {
                OnPropertyChanged(nameof(IsEditorOpen));
                RaiseCommandStates();
            }
        }
    }

    public bool IsEditorOpen => Editor is not null;

    public string LauncherPathDisplay => string.IsNullOrWhiteSpace(launcherPath)
        ? "Automatic detection"
        : launcherPath;

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanEditAccounts));
                RaiseCommandStates();
            }
        }
    }

    public bool CanCancelLogin
    {
        get => canCancelLogin;
        private set
        {
            if (SetField(ref canCancelLogin, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool CanEditAccounts => !IsBusy && Editor is null;

    public async Task InitializeAsync()
    {
        try
        {
            var settings = await settingsStore.LoadAsync();
            launcherPath = settings.LauncherPath;
            OnPropertyChanged(nameof(LauncherPathDisplay));
            await ReloadAccountsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogStorageFailure("load", exception);
            StatusText = "Saved data could not be loaded. Check access to the local application data folder.";
        }
    }

    public async Task SaveEditorAsync(char[] password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (Editor is null || IsBusy || isEditorSaving)
        {
            return;
        }

        isEditorSaving = true;
        var currentEditor = Editor;
        try
        {
            if (currentEditor.IsNew)
            {
                if (password.Length == 0)
                {
                    StatusText = "Enter a password for the new account.";
                    return;
                }

                await accountService.AddAsync(currentEditor.DisplayName, currentEditor.Username, password);
            }
            else
            {
                ReadOnlyMemory<char>? replacement = password.Length == 0 ? null : password;
                await accountService.UpdateAsync(
                    currentEditor.AccountId!.Value,
                    currentEditor.DisplayName,
                    currentEditor.Username,
                    replacement);
            }

            Editor = null;
            await ReloadAccountsAsync();
            StatusText = "Account saved securely.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            CredentialStoreException or
            StorageConsistencyException or
            IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            logger.LogStorageFailure("save", exception);
            StatusText = exception is ArgumentException
                ? exception.Message
                : "The account could not be saved consistently. Retry, or remove the account and add it again.";
        }
        finally
        {
            isEditorSaving = false;
        }
    }

    public async Task DeleteAsync(AccountCardViewModel account)
    {
        if (!CanEditAccounts)
        {
            return;
        }

        try
        {
            await accountService.DeleteAsync(account.Account.Id);
            await ReloadAccountsAsync();
            StatusText = "Account removed from metadata and Windows Credential Manager.";
        }
        catch (Exception exception) when (
            exception is CredentialStoreException or
            StorageConsistencyException or
            IOException or
            UnauthorizedAccessException)
        {
            logger.LogStorageFailure("delete", exception);
            StatusText = "The account could not be removed consistently. Retry deletion before making other changes.";
        }
    }

    public async Task SetLauncherPathAsync(string path)
    {
        if (!ZwiftInstallationLocator.IsValid(path))
        {
            StatusText = "Select a file named ZwiftLauncher.exe.";
            return;
        }

        try
        {
            await settingsStore.SaveAsync(new AppSettings(path));
            launcherPath = path;
            OnPropertyChanged(nameof(LauncherPathDisplay));
            StatusText = "Launcher path saved.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogStorageFailure("settings", exception);
            StatusText = "The launcher path could not be saved.";
        }
    }

    public void Dispose()
    {
        loginCancellation?.Cancel();
        loginCancellation?.Dispose();
        loginCancellation = null;
    }

    private async Task RideAsync(AccountRecord account)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        CanCancelLogin = true;
        loginCancellation = new CancellationTokenSource();
        var progress = new Progress<LoginProgress>(item =>
        {
            StatusText = item.Message;
            CanCancelLogin = item.CanCancel;
            logger.LogState(item.State);
        });

        try
        {
            var result = await loginOrchestrator.RunAsync(
                account,
                launcherPath,
                progress,
                loginCancellation.Token);
            StatusText = result.Message;
            logger.LogOutcome(result.Failure);
        }
        finally
        {
            loginCancellation.Dispose();
            loginCancellation = null;
            CanCancelLogin = false;
            IsBusy = false;
        }
    }

    private async Task ReloadAccountsAsync()
    {
        var records = await accountService.LoadAsync();
        Accounts.Clear();
        foreach (var record in records.OrderBy(account => account.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Accounts.Add(new AccountCardViewModel(record));
        }

        OnPropertyChanged(nameof(Accounts));
    }

    private void RaiseCommandStates()
    {
        ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EditCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RideCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RequestDeleteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelLoginCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelEditorCommand).RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AccountCardViewModel
{
    public AccountCardViewModel(AccountRecord account)
    {
        Account = account;
    }

    public AccountRecord Account { get; }

    public string DisplayName => Account.DisplayName;
}
