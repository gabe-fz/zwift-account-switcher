namespace ZwiftAccountSwitcher.Core.Accounts;

public sealed record AccountRecord
{
    public AccountRecord(Guid id, string displayName, string username)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(id));
        }

        Id = id;
        DisplayName = RequireValue(displayName, nameof(displayName), 80);
        Username = RequireValue(username, nameof(username), 320);
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string Username { get; }

    private static string RequireValue(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName);
        }

        return trimmed;
    }
}
