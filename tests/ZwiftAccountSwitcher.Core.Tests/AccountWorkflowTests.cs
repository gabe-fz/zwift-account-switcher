using ZwiftAccountSwitcher.Core.Accounts;
using ZwiftAccountSwitcher.Core.Secrets;

namespace ZwiftAccountSwitcher.Core.Tests;

public sealed class AccountWorkflowTests
{
    [Fact]
    public async Task AccountWorkflow_AddsMetadataAndCredentialTogether()
    {
        var repository = new FakeRepository();
        var credentials = new FakeCredentialStore();
        using var service = new AccountService(repository, credentials);
        var generated = Guid.NewGuid().ToString("N").ToCharArray();
        try
        {
            var account = await service.AddAsync("Rider", "rider@example.invalid", generated);

            Assert.Single(repository.Accounts, account);
            Assert.Contains(account.Id, credentials.StoredIds);
        }
        finally
        {
            Array.Clear(generated);
        }
    }

    [Fact]
    public async Task AccountWorkflow_RollsBackCredentialWhenMetadataSaveFails()
    {
        var repository = new FakeRepository { SaveFailuresRemaining = 1 };
        var credentials = new FakeCredentialStore();
        using var service = new AccountService(repository, credentials);
        var generated = Guid.NewGuid().ToString("N").ToCharArray();
        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                service.AddAsync("Rider", "rider@example.invalid", generated));

            Assert.Empty(credentials.StoredIds);
            Assert.Equal(1, credentials.DeleteCount);
        }
        finally
        {
            Array.Clear(generated);
        }
    }

    [Fact]
    public async Task AccountWorkflow_RestoresMetadataWhenCredentialDeleteFails()
    {
        var account = new AccountRecord(Guid.NewGuid(), "Rider", "rider@example.invalid");
        var repository = new FakeRepository(account);
        var credentials = new FakeCredentialStore { FailDelete = true };
        credentials.Seed(account.Id);
        using var service = new AccountService(repository, credentials);

        await Assert.ThrowsAsync<CredentialStoreException>(() => service.DeleteAsync(account.Id));

        Assert.Single(repository.Accounts, account);
    }

    [Fact]
    public async Task AccountWorkflow_RejectsDuplicateDisplayNamesWithoutChangingCredentials()
    {
        var existing = new AccountRecord(Guid.NewGuid(), "Rider", "first@example.invalid");
        var repository = new FakeRepository(existing);
        var credentials = new FakeCredentialStore();
        using var service = new AccountService(repository, credentials);
        var generated = Guid.NewGuid().ToString("N").ToCharArray();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AddAsync("rider", "second@example.invalid", generated));

            Assert.Empty(credentials.StoredIds);
        }
        finally
        {
            Array.Clear(generated);
        }
    }

    private sealed class FakeRepository : IAccountRepository
    {
        public FakeRepository(params AccountRecord[] accounts)
        {
            Accounts = accounts.ToList();
        }

        public List<AccountRecord> Accounts { get; private set; }

        public int SaveFailuresRemaining { get; set; }

        public Task<IReadOnlyList<AccountRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountRecord>>(Accounts.ToArray());

        public Task SaveAsync(IReadOnlyCollection<AccountRecord> accounts, CancellationToken cancellationToken = default)
        {
            if (SaveFailuresRemaining > 0)
            {
                SaveFailuresRemaining--;
                throw new IOException("Injected metadata failure.");
            }

            Accounts = accounts.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public HashSet<Guid> StoredIds { get; } = new();
        public int DeleteCount { get; private set; }
        public bool FailDelete { get; set; }

        public void Seed(Guid accountId) => StoredIds.Add(accountId);

        public Task StoreAsync(
            Guid accountId,
            string username,
            ReadOnlyMemory<char> password,
            CancellationToken cancellationToken = default)
        {
            StoredIds.Add(accountId);
            return Task.CompletedTask;
        }

        public Task<StoredCredential?> RetrieveAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            if (!StoredIds.Contains(accountId))
            {
                return Task.FromResult<StoredCredential?>(null);
            }

            return Task.FromResult<StoredCredential?>(
                new StoredCredential("rider@example.invalid", Guid.NewGuid().ToString("N").ToCharArray()));
        }

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            if (FailDelete)
            {
                throw new CredentialStoreException("Injected credential failure.");
            }

            StoredIds.Remove(accountId);
            return Task.CompletedTask;
        }
    }
}
