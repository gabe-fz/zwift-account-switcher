using System.Text.Json;
using ZwiftAccountSwitcher.Core.Accounts;
using ZwiftAccountSwitcher.Windows.Accounts;

namespace ZwiftAccountSwitcher.Windows.Tests;

public sealed class AccountStorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"zas-metadata-{Guid.NewGuid():N}");

    [Fact]
    public async Task AccountStorage_RoundTripsOnlyNonSecretMetadata()
    {
        var path = Path.Combine(directory, "accounts.json");
        var repository = new JsonAccountRepository(path);
        var expected = new[]
        {
            new AccountRecord(Guid.NewGuid(), "Rider Two", "second@example.invalid"),
            new AccountRecord(Guid.NewGuid(), "Rider One", "first@example.invalid"),
        };

        await repository.SaveAsync(expected);
        var actual = await repository.LoadAsync();

        Assert.Equal(expected.OrderBy(item => item.DisplayName), actual.OrderBy(item => item.DisplayName));
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(json);
        var rootProperties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "version", "accounts" }, rootProperties);
        foreach (var account in document.RootElement.GetProperty("accounts").EnumerateArray())
        {
            var properties = account.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(new[] { "id", "displayName", "username" }, properties);
        }
    }

    [Fact]
    public async Task AccountStorage_ReplacesExistingFileAndRejectsDuplicateNames()
    {
        var repository = new JsonAccountRepository(Path.Combine(directory, "accounts.json"));
        var first = new AccountRecord(Guid.NewGuid(), "Rider", "first@example.invalid");
        await repository.SaveAsync(new[] { first });
        var replacement = new AccountRecord(first.Id, "Updated Rider", "updated@example.invalid");

        await repository.SaveAsync(new[] { replacement });

        Assert.Equal(new[] { replacement }, await repository.LoadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(new[]
        {
            replacement,
            new AccountRecord(Guid.NewGuid(), "updated rider", "other@example.invalid"),
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
