namespace QuiverLauncher.Core.Services;

internal static class DownloadStaging
{
    internal const string FolderName = "QuiverDownloads";

    internal static string GetDesktopRoot() =>
        Path.Combine(Path.GetTempPath(), FolderName);

    internal static (string StagingDir, string FilePath) CreateStagedDownload(string downloadRoot, string assetName)
    {
        var safeName = Path.GetFileName(assetName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "download.bin";

        var stagingDir = Path.Combine(downloadRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        return (stagingDir, Path.Combine(stagingDir, safeName));
    }

    internal static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of a staging folder.
        }
    }
}
