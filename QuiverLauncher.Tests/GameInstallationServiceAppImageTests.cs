using System.IO.Compression;
using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class GameInstallationServiceAppImageTests
{
    [Fact]
    public async Task InstallOrUpdateGameAsync_deletes_other_top_level_appimages()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var oldAppImage = Path.Combine(gamePath, "Game-1.0.AppImage");
        var modsFile = Path.Combine(gamePath, "mods", "keep.nrm");
        var portablePath = Path.Combine(gamePath, "portable.txt");
        var newPayload = new byte[] { 9, 8, 7, 6 };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(modsFile)!);
            await File.WriteAllBytesAsync(oldAppImage, [1, 2, 3]);
            await File.WriteAllTextAsync(portablePath, "portable");
            await File.WriteAllBytesAsync(modsFile, [4, 5]);
            await File.WriteAllBytesAsync(downloadPath, newPayload);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0");

            var installedPath = Path.Combine(gamePath, "Game-2.0.AppImage");
            File.Exists(installedPath).Should().BeTrue();
            (await File.ReadAllBytesAsync(installedPath)).Should().Equal(newPayload);
            File.Exists(oldAppImage).Should().BeFalse();
            (await File.ReadAllTextAsync(portablePath)).Should().Be("portable");
            File.Exists(modsFile).Should().BeTrue();
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v2.0.0");
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_overwrites_same_named_appimage_without_removing_other_files()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        const string assetName = "stable.AppImage";
        var installedPath = Path.Combine(gamePath, assetName);
        var portablePath = Path.Combine(gamePath, "portable.txt");
        var newPayload = new byte[] { 11, 12, 13 };

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(installedPath, [1, 1, 1]);
            await File.WriteAllTextAsync(portablePath, "keep");
            await File.WriteAllBytesAsync(downloadPath, newPayload);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                assetName,
                "v3.0.0");

            File.Exists(installedPath).Should().BeTrue();
            Directory.GetFiles(gamePath, "*.AppImage", SearchOption.TopDirectoryOnly)
                .Should().ContainSingle(path => Path.GetFileName(path) == assetName);
            (await File.ReadAllBytesAsync(installedPath)).Should().Equal(newPayload);
            (await File.ReadAllTextAsync(portablePath)).Should().Be("keep");
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_does_not_delete_nested_appimages()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var nestedAppImage = Path.Combine(gamePath, "subdir", "old.AppImage");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(nestedAppImage)!);
            await File.WriteAllBytesAsync(nestedAppImage, [2, 2, 2]);
            await File.WriteAllBytesAsync(downloadPath, [3, 3, 3]);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0");

            File.Exists(nestedAppImage).Should().BeTrue();
            File.Exists(Path.Combine(gamePath, "Game-2.0.AppImage")).Should().BeTrue();
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_retargets_stale_selected_executable()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var oldAppImage = Path.Combine(gamePath, "Game-1.0.AppImage");
        var selectedPath = Path.Combine(gamePath, "selected_executable.txt");

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(oldAppImage, [1]);
            await File.WriteAllTextAsync(selectedPath, oldAppImage);
            await File.WriteAllBytesAsync(downloadPath, [2]);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0");

            File.Exists(oldAppImage).Should().BeFalse();
            var saved = (await File.ReadAllTextAsync(selectedPath)).Trim();
            saved.Should().Be(Path.GetFullPath(Path.Combine(gamePath, "Game-2.0.AppImage")));
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_extensionless_binary_does_not_delete_sibling_appimage()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var siblingAppImage = Path.Combine(gamePath, "Game.AppImage");

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(siblingAppImage, [1, 2, 3]);
            await File.WriteAllBytesAsync(downloadPath, [4, 5, 6]);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "CrashBandicoot_Linux",
                "1.6.1");

            File.Exists(siblingAppImage).Should().BeTrue();
            File.Exists(Path.Combine(gamePath, "CrashBandicoot_Linux")).Should().BeTrue();
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_zip_does_not_delete_sibling_appimage()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var siblingAppImage = Path.Combine(gamePath, "Game.AppImage");
        var payload = "zip-binary"u8.ToArray();

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(siblingAppImage, [1, 2, 3]);
            await File.WriteAllBytesAsync(archivePath, CreateZip([("game.exe", payload)]));

            await GameInstallationService.InstallOrUpdateGameAsync(
                archivePath,
                gamePath,
                "game.zip",
                "v1.0.0");

            File.Exists(siblingAppImage).Should().BeTrue();
            File.ReadAllBytes(Path.Combine(gamePath, "game.exe")).Should().Equal(payload);
        }
        finally
        {
            Cleanup(archivePath, gamePath);
        }
    }

    [Theory]
    [InlineData("MyApp.AppImage", true)]
    [InlineData("MyApp.appimage", true)]
    [InlineData("game.exe", false)]
    [InlineData("payload.zip", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsAppImageAsset_classifies_names(string? assetName, bool expected)
    {
        GameInstallationService.IsAppImageAsset(assetName).Should().Be(expected);
    }

    static byte[] CreateZip(IReadOnlyList<(string Path, byte[] Content)> entries)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return zipStream.ToArray();
    }

    static void Cleanup(string downloadPath, string gamePath)
    {
        if (File.Exists(downloadPath))
            File.Delete(downloadPath);
        if (Directory.Exists(gamePath))
            Directory.Delete(gamePath, true);
    }
}
