using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuiverLauncher.Services.Mods.Providers.Thunderstore;

public sealed class ThunderstoreApiClient
{
    private static readonly TimeSpan ListingCacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FiltersCacheTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan PackageCacheTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan DocsCacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public ThunderstoreApiClient(HttpClient httpClient, string cacheDirectory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
    }

    public async Task<string?> GetModsSectionUuidAsync(
        string communitySlug,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(communitySlug);

        var cachePath = Path.Combine(_cacheDirectory, "filters", $"{SanitizeFileName(communitySlug)}.json");
        if (!forceRefresh && TryReadTextCache(cachePath, FiltersCacheTtl, out var cachedJson))
        {
            var cached = JsonSerializer.Deserialize<ThunderstoreCommunityFiltersResponseDto>(
                cachedJson, ThunderstoreJson.Options);
            return SelectModsSectionUuid(cached?.Sections);
        }

        var url = $"https://thunderstore.io/api/cyberstorm/community/{Uri.EscapeDataString(communitySlug)}/filters/";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        WriteTextCache(cachePath, json);

        var filters = JsonSerializer.Deserialize<ThunderstoreCommunityFiltersResponseDto>(json, ThunderstoreJson.Options);
        return SelectModsSectionUuid(filters?.Sections);
    }

    public async Task<ThunderstoreListingPageDto> GetListingPageAsync(
        string communitySlug,
        int page,
        string? query,
        bool includeNsfw,
        string? sectionUuid,
        string ordering = "last-updated",
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(communitySlug);
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));

        var orderingKey = SanitizeFileName(
            string.IsNullOrWhiteSpace(ordering) ? "last-updated" : ordering.Trim().ToLowerInvariant());
        var queryKey = string.IsNullOrWhiteSpace(query) ? "_" : SanitizeFileName(query.Trim().ToLowerInvariant());
        var sectionKey = string.IsNullOrWhiteSpace(sectionUuid) ? "all" : SanitizeFileName(sectionUuid);
        var cachePath = Path.Combine(
            _cacheDirectory,
            "listing",
            SanitizeFileName(communitySlug),
            $"ord-{orderingKey}",
            $"q-{queryKey}",
            $"nsfw-{(includeNsfw ? "1" : "0")}",
            $"sec-{sectionKey}",
            $"p{page}.json");

        if (!forceRefresh && TryReadTextCache(cachePath, ListingCacheTtl, out var cachedJson))
        {
            var cached = JsonSerializer.Deserialize<ThunderstoreListingPageDto>(cachedJson, ThunderstoreJson.Options);
            if (cached != null)
                return cached;
        }

        var url = BuildListingUrl(communitySlug, page, query, includeNsfw, sectionUuid, ordering);
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        WriteTextCache(cachePath, json);

        return JsonSerializer.Deserialize<ThunderstoreListingPageDto>(json, ThunderstoreJson.Options)
               ?? new ThunderstoreListingPageDto();
    }

    public async Task<ThunderstoreExperimentalPackageDto?> GetExperimentalPackageAsync(
        string packageNamespace,
        string name,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var cachePath = Path.Combine(
            _cacheDirectory,
            "package",
            SanitizeFileName(packageNamespace),
            $"{SanitizeFileName(name)}.json");

        if (!forceRefresh && TryReadTextCache(cachePath, PackageCacheTtl, out var cachedJson))
        {
            return JsonSerializer.Deserialize<ThunderstoreExperimentalPackageDto>(
                cachedJson, ThunderstoreJson.Options);
        }

        var url =
            $"https://thunderstore.io/api/experimental/package/{Uri.EscapeDataString(packageNamespace)}/{Uri.EscapeDataString(name)}/";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        WriteTextCache(cachePath, json);
        return JsonSerializer.Deserialize<ThunderstoreExperimentalPackageDto>(json, ThunderstoreJson.Options);
    }

    public async Task<string?> GetPackageDocMarkdownAsync(
        string packageNamespace,
        string name,
        string version,
        string docKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(docKind);

        var kind = docKind.Trim().ToLowerInvariant();
        if (kind is not ("readme" or "changelog"))
            throw new ArgumentOutOfRangeException(nameof(docKind), "docKind must be readme or changelog.");

        var cachePath = GetDocCachePath(packageNamespace, name, version, kind);
        if (TryReadDocCache(cachePath, out var cachedMarkdown))
            return string.IsNullOrWhiteSpace(cachedMarkdown) ? null : cachedMarkdown;

        var url = BuildPackageDocUrl(packageNamespace, name, version, kind);
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await WriteDocCacheAsync(cachePath, string.Empty, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<ThunderstoreMarkdownDocDto>(
            stream,
            ThunderstoreJson.Options,
            cancellationToken).ConfigureAwait(false);

        var markdown = payload?.Markdown?.Trim();
        await WriteDocCacheAsync(cachePath, markdown ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(markdown) ? null : markdown;
    }

    public static string BuildListingUrl(
        string communitySlug,
        int page,
        string? query,
        bool includeNsfw,
        string? sectionUuid,
        string ordering = "last-updated")
    {
        var order = string.IsNullOrWhiteSpace(ordering) ? "last-updated" : ordering.Trim();
        var sb = new StringBuilder();
        sb.Append("https://thunderstore.io/api/cyberstorm/listing/");
        sb.Append(Uri.EscapeDataString(communitySlug));
        sb.Append("/?page=");
        sb.Append(page);
        sb.Append("&ordering=");
        sb.Append(Uri.EscapeDataString(order));
        sb.Append("&nsfw=");
        sb.Append(includeNsfw ? "true" : "false");
        sb.Append("&deprecated=false");
        if (!string.IsNullOrWhiteSpace(query))
        {
            sb.Append("&q=");
            sb.Append(Uri.EscapeDataString(query.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(sectionUuid))
        {
            sb.Append("&section=");
            sb.Append(Uri.EscapeDataString(sectionUuid.Trim()));
        }

        return sb.ToString();
    }

    public static string BuildPackageDocUrl(string packageNamespace, string name, string version, string docKind) =>
        $"https://thunderstore.io/api/experimental/package/{Uri.EscapeDataString(packageNamespace)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/{docKind.Trim().ToLowerInvariant()}/";

    public static string? SelectModsSectionUuid(IReadOnlyList<ThunderstoreCommunityFilterDto>? filters)
    {
        if (filters == null || filters.Count == 0)
            return null;

        var mods = filters.FirstOrDefault(f =>
            string.Equals(f.Slug, "mods", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Name, "Mods", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(mods?.Uuid) ? null : mods.Uuid;
    }

    public static int? TryParseNextPage(string? nextUrl)
    {
        if (string.IsNullOrWhiteSpace(nextUrl))
            return null;

        if (!Uri.TryCreate(nextUrl, UriKind.Absolute, out var uri))
            return null;

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            if (!key.Equals("page", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = Uri.UnescapeDataString(part[(eq + 1)..]);
            if (int.TryParse(value, out var n) && n > 0)
                return n;
        }

        return null;
    }

    private string GetDocCachePath(string packageNamespace, string name, string version, string kind)
    {
        var dir = Path.Combine(
            _cacheDirectory,
            "docs",
            SanitizeFileName(packageNamespace),
            SanitizeFileName(name),
            SanitizeFileName(version));
        return Path.Combine(dir, $"{kind}.md");
    }

    private static bool TryReadDocCache(string cachePath, out string markdown)
    {
        markdown = string.Empty;
        try
        {
            if (!File.Exists(cachePath))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
            if (age > DocsCacheTtl)
                return false;

            markdown = File.ReadAllText(cachePath);
            return true;
        }
        catch
        {
            markdown = string.Empty;
            return false;
        }
    }

    private static async Task WriteDocCacheAsync(string cachePath, string markdown, CancellationToken cancellationToken)
    {
        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(cachePath, markdown, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cache write.
        }
    }

    private static bool TryReadTextCache(string cachePath, TimeSpan ttl, out string json)
    {
        json = string.Empty;
        try
        {
            if (!File.Exists(cachePath))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
            if (age > ttl)
                return false;

            json = File.ReadAllText(cachePath);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteTextCache(string cachePath, string json)
    {
        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(cachePath, json);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }
}

internal sealed class ThunderstoreMarkdownDocDto
{
    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }
}
