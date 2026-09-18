namespace ZwiftAccountSwitcher.Core.Accounts;

public interface IAccountRepository
{
    Task<IReadOnlyList<AccountRecord>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<AccountRecord> accounts, CancellationToken cancellationToken = default);
}
