using System.Text.Json;

namespace QuiverLauncher.Core.Services;

/// <summary>Validators are meaningful only alongside the payload for the same URL and credentials.</summary>
public sealed class ReleaseEndpointCache
{
    public sealed record Entry(string Body, string? ETag, DateTimeOffset ValidatedAt);
    private readonly object _gate = new();
    private readonly string? _path;
    private readonly Dictionary<string, Entry> _entries;

    public ReleaseEndpointCache(string? directory = null)
    {
        _path = directory == null ? null : Path.Combine(directory, "release_endpoints_v1.json");
        try
        {
            _entries = _path != null && File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _entries = []; }
    }

    public Entry? Get(string key) { lock (_gate) return _entries.GetValueOrDefault(key); }

    public void Set(string key, Entry entry)
    {
        lock (_gate)
        {
            _entries[key] = entry;
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_entries));
                File.Move(_path + ".tmp", _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine("Could not persist release endpoint cache."); }
        }
    }
}
