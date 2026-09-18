using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZwiftAccountSwitcher.Core.Accounts;

namespace ZwiftAccountSwitcher.App.ViewModels;

public sealed class AccountEditorViewModel : INotifyPropertyChanged
{
    private string displayName;
    private string username;

    private AccountEditorViewModel(Guid? accountId, string displayName, string username)
    {
        AccountId = accountId;
        this.displayName = displayName;
        this.username = username;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid? AccountId { get; }

    public bool IsNew => AccountId is null;

    public string Title => IsNew ? "Add account" : "Edit account";

    public string PasswordHint => IsNew ? "Required" : "Leave blank to keep the saved password";

    public string DisplayName
    {
        get => displayName;
        set => SetField(ref displayName, value);
    }

    public string Username
    {
        get => username;
        set => SetField(ref username, value);
    }

    public static AccountEditorViewModel CreateNew() => new(null, string.Empty, string.Empty);

    public static AccountEditorViewModel Edit(AccountRecord account) =>
        new(account.Id, account.DisplayName, account.Username);

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
