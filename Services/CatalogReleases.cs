using System.Collections.Concurrent;
using System.Text.Json;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public enum ReleaseCheckState { Verified, Unverified, Blocked }

/// <summary>What quiverlauncher.com says about one release of a catalog app.</summary>
public sealed record ReleaseCheck(
    ReleaseCheckState State,
    IReadOnlyList<string> Reasons,
    string? VerifiedVersion,
    IReadOnlyDictionary<string, string> Checksums,
    string? ScanVerdict = null,
    string? ScanEngines = null,
    DateTimeOffset? VerifiedAt = null)
{
    /// <summary>The SHA-256 (hex) Quiver pinned for a file of this release, when it knows the file.</summary>
    public string? ChecksumFor(string fileName) => Checksums.GetValueOrDefault(fileName);
}

/// <summary>
/// Matches library apps to the quiverlauncher.com catalog and says which of their releases Quiver
/// verified. Catalog apps update only to the verified release; their downloads are checked against
/// the SHA-256 Quiver pinned. Apps that aren't in the catalog are left alone.
/// </summary>
public sealed class CatalogReleases
{
    internal const string NotSeen = "Quiver hasn't seen this release yet.";
    internal const string Unreachable = "Quiver Launcher couldn't reach quiverlauncher.com to check this release.";
    private static readonly TimeSpan StatusAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan HistoryAge = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly QuiverCatalogClient _client;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly ConcurrentDictionary<string, QuiverCatalogDetail> _details = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime At, IReadOnlyList<QuiverCatalogRelease> Releases)> _history = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<QuiverCatalogStatus> _status;
    private DateTime _statusAt = DateTime.MinValue;
    // When the release status was read from the site (or saved, for a copy read from disk).
    private DateTimeOffset? _statusCheckedAt;

    public CatalogReleases(QuiverCatalogClient client, string cacheDirectory)
    {
        _client = client;
        _cachePath = Path.Combine(cacheDirectory, "catalog-release-status.json");
        _status = ReadSaved();
        if (_status.Count > 0) _statusCheckedAt = File.GetLastWriteTimeUtc(_cachePath);
    }

    /// <summary>Reads the catalog's release status when it is old, then links each app.</summary>
    public async Task RefreshAsync(IEnumerable<GameInfo> apps, CancellationToken token)
    {
        var list = apps.ToList();
        if (!list.Any(a => !a.IsManuallyManaged && !string.IsNullOrWhiteSpace(a.Repository))) return;
        await RefreshStatusAsync(force: false, token).ConfigureAwait(false);
        // Apps sharing a repository are told apart by their download filter, which only the app's page has.
        foreach (var slug in list.SelectMany(Candidates).GroupBy(c => (c.Provider, Repo: c.Repository?.ToLowerInvariant()))
                     .Where(g => g.Count() > 1).SelectMany(g => g).Select(c => c.Slug).Distinct().ToList())
        {
            if (_details.ContainsKey(slug)) continue;
            try { _details[slug] = await _client.GetDetailAsync(slug, token).ConfigureAwait(false); }
            catch (Exception ex) when (!token.IsCancellationRequested) { System.Diagnostics.Debug.WriteLine($"Catalog app {slug} unavailable: {ex.Message}"); }
        }
        foreach (var app in list) ApplyKnown(app);
    }

    /// <summary>Links an app from what is already known, without the network.</summary>
    public void ApplyKnown(GameInfo app)
    {
        var link = Find(app);
        app.CatalogSlug = link?.Slug;
        app.CatalogVerifiedVersion = string.IsNullOrWhiteSpace(link?.Verified?.Version) ? null : link.Verified.Version;
        app.CatalogVerifiedAt = app.CatalogVerifiedVersion == null ? null : _statusCheckedAt;
    }

    /// <summary>Says whether Quiver verified this release of the app; null when the app isn't in the catalog.</summary>
    public async Task<ReleaseCheck?> CheckAsync(GameInfo app, string version, CancellationToken token)
    {
        await RefreshAsync([app], token).ConfigureAwait(false);
        if (app.CatalogSlug is not { } slug) return null;
        var verified = app.CatalogVerifiedVersion;
        IReadOnlyList<QuiverCatalogRelease>? releases;
        try { releases = await HistoryAsync(slug, token).ConfigureAwait(false); }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"Release history for {slug} unavailable: {ex.Message}");
            releases = null;
        }
        var release = releases?.FirstOrDefault(r => ReleaseVersionIdentity.AreVersionsEquivalent(r.Version, version));
        if (release == null)
        {
            // The status feed alone still knows which release is verified, though not its files.
            if (ReleaseVersionIdentity.AreVersionsEquivalent(verified, version))
                return new(ReleaseCheckState.Verified, [], verified, new Dictionary<string, string>());
            return new(ReleaseCheckState.Unverified, [releases == null ? Unreachable : NotSeen], verified, new Dictionary<string, string>());
        }
        var state = release.State switch
        {
            "verified" => ReleaseCheckState.Verified,
            "blocked" => ReleaseCheckState.Blocked,
            _ => ReleaseCheckState.Unverified,
        };
        var checksums = release.Assets
            .Where(a => a.Checksum?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            .GroupBy(a => a.Filename, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Checksum!["sha256:".Length..].ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
        return new(state, release.Reasons, verified, checksums, release.Scan?.Verdict, release.Scan?.Engines,
            release.CheckEndsAt is { } ends ? DateTimeOffset.FromUnixTimeMilliseconds((long)ends) : null);
    }

    private async Task<IReadOnlyList<QuiverCatalogRelease>> HistoryAsync(string slug, CancellationToken token)
    {
        lock (_history)
            if (_history.TryGetValue(slug, out var cached) && DateTime.UtcNow - cached.At < HistoryAge)
                return cached.Releases;
        var page = await _client.GetReleaseHistoryAsync(slug, token).ConfigureAwait(false);
        lock (_history) _history[slug] = (DateTime.UtcNow, page.Items);
        return page.Items;
    }

    private async Task RefreshStatusAsync(bool force, CancellationToken token)
    {
        if (!force && DateTime.UtcNow - _statusAt < StatusAge) return;
        await _refresh.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!force && DateTime.UtcNow - _statusAt < StatusAge) return;
            var items = new List<QuiverCatalogStatus>();
            string? cursor = null;
            do
            {
                var page = await _client.GetReleaseStatusAsync(cursor, token).ConfigureAwait(false);
                items.AddRange(page.Items);
                cursor = page.NextCursor;
            } while (cursor != null);
            _status = items;
            _statusAt = DateTime.UtcNow;
            _statusCheckedAt = DateTimeOffset.UtcNow;
            Save(items);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            // Offline: keep the last status this launcher saved, and try again later.
            System.Diagnostics.Debug.WriteLine($"Catalog release status unavailable: {ex.Message}");
            _statusAt = DateTime.UtcNow - StatusAge + TimeSpan.FromMinutes(2);
        }
        finally { _refresh.Release(); }
    }

    private IEnumerable<QuiverCatalogStatus> Candidates(GameInfo app)
    {
        if (app.IsManuallyManaged || string.IsNullOrWhiteSpace(app.Repository)) return [];
        var source = app.EffectiveRepositorySource;
        var repository = app.Repository.Trim();
        return _status.Where(s => string.Equals(s.Provider, source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Repository?.Trim(), repository, StringComparison.OrdinalIgnoreCase));
    }

    private QuiverCatalogStatus? Find(GameInfo app)
    {
        var candidates = Candidates(app).ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();
        var filter = RepositorySourceHelper.NormalizeReleaseAssetFilter(app.ReleaseAssetFilter) ?? "";
        var byFilter = candidates.Where(c => _details.TryGetValue(c.Slug, out var d) && string.Equals(
            RepositorySourceHelper.NormalizeReleaseAssetFilter(d.Entry.Launcher.ReleaseAssetFilter) ?? "", filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byFilter.Count == 1) return byFilter[0];
        var byFolder = candidates.Where(c => _details.TryGetValue(c.Slug, out var d) &&
            string.Equals(d.Entry.Launcher.FolderName?.Trim(), app.FolderName?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        // Still ambiguous: better unlinked than checked against another app's files.
        return byFolder.Count == 1 ? byFolder[0] : null;
    }

    private IReadOnlyList<QuiverCatalogStatus> ReadSaved()
    {
        try { return File.Exists(_cachePath) ? JsonSerializer.Deserialize<List<QuiverCatalogStatus>>(File.ReadAllText(_cachePath), Json) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private void Save(List<QuiverCatalogStatus> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(items, Json));
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Catalog release status not saved: {ex.Message}");
        }
    }
}
