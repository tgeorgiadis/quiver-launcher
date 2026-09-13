using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace QuiverLauncher.Services;

public class FileSettingsStore : ISettingsStore
{
    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _settingsPath;
    private AppSettings _current;

    public FileSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? QuiverLauncherPaths.SettingsJsonPath;
        _current = ReadFromDisk();
    }

    public AppSettings Current => _current;

    public AppSettings Load()
    {
        var loaded = ReadFromDisk();
        // These summaries are intentionally not serialized. Reloading preferences
        // must not erase already computed catalog membership and review counts.
        var previous = _current;
        foreach (var source in loaded.AppCatalogSources)
        {
            var old = previous.AppCatalogSources.FirstOrDefault(s => s.Id == source.Id);
            if (old == null || old.Location != source.Location ||
                old.CachedListVersion != source.CachedListVersion || old.LastFetchedUtc != source.LastFetchedUtc)
                continue;
            source.LibraryAppCount = old.LibraryAppCount;
            source.ListAppCount = old.ListAppCount;
            if (old.AcknowledgedListVersion == source.AcknowledgedListVersion &&
                old.UpdateAvailable == source.UpdateAvailable &&
                previous.GitHubApiToken == loaded.GitHubApiToken && previous.GitLabApiToken == loaded.GitLabApiToken &&
                old.HiddenFromReviewRepositories.SequenceEqual(source.HiddenFromReviewRepositories) &&
                old.IgnoredChangesAtVersion.Count == source.IgnoredChangesAtVersion.Count &&
                old.IgnoredChangesAtVersion.All(pair => source.IgnoredChangesAtVersion.TryGetValue(pair.Key, out var value) && value == pair.Value))
            {
                source.PendingReviewCount = old.PendingReviewCount;
                source.PlatformExcludedReviewCount = old.PlatformExcludedReviewCount;
            }
        }
        _current = loaded;
        return _current;
    }

    public void Save(AppSettings settings)
    {
        settings.EnsureInitialized();
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        var pathLock = PathLocks.GetOrAdd(_settingsPath, _ => new object());
        lock (pathLock)
        {
            WriteWithRetry(json);
            _current = settings;
        }
    }

    private void WriteWithRetry(string json)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _settingsPath + ".tmp";
        Exception? lastError = null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                if (File.Exists(_settingsPath))
                    File.Replace(tempPath, _settingsPath, null);
                else
                    File.Move(tempPath, _settingsPath);

                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                TryDeleteTempFile(tempPath);
                Thread.Sleep(40 * (attempt + 1));
            }
        }

        throw new IOException(
            $"Could not save settings to '{_settingsPath}'. Close any other running Quiver Launcher instances and try again.",
            lastError);
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private AppSettings ReadFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return CreateDefault();

            var json = ReadAllTextShared(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
                return CreateDefault();

            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefault();
            settings.EnsureInitialized();
            return settings;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            return CreateDefault();
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        return settings;
    }
}
