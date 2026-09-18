using ZwiftAccountSwitcher.Windows.Secrets;

namespace ZwiftAccountSwitcher.Windows.Tests;

public sealed class CredentialStoreTests
{
    [Fact]
    public async Task CredentialStore_RoundTripsAndDeletesDisposableCredential()
    {
        var accountId = Guid.NewGuid();
        var store = new WindowsCredentialStore();
        var generatedValue = $"test-{Guid.NewGuid():N}".ToCharArray();

        try
        {
            await store.StoreAsync(accountId, "disposable-test@example.invalid", generatedValue);
            using var stored = await store.RetrieveAsync(accountId);

            Assert.NotNull(stored);
            Assert.Equal("disposable-test@example.invalid", stored.Username);
            Assert.True(stored.Password.Span.SequenceEqual(generatedValue));

            await store.DeleteAsync(accountId);
            Assert.Null(await store.RetrieveAsync(accountId));
        }
        finally
        {
            Array.Clear(generatedValue);
            await store.DeleteAsync(accountId);
        }
    }
}
