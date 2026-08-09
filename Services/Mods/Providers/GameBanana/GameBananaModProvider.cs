using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Services.Mods.Providers.GameBanana;

public sealed class GameBananaModProvider : IModProvider
{
    private static readonly HashSet<string> MetadataFiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient _httpClient;
    private readonly GameBananaApiClient _apiClient;
    private bool _forceNextRefresh;

    public GameBananaModProvider(HttpClient httpClient, string cacheRootDirectory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var cacheDir = Path.Combine(cacheRootDirectory, "Mods", ModProviderIds.GameBanana);
        _apiClient = new GameBananaApiClient(httpClient, cacheDir);
    }

    public string Id => ModProviderIds.GameBanana;
    public string DisplayName => "GameBanana";
    public bool SupportsPagedListing => true;
    public bool SupportsRemoteSearch => true;

    public void ForceRefreshOnNextList() => _forceNextRefresh = true;

    public bool TryParseSource(string sourceUrl, out ModSourceRef source)
    {
        source = null!;
        if (!GameBananaSourceParser.TryParse(sourceUrl, out var gameId))
            return false;

        source = new ModSourceRef
        {
            ProviderId = Id,
            SourceKey = gameId,
            DisplayLabel = $"{DisplayName} · {gameId}",
            SourceUrl = GameBananaSourceParser.BuildModsPageUrl(gameId),
        };
        return true;
    }

    public async Task<IReadOnlyList<ModPackage>> ListPackagesAsync(
        ModSourceRef source,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = options;
        var packages = new List<ModPackage>();
        string? token = null;
        do
        {
            var page = await ListPackagesPageAsync(source, token, pageSize: 50, options, cancellationToken)
                .ConfigureAwait(false);
            packages.AddRange(page.Packages);
            token = page.NextPageToken;
        } while (token != null);

        return packages;
    }

    public async Task<ModPackagePage> ListPackagesPageAsync(
        ModSourceRef source,
        string? pageToken,
        int pageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProviderId, Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Source provider '{source.ProviderId}' is not {Id}.");

        var page = 1;
        if (!string.IsNullOrWhiteSpace(pageToken) && !int.TryParse(pageToken, out page))
            throw new ArgumentException("GameBanana page token must be a page number.", nameof(pageToken));

        var size = Math.Clamp(pageSize <= 0 ? 30 : pageSize, 1, 50);
        var forceRefresh = _forceNextRefresh && page == 1;
        if (forceRefresh)
            _forceNextRefresh = false;

        var result = await _apiClient
            .GetModIndexPageAsync(
                source.SourceKey,
                page,
                size,
                options?.SortMode,
                forceRefresh,
                cancellationToken)
            .ConfigureAwait(false);

        var packages = result.Records
            .Select(r => MapIndexRecord(r, source))
            .Where(p => p != null)
            .Cast<ModPackage>()
            .ToList();

        string? nextToken = null;
        if (!result.IsComplete)
            nextToken = (page + 1).ToString();

        return new ModPackagePage
        {
            Packages = packages,
            NextPageToken = nextToken,
            TotalCount = result.TotalCount,
        };
    }

