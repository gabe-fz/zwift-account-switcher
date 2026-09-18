using ZwiftAccountSwitcher.Core.Secrets;

namespace ZwiftAccountSwitcher.Core.Accounts;

public sealed class AccountService : IDisposable
{
    private readonly IAccountRepository repository;
    private readonly ICredentialStore credentialStore;
    private readonly SemaphoreSlim gate = new(1, 1);

    public AccountService(IAccountRepository repository, ICredentialStore credentialStore)
    {
        this.repository = repository;
        this.credentialStore = credentialStore;
    }

    public Task<IReadOnlyList<AccountRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
        repository.LoadAsync(cancellationToken);

    public async Task<AccountRecord> AddAsync(
        string displayName,
        string username,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken = default)
    {
        if (password.IsEmpty)
        {
            throw new ArgumentException("A password is required.", nameof(password));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            EnsureUniqueDisplayName(existing, displayName, null);
            var account = new AccountRecord(Guid.NewGuid(), displayName, username);

            await credentialStore.StoreAsync(account.Id, account.Username, password, cancellationToken).ConfigureAwait(false);
            try
            {
                await repository.SaveAsync(existing.Append(account).ToArray(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception metadataError)
            {
                try
                {
                    await credentialStore.DeleteAsync(account.Id, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    throw new StorageConsistencyException(
                        "Account details could not be saved and credential cleanup also failed. Retry deletion from the application.",
                        new AggregateException(metadataError, rollbackError));
                }

                throw;
            }

            return account;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AccountRecord> UpdateAsync(
        Guid accountId,
        string displayName,
        string username,
        ReadOnlyMemory<char>? replacementPassword,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            var oldAccount = existing.SingleOrDefault(account => account.Id == accountId)
                ?? throw new KeyNotFoundException("The account no longer exists.");
            EnsureUniqueDisplayName(existing, displayName, accountId);
            var updated = new AccountRecord(accountId, displayName, username);
            using var oldCredential = await credentialStore.RetrieveAsync(accountId, cancellationToken).ConfigureAwait(false)
                ?? throw new CredentialStoreException("The saved credential is missing. Enter a replacement password.");

            var passwordToStore = replacementPassword is { IsEmpty: false }
                ? replacementPassword.Value
                : oldCredential.Password;
            await credentialStore.StoreAsync(accountId, updated.Username, passwordToStore, cancellationToken).ConfigureAwait(false);

            try
            {
                var records = existing.Select(account => account.Id == accountId ? updated : account).ToArray();
                await repository.SaveAsync(records, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception metadataError)
            {
                try
                {
                    await credentialStore.StoreAsync(
                        accountId,
                        oldAccount.Username,
                        oldCredential.Password,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    throw new StorageConsistencyException(
                        "Account details could not be saved and the previous credential could not be restored. Edit the account again.",
                        new AggregateException(metadataError, rollbackError));
                }

                throw;
            }

            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (existing.All(account => account.Id != accountId))
            {
                return;
            }

            var remaining = existing.Where(account => account.Id != accountId).ToArray();
            await repository.SaveAsync(remaining, cancellationToken).ConfigureAwait(false);
            try
            {
                await credentialStore.DeleteAsync(accountId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception credentialError)
            {
                try
                {
                    await repository.SaveAsync(existing, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    throw new StorageConsistencyException(
                        "Credential deletion failed and account details could not be restored. Retry deletion from the application.",
                        new AggregateException(credentialError, rollbackError));
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        gate.Dispose();
    }

    private static void EnsureUniqueDisplayName(
        IEnumerable<AccountRecord> accounts,
        string displayName,
        Guid? exceptAccountId)
    {
        var normalized = displayName?.Trim() ?? string.Empty;
        if (accounts.Any(account =>
                account.Id != exceptAccountId &&
                string.Equals(account.DisplayName, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Display names must be unique.", nameof(displayName));
        }
    }
}

public sealed class StorageConsistencyException : Exception
{
    public StorageConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
