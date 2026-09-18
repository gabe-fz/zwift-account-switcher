using System.Security;

namespace ZwiftAccountSwitcher.Core.Secrets;

public interface ICredentialStore
{
    Task StoreAsync(Guid accountId, string username, ReadOnlyMemory<char> password, CancellationToken cancellationToken = default);

    Task<StoredCredential?> RetrieveAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default);
}

public sealed class StoredCredential : IDisposable
{
    private char[]? password;

    public StoredCredential(string username, char[] password)
    {
        Username = username ?? throw new ArgumentNullException(nameof(username));
        this.password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public string Username { get; }

    public ReadOnlyMemory<char> Password => password ?? throw new ObjectDisposedException(nameof(StoredCredential));

    public void Dispose()
    {
        var current = Interlocked.Exchange(ref password, null);
        if (current is not null)
        {
            Array.Clear(current);
        }

        GC.SuppressFinalize(this);
    }
}

public sealed class CredentialStoreException : Exception
{
    public CredentialStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
