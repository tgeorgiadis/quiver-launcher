using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class GameInstallationServiceRarTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [Fact]
    public async Task InstallOrUpdateGameAsync_extracts_rar_asset()
    {
        var archivePath = FixturePath("sample-release-flat.rar");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.rar");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            File.Copy(archivePath, downloadPath);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "game-win.rar",
                "v1.2.3");

            (await File.ReadAllTextAsync(Path.Combine(gamePath, "game.exe"))).Should().Be("flat-binary");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "readme.txt"))).Should().Be("flat-readme");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v1.2.3");
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_strips_single_root_directory_from_rar()
    {
        var archivePath = FixturePath("sample-release-wrapped.rar");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.rar");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            File.Copy(archivePath, downloadPath);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "AppRoot-release.rar",
                "v2.0.0");

            (await File.ReadAllTextAsync(Path.Combine(gamePath, "game.exe"))).Should().Be("wrapped-binary");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "readme.txt"))).Should().Be("wrapped-readme");
            Directory.Exists(Path.Combine(gamePath, "AppRoot")).Should().BeFalse();
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v2.0.0");
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }
}
