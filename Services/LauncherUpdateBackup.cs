using System.Security.Cryptography;
using System.Text.Json;

namespace QuiverLauncher.Services;

/// <summary>Preserves the exact user files before staging or applying a self-update.</summary>
public static class LauncherUpdateBackup
{
    public const int MaxBackupCount = 10;
    public const long MaxBackupBytes = 25L * 1024 * 1024;
    public static string Create(string? dataRoot = null)
    {
        dataRoot ??= QuiverLauncherPaths.UserDataRoot;
        var destination = Path.Combine(dataRoot, "Backups", "updates",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N"));
        var staging = destination + ".incomplete";
        var sources = new Dictionary<string, FileStream?>();
        try
        {
            // Hold both files open without write/delete sharing until the snapshot is
            // committed. An active writer causes a safe failure, never a partial copy.
            foreach (var name in new[] { "apps.json", "settings.json" })
            {
                try { sources[name] = new FileStream(Path.Combine(dataRoot, name), FileMode.Open, FileAccess.Read, FileShare.Read); }
                catch (FileNotFoundException) { sources[name] = null; }
            }
            Directory.CreateDirectory(staging);
            var hashes = new Dictionary<string, string?>();
            foreach (var (name, source) in sources)
            {
                if (source == null) { hashes[name] = null; continue; }
                var target = Path.Combine(staging, name);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                source.Position = 0;
                var expected = SHA256.HashData(source);
                using var copied = File.OpenRead(target);
                if (!expected.AsSpan().SequenceEqual(SHA256.HashData(copied)))
                    throw new IOException($"Could not verify the backup of {name}.");
                hashes[name] = Convert.ToHexString(expected);
            }
            using (var manifest = new FileStream(Path.Combine(staging, "manifest.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(manifest, new { createdUtc = DateTime.UtcNow, files = hashes },
                    new JsonSerializerOptions { WriteIndented = true });
                manifest.Flush(flushToDisk: true);
            }
            Directory.Move(staging, destination);
            Prune(dataRoot);
            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Quiver update stopped because apps.json and settings.json could not be backed up. " +
                $"Your original files have not been changed. Check access and free space in {Path.Combine(dataRoot, "Backups", "updates")}, then retry. {ex.Message}", ex);
        }
        finally
        {
            foreach (var source in sources.Values) source?.Dispose();
        }
    }

    private static void Prune(string dataRoot)
    {
        try
        {
            var root = new DirectoryInfo(Path.Combine(dataRoot, "Backups", "updates"));
            var folders = root.EnumerateDirectories()
                .Where(folder => !folder.Name.EndsWith(".incomplete", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(folder.FullName, "manifest.json")))
                .OrderBy(folder => folder.LastWriteTimeUtc)
                .ThenBy(folder => folder.Name, StringComparer.Ordinal)
                .ToList();
            var total = folders.Sum(SizeOf);
            var newest = folders.LastOrDefault();
            foreach (var folder in folders.ToList())
            {
                if (folder == newest || (folders.Count <= MaxBackupCount && total <= MaxBackupBytes)) break;
                try
                {
                    var size = SizeOf(folder);
                    folder.Delete(recursive: true);
                    folders.Remove(folder);
                    total -= size;
                }
                catch (IOException) { break; }
                catch (UnauthorizedAccessException) { break; }
            }
        }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static long SizeOf(DirectoryInfo folder)
    {
        try { return folder.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    internal static async Task BeforeUpdateAsync(Func<Task> update, string? dataRoot = null)
    {
        Create(dataRoot);
        await update().ConfigureAwait(false);
    }

    internal static void BeforeUpdate(Action update, string? dataRoot = null)
    {
        Create(dataRoot);
        update();
    }
}
