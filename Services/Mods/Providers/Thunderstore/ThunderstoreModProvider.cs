using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Services.Mods.Providers.Thunderstore;

public sealed class ThunderstoreModProvider : IModProvider
{
    private static readonly HashSet<string> MetadataFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "manifest.json",
        "icon.png",
        "README.md",
        "CHANGELOG.md",
    };

    private readonly HttpClient _httpClient;
    private readonly ThunderstoreApiClient _apiClient;
    private bool _forceNextRefresh;

    public ThunderstoreModProvider(HttpClient httpClient, string cacheRootDirectory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var cacheDir = Path.Combine(cacheRootDirectory, "Mods", ModProviderIds.Thunderstore);
        _apiClient = new ThunderstoreApiClient(httpClient, cacheDir);
    }

    public string Id => ModProviderIds.Thunderstore;
    public string DisplayName => "Thunderstore";
    public bool SupportsPagedListing => true;
    public bool SupportsRemoteSearch => true;

    public void ForceRefreshOnNextList() => _forceNextRefresh = true;

    public bool TryParseSource(string sourceUrl, out ModSourceRef source)
    {
        source = null!;
        if (!ThunderstoreCommunityParser.TryParse(sourceUrl, out var slug))
            return false;

        source = new ModSourceRef
        {
            ProviderId = Id,
            SourceKey = slug,
            DisplayLabel = $"{DisplayName} · {slug}",
            SourceUrl = ThunderstoreCommunityParser.BuildCommunityPageUrl(slug),
        };
        return true;
    }

    public async Task<IReadOnlyList<ModPackage>> ListPackagesAsync(
        ModSourceRef source,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var packages = new List<ModPackage>();
        string? token = null;
        do
        {
            var page = await ListPackagesPageAsync(source, token, pageSize: 20, options, cancellationToken)
                .ConfigureAwait(false);
            packages.AddRange(page.Packages);
            token = page.NextPageToken;
        } while (token != null);

        return packages;
    }

    public Task<ModPackagePage> ListPackagesPageAsync(
        ModSourceRef source,
        string? pageToken,
        int pageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetListingPageAsync(source, query: null, pageToken, pageSize, options, cancellationToken);

    public Task<ModPackagePage> SearchPackagesPageAsync(
        ModSourceRef source,
        string query,
        string? pageToken,
        int pageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ListPackagesPageAsync(source, pageToken, pageSize, options, cancellationToken);

        return GetListingPageAsync(source, query.Trim(), pageToken, pageSize, options, cancellationToken);
    }

    public async Task<ModPackage> EnrichForInstallAsync(
        ModPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.IsNullOrWhiteSpace(package.LatestVersion?.DownloadUrl) &&
            !string.IsNullOrWhiteSpace(package.LatestVersion.Version))
        {
            return package;
        }

        var detail = await _apiClient
            .GetExperimentalPackageAsync(package.Owner, package.Name, forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);
        if (detail?.Latest == null || string.IsNullOrWhiteSpace(detail.Latest.VersionNumber))
            return package;

        var latest = detail.Latest;
        return new ModPackage
        {
            ProviderId = package.ProviderId,
            SourceKey = package.SourceKey,
            SourceDisplayLabel = package.SourceDisplayLabel,
            Id = package.Id,
            Owner = string.IsNullOrWhiteSpace(detail.Owner) ? package.Owner : detail.Owner,
            Name = string.IsNullOrWhiteSpace(detail.Name) ? package.Name : detail.Name,
            FullName = string.IsNullOrWhiteSpace(detail.FullName) ? package.FullName : detail.FullName,
            Description = string.IsNullOrWhiteSpace(latest.Description) ? package.Description : latest.Description,
            IconUrl = latest.Icon ?? package.IconUrl,
            PackagePageUrl = ResolvePackagePageUrl(
                package.PackagePageUrl,
                package.SourceKey,
                string.IsNullOrWhiteSpace(detail.Owner) ? package.Owner : detail.Owner,
                string.IsNullOrWhiteSpace(detail.Name) ? package.Name : detail.Name,
                detail.PackageUrl),
            IsDeprecated = detail.IsDeprecated || package.IsDeprecated,
            HasContentRating = package.HasContentRating,
            DownloadCount = package.DownloadCount,
            RatingScore = package.RatingScore,
            UpdatedAtUnix = package.UpdatedAtUnix,
            CreatedAtUnix = package.CreatedAtUnix,
            LatestVersion = new ModPackageVersion
            {
                Version = latest.VersionNumber,
                DownloadUrl = latest.DownloadUrl ?? string.Empty,
                FileSize = package.LatestVersion?.FileSize ?? 0,
                Dependencies = latest.Dependencies ?? [],
            },
            DownloadFiles = package.DownloadFiles,
        };
    }

    public async Task<Stream> DownloadAsync(
        ModPackageVersion version,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (string.IsNullOrWhiteSpace(version.DownloadUrl))
            throw new InvalidOperationException("Download URL is missing.");

        return await ModArchiveDownload.DownloadToTempFileAsync(
                _httpClient,
                version.DownloadUrl,
                version.FileSize,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public IReadOnlySet<string> GetArchiveMetadataFileNames() => MetadataFiles;

    public Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default) =>
        GetDocAsync(package, "readme", cancellationToken);

    public Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default) =>
        GetDocAsync(package, "changelog", cancellationToken);

    private async Task<ModPackagePage> GetListingPageAsync(
        ModSourceRef source,
        string? query,
        string? pageToken,
        int pageSize,
        ModListOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProviderId, Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Source provider '{source.ProviderId}' is not {Id}.");

        _ = pageSize; // cyberstorm uses a fixed page size
        var page = 1;
        if (!string.IsNullOrWhiteSpace(pageToken) && !int.TryParse(pageToken, out page))
            throw new ArgumentException("Thunderstore page token must be a page number.", nameof(pageToken));

        var includeNsfw = options?.IncludeNsfw == true;
        var ordering = ModListSorter.ToThunderstoreOrdering(options?.SortMode);
        var forceRefresh = _forceNextRefresh && page == 1 && string.IsNullOrWhiteSpace(query);
        if (forceRefresh)
            _forceNextRefresh = false;

        var sectionUuid = await _apiClient
            .GetModsSectionUuidAsync(source.SourceKey, forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);

        var listing = await _apiClient
            .GetListingPageAsync(
                source.SourceKey,
                page,
                query,
                includeNsfw,
                sectionUuid,
                ordering,
                forceRefresh,
                cancellationToken)
            .ConfigureAwait(false);

        var packages = listing.Results
            .Select(r => MapListingPackage(r, source))
            .Where(p => p != null)
            .Cast<ModPackage>()
            .ToList();

        var nextPage = ThunderstoreApiClient.TryParseNextPage(listing.Next);
        return new ModPackagePage
        {
            Packages = packages,
            NextPageToken = nextPage?.ToString(),
            TotalCount = listing.Count,
        };
    }

    private async Task<string?> GetDocAsync(
        ModPackage package,
        string docKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        var enriched = package;
        if (string.IsNullOrWhiteSpace(package.LatestVersion?.Version))
            enriched = await EnrichForInstallAsync(package, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(enriched.Owner) ||
            string.IsNullOrWhiteSpace(enriched.Name) ||
            string.IsNullOrWhiteSpace(enriched.LatestVersion?.Version))
        {
            return null;
        }

        try
        {
            return await _apiClient.GetPackageDocMarkdownAsync(
                enriched.Owner,
                enriched.Name,
                enriched.LatestVersion.Version,
                docKind,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    internal static ModPackage? MapListingPackage(ThunderstoreListingPackageDto dto, ModSourceRef source)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Namespace))
            return null;

        var fullName = $"{dto.Namespace}-{dto.Name}";
        var createdAt = dto.DateTimeCreated?.ToUnixTimeSeconds();
        var updatedAt = dto.LastUpdated?.ToUnixTimeSeconds() ?? createdAt;
        var versionHint = TryParseVersionFromIconUrl(dto.IconUrl, dto.Namespace, dto.Name) ?? string.Empty;

        return new ModPackage
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = source.SourceKey,
            SourceDisplayLabel = source.DisplayLabel,
            Id = fullName,
            Owner = dto.Namespace,
            Name = dto.Name,
            FullName = fullName,
            Description = dto.Description ?? string.Empty,
            IconUrl = dto.IconUrl,
            PackagePageUrl = BuildPackagePageUrl(source.SourceKey, dto.Namespace, dto.Name),
            IsDeprecated = dto.IsDeprecated,
            HasContentRating = dto.IsNsfw,
            DownloadCount = dto.DownloadCount,
            RatingScore = dto.RatingCount,
            UpdatedAtUnix = updatedAt,
            CreatedAtUnix = createdAt,
            LatestVersion = new ModPackageVersion
            {
                Version = versionHint,
                DownloadUrl = string.Empty,
                FileSize = dto.Size,
                Dependencies = [],
            },
        };
    }

    /// <summary>
    /// Community-scoped Thunderstore package page:
    /// <c>https://thunderstore.io/c/{community}/p/{owner}/{name}/</c>.
    /// </summary>
    internal static string? BuildPackagePageUrl(string? sourceKey, string? owner, string? name)
    {
        if (string.IsNullOrWhiteSpace(sourceKey) ||
            string.IsNullOrWhiteSpace(owner) ||
            string.IsNullOrWhiteSpace(name))
            return null;

        return $"https://thunderstore.io/c/{Uri.EscapeDataString(sourceKey)}/p/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/";
    }

    internal static bool IsCommunityScopedPackagePageUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.Contains("/c/", StringComparison.OrdinalIgnoreCase) &&
        url.Contains("/p/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Prefer an existing community URL, else rebuild from <paramref name="sourceKey"/>,
    /// else keep any existing URL, else experimental <c>package_url</c> (often community-less).
    /// </summary>
    internal static string? ResolvePackagePageUrl(
        string? existingUrl,
        string? sourceKey,
        string owner,
        string name,
        string? experimentalPackageUrl)
    {
        if (IsCommunityScopedPackagePageUrl(existingUrl))
            return existingUrl;

        var rebuilt = BuildPackagePageUrl(sourceKey, owner, name);
        if (!string.IsNullOrWhiteSpace(rebuilt))
            return rebuilt;

        if (!string.IsNullOrWhiteSpace(existingUrl))
            return existingUrl;

        return experimentalPackageUrl;
    }

    /// <summary>
    /// Icon URLs are typically <c>.../Namespace-Name-1.2.3.png</c>; used as a version hint until experimental enrich.
    /// </summary>
    internal static string? TryParseVersionFromIconUrl(string? iconUrl, string packageNamespace, string name)
    {
        if (string.IsNullOrWhiteSpace(iconUrl))
            return null;

        var file = iconUrl.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(file))
            return null;

        file = Path.GetFileNameWithoutExtension(file);
        var prefix = $"{packageNamespace}-{name}-";
        if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var version = file[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    /// <summary>Legacy v1 DTO mapper kept for unit tests and any residual dump consumers.</summary>
    internal static ModPackage? MapPackage(ThunderstorePackageDto dto, ModSourceRef source)
    {
        if (string.IsNullOrWhiteSpace(dto.Uuid4) && string.IsNullOrWhiteSpace(dto.FullName))
            return null;

        var latest = dto.Versions.FirstOrDefault(v => v.IsActive) ?? dto.Versions.FirstOrDefault();
        ModPackageVersion? latestVersion = null;
        if (latest != null && !string.IsNullOrWhiteSpace(latest.VersionNumber))
        {
            latestVersion = new ModPackageVersion
            {
                Version = latest.VersionNumber,
                DownloadUrl = latest.DownloadUrl ?? string.Empty,
                FileSize = latest.FileSize,
                Dependencies = latest.Dependencies ?? [],
            };
        }

        long downloadCount = 0;
        foreach (var version in dto.Versions)
            downloadCount += version.Downloads;

        var updatedAt = dto.DateUpdated?.ToUnixTimeSeconds()
                        ?? latest?.DateCreated?.ToUnixTimeSeconds()
                        ?? dto.DateCreated?.ToUnixTimeSeconds();
        var createdAt = dto.DateCreated?.ToUnixTimeSeconds()
                        ?? latest?.DateCreated?.ToUnixTimeSeconds();

        return new ModPackage
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = source.SourceKey,
            SourceDisplayLabel = source.DisplayLabel,
            Id = string.IsNullOrWhiteSpace(dto.Uuid4) ? dto.FullName : dto.Uuid4,
            Owner = dto.Owner ?? string.Empty,
            Name = dto.Name ?? string.Empty,
            FullName = string.IsNullOrWhiteSpace(dto.FullName)
                ? $"{dto.Owner}-{dto.Name}"
                : dto.FullName,
            Description = latest?.Description ?? string.Empty,
            IconUrl = latest?.Icon,
            PackagePageUrl = dto.PackageUrl,
            IsDeprecated = dto.IsDeprecated,
            HasContentRating = dto.HasNsfwContent,
            DownloadCount = downloadCount,
            RatingScore = dto.RatingScore,
            UpdatedAtUnix = updatedAt,
            CreatedAtUnix = createdAt,
            LatestVersion = latestVersion,
        };
    }

    /// <summary>
    /// Dependency strings are <c>Owner-Name-Version</c>. Returns owner+name package key and optional version.
    /// </summary>
    public static bool TryParseDependencyString(string? dependency, out string packageFullName, out string? version)
    {
        packageFullName = string.Empty;
        version = null;
        if (string.IsNullOrWhiteSpace(dependency))
            return false;

        var parts = dependency.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        if (parts.Length >= 3 && LooksLikeVersion(parts[^1]))
        {
            version = parts[^1];
            packageFullName = string.Join('-', parts.Take(parts.Length - 1));
            return true;
        }

        packageFullName = dependency.Trim();
        return true;
    }

    /// <summary>Splits <c>Owner-Name</c> (first hyphen) into namespace and package name.</summary>
    public static bool TrySplitPackageFullName(string? fullName, out string owner, out string name)
    {
        owner = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(fullName))
            return false;

        var idx = fullName.IndexOf('-');
        if (idx <= 0 || idx >= fullName.Length - 1)
            return false;

        owner = fullName[..idx];
        name = fullName[(idx + 1)..];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(name);
    }

    private static bool LooksLikeVersion(string value)
    {
        var segments = value.Split('.');
        return segments.Length >= 2 && segments.All(s => int.TryParse(s, out _));
    }
}