    public async Task<ModPackagePage> SearchPackagesPageAsync(
        ModSourceRef source,
        string query,
        string? pageToken,
        int pageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProviderId, Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Source provider '{source.ProviderId}' is not {Id}.");
        if (string.IsNullOrWhiteSpace(query))
            return await ListPackagesPageAsync(source, pageToken, pageSize, options, cancellationToken)
                .ConfigureAwait(false);

        var page = 1;
        if (!string.IsNullOrWhiteSpace(pageToken) && !int.TryParse(pageToken, out page))
            throw new ArgumentException("GameBanana page token must be a page number.", nameof(pageToken));

        var size = Math.Clamp(pageSize <= 0 ? 30 : pageSize, 1, 50);
        var result = await _apiClient
            .SearchResultsPageAsync(
                source.SourceKey,
                query.Trim(),
                page,
                size,
                options?.SortMode,
                forceRefresh: false,
                cancellationToken)
            .ConfigureAwait(false);

        var packages = result.Records
            .Select(r => MapIndexRecord(r, source))
            .Where(p => p != null)
            .Cast<ModPackage>()
            .ToList();

        return new ModPackagePage
        {
            Packages = packages,
            NextPageToken = result.IsComplete ? null : (page + 1).ToString(),
            TotalCount = result.TotalCount,
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

        if (!LooksLikeSupportedArchiveUrl(version.DownloadUrl))
            throw new InvalidOperationException(
                "This GameBanana file does not look like a zip or 7z archive. Quiver Launcher only installs zip/7z mods.");

        using var request = new HttpRequestMessage(HttpMethod.Get, version.DownloadUrl);
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var disposition = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? string.Empty;
        if (!LooksLikeSupportedArchiveUrl(version.DownloadUrl) &&
            !LooksLikeSupportedArchiveFileName(disposition) &&
            !LooksLikeSupportedArchiveContentType(contentType))
        {
            throw new InvalidOperationException(
                "This GameBanana download is not a zip or 7z archive. Quiver Launcher only installs zip/7z mods.");
        }

        var total = response.Content.Headers.ContentLength ?? version.FileSize;
        var fileStream = await ModArchiveDownload.CopyContentToTempFileAsync(
                response.Content,
                total,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (fileStream.Length >= 4)
            {
                Span<byte> header = stackalloc byte[4];
                _ = fileStream.Read(header);
                fileStream.Position = 0;
                if (!IsZipOrSevenZipHeader(header))
                {
                    throw new InvalidOperationException(
                        "Downloaded file is not a zip or 7z archive. Quiver Launcher only installs zip/7z mods.");
                }
            }

            return fileStream;
        }
        catch
        {
            await fileStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public IReadOnlySet<string> GetArchiveMetadataFileNames() => MetadataFiles;

    public async Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var detail = await _apiClient.GetModDetailAsync(package.Id, forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);
        if (detail == null)
            return null;

        var markdown = GameBananaHtml.ToMarkdown(detail.Text);
        if (string.IsNullOrWhiteSpace(markdown))
            markdown = GameBananaHtml.ToMarkdown(detail.Description);
        return string.IsNullOrWhiteSpace(markdown) ? null : markdown;
    }

    public async Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var updates = await _apiClient.GetModUpdatesAsync(package.Id, cancellationToken).ConfigureAwait(false);
        var markdown = GameBananaHtml.FormatUpdatesChangelog(updates);
        return string.IsNullOrWhiteSpace(markdown) ? null : markdown;
    }

    public async Task<ModPackage> EnrichWithFilesAsync(
        ModPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.DownloadFiles.Count > 0)
            return package;

        var detail = await _apiClient.GetModDetailAsync(package.Id, forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);
        if (detail == null)
            return package;

        return ApplyDetail(package, detail);
    }

