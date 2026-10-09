using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuiverLauncher.Core.Services;

/// <summary>Validators are meaningful only alongside the payload for the same URL and credentials.</summary>
/// <remarks>
/// Each payload is its own file, read only when it is needed (a 304 or a recent enough copy), and the
/// small index of validators is all that stays in memory. A release list can be a few hundred KB, so
/// keeping every payload in one file meant holding tens of MB of text for the whole session and
/// writing all of it again after every check.
/// </remarks>
public sealed class ReleaseEndpointCache
{
    public sealed record Entry(string Body, string? ETag, DateTimeOffset ValidatedAt);
    public sealed record RateLimitEntry(long? Limit, long Remaining, DateTimeOffset? ResetAt, DateTimeOffset ObservedAt);
    private sealed record Validator(string? ETag, DateTimeOffset ValidatedAt);

    private readonly object _gate = new();
    private readonly string? _directory;
    private readonly string? _indexPath;
    private readonly string? _rateLimitPath;
    private readonly Dictionary<string, Validator> _validators = [];
    private readonly Dictionary<string, RateLimitEntry> _rateLimits = [];
    // Payloads only for a cache that has nowhere to save them.
    private readonly Dictionary<string, string> _bodies = [];

    public ReleaseEndpointCache(string? directory = null)
    {
        if (directory == null) return;
        _directory = Path.Combine(directory, "release_endpoints_v2");
        _indexPath = Path.Combine(_directory, "index.json");
        _rateLimitPath = Path.Combine(directory, "release_rate_limits_v1.json");
        try
        {
            if (File.Exists(_indexPath))
            {
                using var stream = File.OpenRead(_indexPath);
                _validators = JsonSerializer.Deserialize<Dictionary<string, Validator>>(stream) ?? [];
            }
            else
                Migrate(Path.Combine(directory, "release_endpoints_v1.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _validators = []; }
        try
        {
            if (File.Exists(_rateLimitPath))
            {
                using var stream = File.OpenRead(_rateLimitPath);
                _rateLimits = JsonSerializer.Deserialize<Dictionary<string, RateLimitEntry>>(stream) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _rateLimits = []; }
    }

    public Entry? Get(string key)
    {
        lock (_gate)
        {
            if (!_validators.TryGetValue(key, out var validator)) return null;
            string body;
            if (_directory == null) body = _bodies[key];
            else
            {
                try { body = File.ReadAllText(BodyPath(key)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
            }
            return new(body, validator.ETag, validator.ValidatedAt);
        }
    }

    public string? ETag(string key) { lock (_gate) return _validators.GetValueOrDefault(key)?.ETag; }

    public DateTimeOffset? ValidatedAt(string key) { lock (_gate) return _validators.GetValueOrDefault(key)?.ValidatedAt; }

    public void Set(string key, Entry entry)
    {
        lock (_gate)
        {
            if (_directory == null) _bodies[key] = entry.Body;
            else if (!TryWrite(BodyPath(key), entry.Body)) return;
            _validators[key] = new(entry.ETag, entry.ValidatedAt);
            SaveIndex();
        }
    }

    /// <summary>Records that the saved payload is still current (a 304) without writing it again.</summary>
    public void Revalidate(string key, string? etag, DateTimeOffset validatedAt)
    {
        lock (_gate)
        {
            if (!_validators.ContainsKey(key)) return;
            _validators[key] = new(etag, validatedAt);
            SaveIndex();
        }
    }

    public RateLimitEntry? GetRateLimit(string key) { lock (_gate) return _rateLimits.GetValueOrDefault(key); }

    public void SetRateLimit(string key, RateLimitEntry entry)
    {
        lock (_gate)
        {
            _rateLimits[key] = entry;
            SaveRateLimits();
        }
    }

    public RateLimitEntry UpdateRateLimit(string key, Func<RateLimitEntry?, RateLimitEntry> update)
    {
        lock (_gate)
        {
            var entry = update(_rateLimits.GetValueOrDefault(key));
            _rateLimits[key] = entry;
            SaveRateLimits();
            return entry;
        }
    }

    private string BodyPath(string key) =>
        Path.Combine(_directory!, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

    private void SaveIndex()
    {
        if (_indexPath != null) TryWrite(_indexPath, JsonSerializer.Serialize(_validators));
    }

    private void SaveRateLimits()
    {
        if (_rateLimitPath == null) return;
        try
        {
            var directory = Path.GetDirectoryName(_rateLimitPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_rateLimitPath + ".tmp", JsonSerializer.Serialize(_rateLimits));
            File.Move(_rateLimitPath + ".tmp", _rateLimitPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine("Could not persist release rate-limit cache."); }
    }

    private bool TryWrite(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(_directory!);
            File.WriteAllText(path + ".tmp", text);
            File.Move(path + ".tmp", path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Could not persist release endpoint cache.");
            return false;
        }
    }

    // Keeps the validators from the single-file cache, so the first check after updating is still conditional.
    private void Migrate(string oldPath)
    {
        if (!File.Exists(oldPath)) return;
        try
        {
            Dictionary<string, Entry>? old;
            using (var stream = File.OpenRead(oldPath))
                old = JsonSerializer.Deserialize<Dictionary<string, Entry>>(stream);
            foreach (var (key, entry) in old ?? [])
                if (TryWrite(BodyPath(key), entry.Body))
                    _validators[key] = new(entry.ETag, entry.ValidatedAt);
            SaveIndex();
            File.Delete(oldPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { System.Diagnostics.Debug.WriteLine("Could not migrate release endpoint cache."); }
    }
}
