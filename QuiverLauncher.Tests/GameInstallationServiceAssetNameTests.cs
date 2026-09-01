using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class GameInstallationServiceAssetNameTests
{
    [Theory]
    [InlineData("LADXHD.Patcher-Lite-Windows", "LADXHD.Patcher-Lite-Windows.7z", "LADXHD.Patcher-Lite-Windows.7z")]
    [InlineData("app-win.zip", "other.7z", "app-win.zip")]
    [InlineData("app-win.zip", null, "app-win.zip")]
    [InlineData(null, "payload.7z", "payload.7z")]
    public void ResolveEffectiveAssetName_prefers_disposition_when_name_lacks_extension(
        string? assetName,
        string? disposition,
        string expected)
    {
        GameInstallationService.ResolveEffectiveAssetName(assetName, disposition).Should().Be(expected);
    }

    [Theory]
    [InlineData("app-android.apk", true)]
    [InlineData("game.zip", true)]
    [InlineData("game.rar", true)]
    [InlineData("setup.exe", true)]
    [InlineData("notes.txt", false)]
    public void HasRecognizedInstallExtension_includes_apk(string assetName, bool expected)
    {
        GameInstallationService.HasRecognizedInstallExtension(assetName).Should().Be(expected);
    }

    [Fact]
    public void IsAndroidPackageAsset_detects_apk()
    {
        GameInstallationService.IsAndroidPackageAsset("app-android.apk").Should().BeTrue();
        GameInstallationService.IsAndroidPackageAsset("app-win.zip").Should().BeFalse();
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_rejects_apk_on_desktop()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.apk");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            File.WriteAllBytes(downloadPath, [0x50, 0x4B, 0x03, 0x04]);

            var act = () => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "app-android.apk",
                "v1.0.0");

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*cannot be installed on this desktop platform*");
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
    public void DetectArchiveExtensionFromFile_detects_7z_signature()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [0x37, 0x7A, 0xBC, 0xAF, 0x00, 0x01]);
            GameInstallationService.DetectArchiveExtensionFromFile(path).Should().Be(".7z");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void DetectArchiveExtensionFromFile_detects_rar_signature()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);
            GameInstallationService.DetectArchiveExtensionFromFile(path).Should().Be(".rar");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_extracts_7z_when_asset_name_lacks_extension()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-release-flat.7z");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"LADXHD.Patcher-Lite-Windows-{Guid.NewGuid():N}");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            File.Copy(fixture, downloadPath);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "LADXHD.Patcher-Lite-Windows",
                "v1.0.0");

            (await File.ReadAllTextAsync(Path.Combine(gamePath, "game.exe"))).Should().Be("flat-binary");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v1.0.0");
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
    public async Task InstallOrUpdateGameAsync_extracts_rar_when_asset_name_lacks_extension()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-release-flat.rar");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"LADXHD.Patcher-Lite-Windows-{Guid.NewGuid():N}");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            File.Copy(fixture, downloadPath);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "LADXHD.Patcher-Lite-Windows",
                "v1.0.0");

            (await File.ReadAllTextAsync(Path.Combine(gamePath, "game.exe"))).Should().Be("flat-binary");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v1.0.0");
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
