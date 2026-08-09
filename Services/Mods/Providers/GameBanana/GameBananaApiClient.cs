using System.Text.Json;

namespace QuiverLauncher.Services.Mods.Providers.GameBanana;

internal sealed class GameBananaApiClient
{
    private static readonly TimeSpan IndexCacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DetailCacheTtl = TimeSpan.FromHours(6);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public GameBananaApiClient(HttpClient httpClient, string cacheDirectory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
    }

    public async Task<GameBananaIndexPageResult> GetModIndexPageAsync(
        string gameId,
        int page,
        int pageSize,
        string? sortMode = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var indexSort = ModListSorter.ToGameBananaIndexSort(sortMode);
        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = Path.Combine(
            _cacheDirectory,
            "index",
            SanitizeFileName(gameId),
            $"sort-{SanitizeFileName(indexSort)}",
            $"p{page}_n{pageSize}.json");

        if (!forceRefresh && TryReadCache(cachePath, IndexCacheTtl, out var cachedJson))
        {
            var cached = JsonSerializer.Deserialize<GameBananaIndexResponse>(cachedJson, GameBananaJson.Options);
            if (cached != null)
                return ToIndexResult(cached, page);
        }

        var url = BuildIndexUrl(gameId, page, pageSize, indexSort);
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);

        var parsed = JsonSerializer.Deserialize<GameBananaIndexResponse>(json, GameBananaJson.Options)
                     ?? new GameBananaIndexResponse();
        return ToIndexResult(parsed, page);
    }

    public async Task<GameBananaModDetail?> GetModDetailAsync(
        string modId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = Path.Combine(_cacheDirectory, "detail", $"{SanitizeFileName(modId)}.json");

        if (!forceRefresh && TryReadCache(cachePath, DetailCacheTtl, out var cachedJson))
        {
            return JsonSerializer.Deserialize<GameBananaModDetail>(cachedJson, GameBananaJson.Options);
        }

        var url =
            $"https://gamebanana.com/apiv11/Mod/{Uri.EscapeDataString(modId)}?_csvProperties=_idRow,_sName,_sText,_sDescription,_sVersion,_aFiles,_sProfileUrl,_aSubmitter,_bIsObsolete";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<GameBananaModDetail>(json, GameBananaJson.Options);
    }

    public async Task<GameBananaIndexPageResult> SearchResultsPageAsync(
        string gameId,
        string query,
        int page,
        int pageSize,
        string? sortMode = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var order = ModListSorter.ToGameBananaSearchOrder(sortMode);
        var queryKey = SanitizeFileName(query.Trim().ToLowerInvariant());
        var cachePath = Path.Combine(
            _cacheDirectory,
            "search",
            SanitizeFileName(gameId),
            $"ord-{SanitizeFileName(order)}",
            queryKey,
            $"p{page}_n{pageSize}.json");

        if (!forceRefresh && TryReadCache(cachePath, IndexCacheTtl, out var cachedJson))
        {
            var cached = JsonSerializer.Deserialize<GameBananaIndexResponse>(cachedJson, GameBananaJson.Options);
            if (cached != null)
                return ToSearchResult(cached, page);
        }

        var url = BuildSearchUrl(gameId, query.Trim(), page, pageSize, order);
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);

        var parsed = JsonSerializer.Deserialize<GameBananaIndexResponse>(json, GameBananaJson.Options)
                     ?? new GameBananaIndexResponse();
        return ToSearchResult(parsed, page);
    }

    public async Task<IReadOnlyList<GameBananaUpdateRecord>> GetModUpdatesAsync(
        string modId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        var cachePath = Path.Combine(_cacheDirectory, "updates", $"{SanitizeFileName(modId)}.json");
        if (TryReadCache(cachePath, DetailCacheTtl, out var cachedJson))
        {
            var cached = JsonSerializer.Deserialize<GameBananaUpdatesResponse>(cachedJson, GameBananaJson.Options);
            return cached?.Records ?? [];
        }

        var url =
            $"https://gamebanana.com/apiv11/Mod/{Uri.EscapeDataString(modId)}/Updates?_nPage=1&_nPerpage=50";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return [];

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);

        var parsed = JsonSerializer.Deserialize<GameBananaUpdatesResponse>(json, GameBananaJson.Options);
        return parsed?.Records ?? [];
    }

    public static string BuildIndexUrl(string gameId, int page, int pageSize, string indexSort) =>
        $"https://gamebanana.com/apiv13/Mod/Index?_nPerpage={pageSize}" +
        $"&_aFilters[Generic_Game]={Uri.EscapeDataString(gameId)}" +
        $"&_nPage={page}" +
        $"&_sSort={Uri.EscapeDataString(indexSort)}";

    public static string BuildSearchUrl(string gameId, string query, int page, int pageSize, string order) =>
        "https://gamebanana.com/apiv13/Util/Search/Results" +
        $"?_sModelName=Mod" +
        $"&_sOrder={Uri.EscapeDataString(order)}" +
        $"&_idGameRow={Uri.EscapeDataString(gameId)}" +
        $"&_sSearchString={Uri.EscapeDataString(query)}" +
        $"&_csvFields=name,description,owner,credits" +
        $"&_nPerpage={pageSize}" +
        $"&_nPage={page}";

    private static GameBananaIndexPageResult ToIndexResult(GameBananaIndexResponse response, int page)
    {
        var isComplete = response.Metadata?.IsComplete == true || response.Records.Count == 0;
        return new GameBananaIndexPageResult
        {
            Records = response.Records,
            Page = page,
            IsComplete = isComplete,
            TotalCount = response.Metadata?.RecordCount,
        };
    }

    private static GameBananaIndexPageResult ToSearchResult(GameBananaIndexResponse response, int page)
    {
        // Defensive: keep Mods only even though _sModelName=Mod is requested.
        var mods = response.Records
            .Where(r => string.IsNullOrWhiteSpace(r.ModelName) ||
                        r.ModelName.Equals("Mod", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var isComplete = response.Metadata?.IsComplete == true || response.Records.Count == 0;
        return new GameBananaIndexPageResult
        {
            Records = mods,
            Page = page,
            IsComplete = isComplete,
            TotalCount = response.Metadata?.RecordCount,
        };
    }

    private static bool TryReadCache(string cachePath, TimeSpan ttl, out string json)
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

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}

internal sealed class GameBananaIndexPageResult
{
    public IReadOnlyList<GameBananaIndexRecord> Records { get; init; } = [];
    public int Page { get; init; }
    public bool IsComplete { get; init; }
    public int? TotalCount { get; init; }
}
