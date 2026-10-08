using System.Diagnostics;

namespace QuiverLauncher.Services;

/// <summary>
/// Before 3.5.0, a Windows install at the top of a drive (G:\) kept the player's library inside
/// current\, the folder the updater moves to packages\VelopackTemp and deletes on every update.
/// This moves that data back beside current\, where Quiver now keeps it.
/// </summary>
public static class UserDataRescue
{
    /// <summary>Everything Quiver keeps in its data folder.</summary>
    internal static readonly string[] DataEntries =
    [
        "apps.json",
        "settings.json",
        "games.json",
        "Apps",
        "Cache",
        "Backups",
        "crash.log",
        GamepadDebugLog.FileName,
        LaunchDebugReport.UserDataFileName,
    ];

    // Downloads and thumbnails: not worth merging file by file when both copies exist.
    private const string CacheEntry = "Cache";

    /// <summary>
    /// Runs in the new version while the updater still holds the old current\ in
    /// packages\VelopackTemp, before it deletes that folder.
    /// </summary>
    public static void AfterUpdate(string? rootAppDir)
    {
        if (string.IsNullOrWhiteSpace(rootAppDir))
            return;

        var dataRoot = QuiverLauncherPaths.NormalizeDirectory(rootAppDir);
        Rescue(UpdaterTempFolders(dataRoot), dataRoot);
    }

    /// <summary>
    /// Data an earlier version left in current\, or in an old copy of it the updater didn't
    /// finish deleting.
    /// </summary>
    public static void AtStartup(string? rootAppDir, string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootAppDir) || string.IsNullOrWhiteSpace(appDirectory))
            return;

        var dataRoot = QuiverLauncherPaths.NormalizeDirectory(rootAppDir);
        var current = QuiverLauncherPaths.NormalizeDirectory(appDirectory);
        if (string.Equals(dataRoot, current, StringComparison.OrdinalIgnoreCase))
            return;

        Rescue([current, .. UpdaterTempFolders(dataRoot)], dataRoot);
    }

    internal static IEnumerable<string> UpdaterTempFolders(string dataRoot)
    {
        try
        {
            var temp = Path.Combine(dataRoot, "packages", "VelopackTemp");
            return Directory.Exists(temp)
                ? Directory.GetDirectories(temp, "tmp_*")
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Moves each data entry from the sources into <paramref name="dataRoot"/> where it isn't
    /// already there. The source with the newest apps.json goes first. Returns what was moved.
    /// </summary>
    internal static List<string> Rescue(IEnumerable<string> sources, string dataRoot)
    {
        var moved = new List<string>();
        var ordered = sources
            .Where(source => !string.Equals(
                Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            .Where(source => DataEntries.Any(entry => Exists(Path.Combine(source, entry))))
            .OrderByDescending(source => File.GetLastWriteTimeUtc(Path.Combine(source, "apps.json")))
            .ToList();

        foreach (var source in ordered)
        {
            foreach (var entry in DataEntries)
            {
                try
                {
                    MoveMissing(Path.Combine(source, entry), Path.Combine(dataRoot, entry), entry != CacheEntry, moved);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.WriteLine($"Could not move {Path.Combine(source, entry)} to {dataRoot}: {ex.Message}");
                }
            }
        }

        if (moved.Count > 0)
            Trace.WriteLine($"Moved Quiver data into {dataRoot}: {string.Join(", ", moved)}");
        return moved;
    }

    private static void MoveMissing(string source, string target, bool merge, List<string> moved)
    {
        if (File.Exists(source))
        {
            if (!Exists(target))
            {
                File.Move(source, target);
                moved.Add(source);
            }
            return;
        }

        if (!Directory.Exists(source))
            return;

        if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any())
            Directory.Delete(target);

        if (!Exists(target))
        {
            Directory.Move(source, target);
            moved.Add(source);
            return;
        }

        if (!merge || !Directory.Exists(target))
            return;

        foreach (var child in Directory.GetFileSystemEntries(source))
            MoveMissing(child, Path.Combine(target, Path.GetFileName(child)), merge, moved);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
