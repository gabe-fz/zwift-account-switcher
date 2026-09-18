using ZwiftAccountSwitcher.Core.Launch;
using ZwiftAccountSwitcher.Windows.Launch;

namespace ZwiftAccountSwitcher.Windows.Tests;

public sealed class InstallationLocatorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"zas-install-{Guid.NewGuid():N}");

    [Fact]
    public async Task InstallationLocator_UsesAValidOverride()
    {
        Directory.CreateDirectory(directory);
        var launcher = Path.Combine(directory, "ZwiftLauncher.exe");
        await File.WriteAllBytesAsync(launcher, Array.Empty<byte>());
        var locator = new ZwiftInstallationLocator(Array.Empty<string>());

        var result = await locator.ResolveAsync(launcher);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(launcher), result.LauncherPath);
    }

    [Fact]
    public async Task InstallationLocator_RejectsWrongFileNameWithoutFallingBack()
    {
        Directory.CreateDirectory(directory);
        var wrongFile = Path.Combine(directory, "Other.exe");
        await File.WriteAllBytesAsync(wrongFile, Array.Empty<byte>());
        var knownLauncher = Path.Combine(directory, "ZwiftLauncher.exe");
        await File.WriteAllBytesAsync(knownLauncher, Array.Empty<byte>());
        var locator = new ZwiftInstallationLocator(new[] { knownLauncher });

        var result = await locator.ResolveAsync(wrongFile);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallationFailure.InvalidOverride, result.Failure);
    }

    [Fact]
    public async Task InstallationLocator_FindsKnownLocation()
    {
        Directory.CreateDirectory(directory);
        var launcher = Path.Combine(directory, "ZwiftLauncher.exe");
        await File.WriteAllBytesAsync(launcher, Array.Empty<byte>());
        var locator = new ZwiftInstallationLocator(new[] { Path.Combine(directory, "missing", "ZwiftLauncher.exe"), launcher });

        var result = await locator.ResolveAsync(null);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(launcher), result.LauncherPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
