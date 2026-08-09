namespace QuiverLauncher.Services.Mods;

public interface IModProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>True when <see cref="ListPackagesPageAsync"/> performs real remote pagination.</summary>
    bool SupportsPagedListing { get; }

    /// <summary>True when <see cref="SearchPackagesPageAsync"/> queries a remote search API.</summary>
    bool SupportsRemoteSearch { get; }

    bool TryParseSource(string sourceUrl, out ModSourceRef source);

    /// <summary>Request that the next list/page call bypass disk cache when supported.</summary>
    void ForceRefreshOnNextList();

    Task<IReadOnlyList<ModPackage>> ListPackagesAsync(
        ModSourceRef source,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of packages. Providers with <see cref="SupportsPagedListing"/> false
    /// may return the full list as a single page with <see cref="ModPackagePage.NextPageToken"/> null.
    /// </summary>
    Task<ModPackagePage> ListPackagesPageAsync(
        ModSourceRef source,
        string? pageToken,
        int pageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of remote search results. Providers with <see cref="SupportsRemoteSearch"/> false
    /// should throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<ModPackagePage> SearchPackagesPageAsync(
        ModSourceRef source,
        string query,
        string? pageToken,
        int pageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<Stream> DownloadAsync(
        ModPackageVersion version,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    IReadOnlySet<string> GetArchiveMetadataFileNames();

    /// <summary>Returns package README markdown, or null when unavailable.</summary>
    Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default);

    /// <summary>Returns package changelog markdown, or null when unavailable.</summary>
    Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default);
}
