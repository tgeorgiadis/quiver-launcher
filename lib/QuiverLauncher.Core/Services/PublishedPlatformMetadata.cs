using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuiverLauncher.Core.Services;

public sealed record PublishedPlatformTarget(string Provider, string Repository, string? PreferredRelease = null)
{
    [JsonIgnore]
    public string Key => CatalogPlatformIndex.Key(Provider, Repository, PreferredRelease);
}

public sealed record PublishedPlatformRecord(string Provider, string Repository, string? PreferredRelease,
    string ReleaseTag, string[] AssetNames, DateTimeOffset ValidatedAt, int SelectionRevision = 1)
{
    [JsonIgnore]
    public string Key => CatalogPlatformIndex.Key(Provider, Repository, PreferredRelease);
    [JsonIgnore]
    public CatalogPlatformEntry Metadata => new(ReleaseTag, AssetNames, ValidatedAt, SelectionRevision);
}

public sealed class PublishedPlatformDocument
{
    public const int CurrentRevision = 1;
    public int FormatRevision { get; set; } = CurrentRevision;
    public DateTimeOffset GeneratedAt { get; set; }
    public List<PublishedPlatformRecord> Entries { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static PublishedPlatformDocument Parse(string json)
    {
        var document = JsonSerializer.Deserialize<PublishedPlatformDocument>(json, JsonOptions)
            ?? throw new JsonException("Missing platform index.");
        if (document.FormatRevision != CurrentRevision || document.GeneratedAt == default ||
            document.Entries == null || document.Entries.Count > 10000)
            throw new JsonException("Unsupported or invalid platform index.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            if (entry == null || entry.SelectionRevision != CurrentRevision ||
                entry.Provider is not ("github" or "gitlab") || string.IsNullOrWhiteSpace(entry.Repository) ||
                entry.ReleaseTag == null || entry.AssetNames == null || entry.AssetNames.Length > 10000 ||
                entry.AssetNames.Any(n => n == null || n.Length > 2048) || entry.ValidatedAt == default ||
                entry.ValidatedAt > document.GeneratedAt.AddMinutes(5) || !keys.Add(entry.Key))
                throw new JsonException("Invalid platform entry.");
        }
        return document;
    }

    public void WriteAtomically(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        Parse(json); // Validate the complete output before replacing any successful publication.
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, full, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record PlatformPublicationResult(PublishedPlatformDocument Document, int Successful, IReadOnlyList<string> Failures);

/// <summary>Public catalog generator. Release decisions live in Core, shared with installs and updates.</summary>
public static class PlatformMetadataPublisher
{
    public static async Task<PlatformPublicationResult> GenerateAsync(HttpClient client,
        IEnumerable<PublishedPlatformTarget> targets, PublishedPlatformDocument? previous,
        Func<string, string?> tokenForProvider, CancellationToken cancellationToken = default)
    {
        var old = previous?.Entries.ToDictionary(e => e.Key) ?? [];
        var entries = new List<PublishedPlatformRecord>();
        var failures = new List<string>();
        var successful = 0;
        foreach (var target in targets.DistinctBy(t => t.Key).OrderBy(t => t.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var release = await CatalogReleaseSelection.FetchSelectedAsync(client, target.Provider, target.Repository,
                    target.PreferredRelease, tokenForProvider(target.Provider), cancellationToken).ConfigureAwait(false);
                entries.Add(new(RepositorySourceHelper.Normalize(target.Provider),
                    RepositorySourceHelper.IsGitHub(target.Provider) ? target.Repository.Trim().ToLowerInvariant() : target.Repository.Trim(),
                    target.PreferredRelease?.Trim(), release?.tag_name ?? "",
                    GitHubApiCache.ExtractAssetNames(release).ToArray(), DateTimeOffset.UtcNow));
                successful++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Never include response bodies or exception messages: credentials cannot enter published diagnostics.
                var detail = ex is ReleaseFetchException failure
                    ? $"HTTP {(int)failure.Result.StatusCode}" + (failure.Result.RetryAt is { } retry ? $"; retry after {retry:O}" : "")
                    : "metadata request failed";
                failures.Add($"{target.Provider}/{target.Repository}: {detail}");
                if (old.TryGetValue(target.Key, out var fallback)) entries.Add(fallback);
            }
        }
        return new(new() { GeneratedAt = DateTimeOffset.UtcNow, Entries = entries }, successful, failures);
    }
}
