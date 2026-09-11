using System.Collections.Concurrent;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services;

public sealed record CatalogPlatformEntry(string ReleaseTag, string[] AssetNames, DateTimeOffset ValidatedAt, int SelectionRevision = 0);

/// <summary>Release-selection-specific metadata. Library version checks never write here.</summary>
public static class CatalogPlatformIndex
{
    private static readonly ConcurrentDictionary<string, CatalogPlatformEntry> Entries = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CatalogPlatformEntry> Legacy = new(StringComparer.Ordinal);
    private static readonly object DiskGate = new();
    private static string? _path;
    public static string Key(string? provider, string repository, string? preferredVersion = null, string? token = null) =>
        JsonSerializer.Serialize(new[] { RepositorySourceHelper.Normalize(provider),
            RepositorySourceHelper.IsGitHub(provider) ? repository.Trim().ToLowerInvariant() : repository.Trim(),
            preferredVersion?.Trim() ?? "", ReleaseRequestCoordinator.CredentialKey(token) });

    public static void Initialize(string directory)
    {
        lock (DiskGate)
        {
            Entries.Clear(); Legacy.Clear();
            PublishedPlatformCache.Initialize(directory);
            _path = Path.Combine(directory, "catalog_platform_index_v1.json");
            try
            {
                if (File.Exists(_path))
                    foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, CatalogPlatformEntry>>(File.ReadAllText(_path)) ?? [])
                        Entries[entry.Key] = entry.Value;
                var legacyPath = Path.Combine(directory, "version_cache.json");
                if (File.Exists(legacyPath))
                    foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, GameVersionCache>>(File.ReadAllText(legacyPath)) ?? [])
                    {
                        var split = entry.Key.IndexOf(':');
                        var provider = split < 0 ? "github" : entry.Key[..split];
                        var repo = split < 0 ? entry.Key : entry.Key[(split + 1)..];
                        var names = entry.Value.AssetNames.Count > 0 ? entry.Value.AssetNames : GitHubApiCache.ExtractAssetNames(entry.Value.CachedRelease);
                        if (names.Count > 0 || entry.Value.CachedRelease != null)
                            Legacy[Key(provider, repo)] = new(entry.Value.Version, names.ToArray(), DateTimeOffset.MinValue);
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }

    public static bool TryGet(string? provider, string? repository, string? preferredVersion, string? token, out CatalogPlatformEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(repository)) return false;
        var candidates = new List<CatalogPlatformEntry>();
        if (Entries.TryGetValue(Key(provider, repository, preferredVersion, token), out var own)) candidates.Add(own);
        // Anonymous public successes remain usable after saving a token. Authenticated results never cross contexts.
        if (Entries.TryGetValue(Key(provider, repository, preferredVersion), out var anonymous)) candidates.Add(anonymous);
        if (PublishedPlatformCache.TryGet(RepositorySourceHelper.Normalize(provider), repository, preferredVersion, out var published)) candidates.Add(published!);
        if (string.IsNullOrWhiteSpace(preferredVersion) && Legacy.TryGetValue(Key(provider, repository), out var legacy)) candidates.Add(legacy);
        entry = candidates.OrderByDescending(e => e.ValidatedAt).FirstOrDefault();
        return entry != null;
    }
    public static bool IsFresh(string? provider, string repository, string? preferredVersion = null, string? token = null) =>
        TryGet(provider, repository, preferredVersion, token, out var entry) &&
        (entry!.AssetNames.Length > 0 || entry.SelectionRevision >= 1) &&
        DateTimeOffset.UtcNow - entry.ValidatedAt < TimeSpan.FromHours(24);

    public static void Set(string? provider, string repository, string? preferredVersion, string? token, GitHubRelease? release)
    {
        Entries[Key(provider, repository, preferredVersion, token)] = new(release?.tag_name ?? "",
            GitHubApiCache.ExtractAssetNames(release).ToArray(), DateTimeOffset.UtcNow, SelectionRevision: 1);
    }
    public static void Flush()
    {
        lock (DiskGate)
        {
            if (_path == null) return;
            try
            {
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(Entries));
                File.Move(temporary, _path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
