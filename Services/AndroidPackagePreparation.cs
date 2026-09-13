using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

/// <summary>Prepares an Android installer payload without changing the installed game.</summary>
public static class AndroidPackagePreparation
{
    public static async Task<string> PrepareAsync(string downloadPath, string assetName, string stagingDirectory)
    {
        if (GameInstallationService.IsAndroidPackageAsset(assetName)) return downloadPath;

        var archiveExtension = GameInstallationService.DetectArchiveExtensionFromFile(downloadPath);
        if (archiveExtension == null)
            throw new InvalidDataException("This Android download is neither an APK nor a supported archive containing an APK.");

        var payloadDirectory = Path.Combine(stagingDirectory, "android-payload");
        await GameInstallationService.InstallOrUpdateGameAsync(downloadPath, payloadDirectory,
            "payload" + archiveExtension, "", new GameInstallationOptions()).ConfigureAwait(false);
        var packages = Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories)
            .Where(GameInstallationService.IsAndroidPackageAsset).ToList();
        return packages.Count switch
        {
            1 => packages[0],
            0 => throw new InvalidDataException("The downloaded archive contains no APK to install. Choose an Android APK download."),
            _ => throw new InvalidDataException("The downloaded archive contains multiple APKs. Choose a release download containing one APK; split APK bundles are not supported."),
        };
    }
}
