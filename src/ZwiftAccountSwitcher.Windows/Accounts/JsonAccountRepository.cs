using System.IO;
using System.Text.Json;
using ZwiftAccountSwitcher.Core.Accounts;

namespace ZwiftAccountSwitcher.Windows.Accounts;

public sealed class JsonAccountRepository : IAccountRepository
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string filePath;

    public JsonAccountRepository(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZwiftAccountSwitcher",
            "accounts.json");
    }

    public string FilePath => filePath;

    public async Task<IReadOnlyList<AccountRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<AccountRecord>();
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<AccountDocument>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Account metadata is empty or invalid.");

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException("Account metadata was written by an unsupported version.");
        }

        var accounts = document.Accounts
            .Select(account => new AccountRecord(account.Id, account.DisplayName, account.Username))
            .ToArray();
        ValidateUnique(accounts);
        return accounts;
    }

    public async Task SaveAsync(IReadOnlyCollection<AccountRecord> accounts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ValidateUnique(accounts);
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("The metadata path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var document = new AccountDocument(
                CurrentVersion,
                accounts
                    .OrderBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(account => new MetadataAccount(account.Id, account.DisplayName, account.Username))
                    .ToArray());

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateUnique(IEnumerable<AccountRecord> accounts)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            if (!ids.Add(account.Id))
            {
                throw new InvalidDataException("Account metadata contains a duplicate ID.");
            }

            if (!names.Add(account.DisplayName))
            {
                throw new InvalidDataException("Account display names must be unique.");
            }
        }
    }

    private sealed record AccountDocument(int Version, IReadOnlyList<MetadataAccount> Accounts);

    private sealed record MetadataAccount(Guid Id, string DisplayName, string Username);
}