    internal static ModPackage? MapIndexRecord(GameBananaIndexRecord record, ModSourceRef source)
    {
        if (record.IdRow <= 0 || string.IsNullOrWhiteSpace(record.Name))
            return null;

        if (!string.IsNullOrWhiteSpace(record.ModelName) &&
            !record.ModelName.Equals("Mod", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.IsNullOrWhiteSpace(record.PayType) &&
            !record.PayType.Equals("free", StringComparison.OrdinalIgnoreCase))
            return null;

        var owner = record.Submitter?.Name?.Trim() ?? string.Empty;
        var version = string.IsNullOrWhiteSpace(record.Version) ? "0" : record.Version.Trim();
        var iconUrl = BuildIconUrl(record.PreviewContent?.Screenshot, preferSfw: record.HasContentRatings);
        long? createdAt = record.DateAddedUnix > 0 ? record.DateAddedUnix : null;
        long? updatedAt = record.DateModifiedUnix > 0
            ? record.DateModifiedUnix
            : createdAt;

        return new ModPackage
        {
            ProviderId = ModProviderIds.GameBanana,
            SourceKey = source.SourceKey,
            SourceDisplayLabel = source.DisplayLabel,
            Id = record.IdRow.ToString(),
            Owner = owner,
            Name = record.Name.Trim(),
            FullName = string.IsNullOrWhiteSpace(owner)
                ? record.Name.Trim()
                : $"{owner}-{record.Name.Trim()}",
            Description = string.Empty,
            IconUrl = iconUrl,
            PackagePageUrl = record.ProfileUrl ?? $"https://gamebanana.com/mods/{record.IdRow}",
            IsDeprecated = record.IsObsolete,
            HasContentRating = record.HasContentRatings,
            DownloadCount = record.DownloadCount,
            RatingScore = record.LikeCount,
            UpdatedAtUnix = updatedAt,
            CreatedAtUnix = createdAt,
            LatestVersion = new ModPackageVersion
            {
                Version = version,
                DownloadUrl = string.Empty,
                FileSize = 0,
                Dependencies = [],
            },
            DownloadFiles = [],
        };
    }

    internal static ModPackage ApplyDetail(ModPackage package, GameBananaModDetail detail)
    {
        var files = (detail.Files ?? [])
            .Where(f => f.IdRow > 0 && !f.IsArchived && !string.IsNullOrWhiteSpace(f.DownloadUrl))
            .Select(f => new ModDownloadFile
            {
                Id = f.IdRow.ToString(),
                FileName = f.FileName?.Trim() ?? $"file-{f.IdRow}",
                Description = f.Description?.Trim() ?? string.Empty,
                DownloadUrl = f.DownloadUrl!.Trim(),
                FileSize = f.FileSize,
                Version = f.Version?.Trim() ?? string.Empty,
            })
            .ToList();

        var primary = files.FirstOrDefault();
        var version = !string.IsNullOrWhiteSpace(detail.Version)
            ? detail.Version!.Trim()
            : (!string.IsNullOrWhiteSpace(primary?.Version)
                ? primary!.Version
                : (package.LatestVersion?.Version ?? "0"));

        return new ModPackage
        {
            ProviderId = package.ProviderId,
            SourceKey = package.SourceKey,
            SourceDisplayLabel = package.SourceDisplayLabel,
            Id = package.Id,
            Owner = string.IsNullOrWhiteSpace(detail.Submitter?.Name)
                ? package.Owner
                : detail.Submitter!.Name!.Trim(),
            Name = string.IsNullOrWhiteSpace(detail.Name) ? package.Name : detail.Name.Trim(),
            FullName = package.FullName,
            Description = string.IsNullOrWhiteSpace(detail.Description)
                ? package.Description
                : detail.Description.Trim(),
            IconUrl = package.IconUrl,
            PackagePageUrl = detail.ProfileUrl ?? package.PackagePageUrl,
            IsDeprecated = detail.IsObsolete || package.IsDeprecated,
            HasContentRating = package.HasContentRating,
            DownloadCount = package.DownloadCount,
            RatingScore = package.RatingScore,
            UpdatedAtUnix = package.UpdatedAtUnix,
            CreatedAtUnix = package.CreatedAtUnix,
            LatestVersion = new ModPackageVersion
            {
                Version = version,
                DownloadUrl = primary?.DownloadUrl ?? string.Empty,
                FileSize = primary?.FileSize ?? 0,
                Dependencies = [],
            },
            DownloadFiles = files,
        };
    }

    internal static ModPackage WithSelectedFile(ModPackage package, ModDownloadFile file)
    {
        var version = !string.IsNullOrWhiteSpace(file.Version)
            ? file.Version
            : (package.LatestVersion?.Version ?? "0");

        return new ModPackage
        {
            ProviderId = package.ProviderId,
            SourceKey = package.SourceKey,
            SourceDisplayLabel = package.SourceDisplayLabel,
            Id = package.Id,
            Owner = package.Owner,
            Name = package.Name,
            FullName = package.FullName,
            Description = package.Description,
            IconUrl = package.IconUrl,
            PackagePageUrl = package.PackagePageUrl,
            IsDeprecated = package.IsDeprecated,
            HasContentRating = package.HasContentRating,
            DownloadCount = package.DownloadCount,
            RatingScore = package.RatingScore,
            UpdatedAtUnix = package.UpdatedAtUnix,
            CreatedAtUnix = package.CreatedAtUnix,
            LatestVersion = new ModPackageVersion
            {
                Version = version,
                DownloadUrl = file.DownloadUrl,
                FileSize = file.FileSize,
                Dependencies = [],
            },
            DownloadFiles = package.DownloadFiles,
        };
    }

    internal static string FormatUpdatesChangelog(IEnumerable<GameBananaUpdateRecord> updates) =>
        GameBananaHtml.FormatUpdatesChangelog(updates);

    private static string? BuildIconUrl(GameBananaScreenshot? shot, bool preferSfw)
    {
        if (shot == null || string.IsNullOrWhiteSpace(shot.BaseUrl))
            return null;

        string? file = preferSfw
            ? FirstNonEmpty(shot.File530Sfw, shot.File220Sfw, shot.File530, shot.File220)
            : FirstNonEmpty(shot.File530, shot.File220, shot.File530Sfw, shot.File220Sfw);

        if (string.IsNullOrWhiteSpace(file))
            return null;

        return $"{shot.BaseUrl.TrimEnd('/')}/{file}";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool LooksLikeSupportedArchiveUrl(string url)
    {
        if (url.Contains(".zip", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".7z", StringComparison.OrdinalIgnoreCase))
            return true;

        return url.Contains("gamebanana.com/dl/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSupportedArchiveFileName(string fileName) =>
        fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSupportedArchiveContentType(string contentType) =>
        contentType.Contains("zip", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("7z", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("x-7z", StringComparison.OrdinalIgnoreCase);

    /// <summary>Zip local header (<c>PK</c>) or 7z signature (<c>37 7A BC AF</c>).</summary>
    internal static bool IsZipOrSevenZipHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 2)
            return false;

        if (header[0] == (byte)'P' && header[1] == (byte)'K')
            return true;

        return header.Length >= 4 &&
               header[0] == 0x37 &&
               header[1] == 0x7A &&
               header[2] == 0xBC &&
               header[3] == 0xAF;
    }
}
