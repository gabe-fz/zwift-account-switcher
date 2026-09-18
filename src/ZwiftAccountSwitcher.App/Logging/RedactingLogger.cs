using System.IO;
using System.Text;
using ZwiftAccountSwitcher.Core.Launch;

namespace ZwiftAccountSwitcher.App.Logging;

public sealed class RedactingLogger
{
    private readonly string logPath;
    private readonly object sync = new();

    public RedactingLogger(string? logPath = null)
    {
        this.logPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZwiftAccountSwitcher",
            "logs",
            "app.log");
    }

    public void LogState(LoginState state) => Write("state", state.ToString());

    public void LogOutcome(LoginFailureKind outcome) => Write("outcome", outcome.ToString());

    public void LogStorageFailure(string operation, Exception exception) =>
        Write("storage", $"{SanitizeToken(operation)}:{exception.GetType().Name}");

    private void Write(string category, string value)
    {
        try
        {
            lock (sync)
            {
                var directory = Path.GetDirectoryName(logPath);
                if (directory is null)
                {
                    return;
                }

                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    logPath,
                    $"{DateTimeOffset.UtcNow:O} {SanitizeToken(category)}={SanitizeToken(value)}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Diagnostics must never interrupt account switching.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must never interrupt account switching.
        }
    }

    private static string SanitizeToken(string value) =>
        new(value.Where(character => char.IsLetterOrDigit(character) || character is '_' or '-' or ':').Take(80).ToArray());
}
