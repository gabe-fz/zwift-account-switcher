using System.IO;
using ZwiftAccountSwitcher.Core.Launch;

namespace ZwiftAccountSwitcher.Windows.Launch;

public sealed class ZwiftInstallationLocator : IZwiftInstallationLocator
{
    private const string LauncherFileName = "ZwiftLauncher.exe";
    private readonly IReadOnlyList<string> knownPaths;

    public ZwiftInstallationLocator(IEnumerable<string>? knownPaths = null)
    {
        this.knownPaths = (knownPaths ?? BuildKnownPaths()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task<InstallationResult> ResolveAsync(string? savedOverride, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(savedOverride))
        {
            return Task.FromResult(IsValid(savedOverride)
                ? new InstallationResult(Path.GetFullPath(savedOverride))
                : new InstallationResult(null, InstallationFailure.InvalidOverride));
        }

        var found = knownPaths.FirstOrDefault(IsValid);
        return Task.FromResult(found is null
            ? new InstallationResult(null, InstallationFailure.NotFound)
            : new InstallationResult(Path.GetFullPath(found)));
    }

    public static bool IsValid(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetFileName(path), LauncherFileName, StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);

    private static IEnumerable<string> BuildKnownPaths()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Zwift", LauncherFileName);
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Zwift", LauncherFileName);
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Zwift", LauncherFileName);
        }
    }
}
