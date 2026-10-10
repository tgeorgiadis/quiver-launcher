using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuiverLauncher.Services;

public sealed record QuiverCatalogPage<T>(List<T> Items, string? NextCursor, bool IsDone);

public sealed class QuiverCatalogGame
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>An original game on quiverlauncher.com: what it is, and every app that plays it.</summary>
public sealed class QuiverCatalogGameDetail
{
    public QuiverCatalogGameInfo Game { get; set; } = new();
    public List<QuiverCatalogApp> Entries { get; set; } = [];
}

public sealed class QuiverCatalogGameInfo
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Artwork { get; set; }
    public QuiverCatalogArt? LibraryArt { get; set; }
    public List<string> OriginalSystems { get; set; } = [];
}

public sealed class QuiverCatalogArt
{
    public string? Capsule { get; set; }
    public string? Header { get; set; }
    public string? Hero { get; set; }
    public string? Logo { get; set; }
}

public sealed class QuiverCatalogModSource
{
    public string? Provider { get; set; }
    public string? SourceUrl { get; set; }
}

public sealed class QuiverCatalogMods
{
    public string? Path { get; set; }
    public string? Layout { get; set; }
    public List<QuiverCatalogModSource> Sources { get; set; } = [];
}

public sealed class QuiverCatalogLauncher
{
    public string FolderName { get; set; } = "";
    public List<string> FilesToAdd { get; set; } = [];
    public string? ReleaseAssetFilter { get; set; }
    public QuiverCatalogMods? Mods { get; set; }
}

public sealed class QuiverCatalogVerified
{
    public string Version { get; set; } = "";
}

/// <summary>An app in the quiverlauncher.com catalog (the REST API's App object).</summary>
public sealed class QuiverCatalogApp
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public List<QuiverCatalogGame> Games { get; set; } = [];
    public List<string> Consoles { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string? Artwork { get; set; }
    public bool ArtworkFromGame { get; set; }
    public QuiverCatalogArt? LibraryArt { get; set; }
    public QuiverCatalogLauncher Launcher { get; set; } = new();
    public string ProjectType { get; set; } = "";
    public List<string> SupportedOS { get; set; } = [];
    /// <summary>"none", "assisted" or "generated".</summary>
    public string? AiLevel { get; set; }
    public QuiverCatalogDeveloper? Developer { get; set; }
    public int Recommended { get; set; }
    public int ReportIssues { get; set; }
    public int ReportBroken { get; set; }
    // Times are JavaScript milliseconds, which can have a fraction.
    public double AddedAt { get; set; }
    public double? LastReleaseAt { get; set; }
    public string? LastReleaseVersion { get; set; }
    public QuiverCatalogVerified? Verified { get; set; }
}

public sealed class QuiverCatalogDeveloper
{
    public string Name { get; set; } = "";
}

public sealed class QuiverCatalogProject
{
    public string Provider { get; set; } = "";
    public string? Repository { get; set; }
    public string? Author { get; set; }
}

public sealed class QuiverCatalogDetail
{
    public QuiverCatalogApp Entry { get; set; } = new();
    public QuiverCatalogProject Project { get; set; } = new();
}

public sealed class QuiverCatalogReview
{
    public string Author { get; set; } = "";
    public string Result { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Platform { get; set; }
    public string? Version { get; set; }
    public double CreatedAt { get; set; }
}

public sealed class QuiverCatalogConsole
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
}

public sealed class QuiverCatalogFacets
{
    public int Total { get; set; }
    public List<QuiverCatalogConsole> Consoles { get; set; } = [];
}

public sealed class QuiverCatalogReadme
{
    public string Markdown { get; set; } = "";
    public string? RawBase { get; set; }
}

/// <summary>An app in the release status feed: enough to match a library app to the catalog.</summary>
public sealed class QuiverCatalogStatus
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? Repository { get; set; }
    public QuiverCatalogVerified? Verified { get; set; }
    /// <summary>The newest release the developer published, verified or not.</summary>
    public QuiverCatalogVerified? LatestUpstream { get; set; }
}

public sealed class QuiverCatalogAsset
{
    public string Filename { get; set; } = "";
    /// <summary>Where the file downloads from: the release's own file link, not a GitHub API address.</summary>
    public string? Url { get; set; }
    /// <summary>"sha256:" and the hex digest of the file Quiver saw when the release came out.</summary>
    public string? Checksum { get; set; }
    /// <summary>VirusTotal's verdict on this file, once the site has one.</summary>
    public QuiverCatalogScan? Scan { get; set; }
}

public sealed class QuiverCatalogScan
{
    public string Verdict { get; set; } = "";
    public string? Engines { get; set; }
}

/// <summary>A release as the site judges it: verified, unverified or blocked, with the files it pinned.</summary>
public sealed class QuiverCatalogRelease
{
    public string Version { get; set; } = "";
    public string State { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Prerelease { get; set; }
    public double? ReleasedAt { get; set; }
    public List<string> Reasons { get; set; } = [];
    public List<QuiverCatalogAsset> Assets { get; set; } = [];
    public QuiverCatalogScan? Scan { get; set; }
    public double? CheckEndsAt { get; set; }
    /// <summary>
    /// The repository rebuilds this release under the same tag (a nightly): its checksums are of the newest build
    /// Quiver saw, which can be older than the one upstream.
    /// </summary>
    public bool Rolling { get; set; }
}

public sealed record QuiverCatalogQuery(string? Search = null, string? Os = null, string? Console = null,
    string? ProjectType = null, string Sort = "added", string? Ai = null);

/// <summary>Reads the public quiverlauncher.com catalog API. Every call is a plain GET.</summary>
public sealed class QuiverCatalogClient(HttpClient http, string? baseUrl = null)
{
    public const string DefaultBaseUrl = "https://api.quiverlauncher.com/api/v1";
    /// <summary>
    /// The same API on the Convex deployment's own host. api.quiverlauncher.com is a custom domain in front of it, and
    /// when that domain's TLS handshake fails for a player the deployment's host still answers.
    /// </summary>
    public const string FallbackBaseUrl = "https://famous-wildebeest-660.convex.site/api/v1";
    public const string WebsiteUrl = "https://quiverlauncher.com";
    public const int PageSize = 48;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _base = (baseUrl ?? Environment.GetEnvironmentVariable("QUIVER_API") ?? DefaultBaseUrl).TrimEnd('/');
    private bool _usingFallback;

    public static string AppPageUrl(string slug) => $"{WebsiteUrl}/apps/{Uri.EscapeDataString(slug)}";
    public static string ReviewPageUrl(string slug) => AppPageUrl(slug) + "?tab=how-it-runs";

    /// <summary>The most apps the API returns in one page.</summary>
    public const int MaxPageSize = 100;

    public Task<QuiverCatalogPage<QuiverCatalogApp>> GetAppsAsync(QuiverCatalogQuery query, string? cursor, CancellationToken token, int limit = PageSize)
    {
        var parameters = new List<string> { $"limit={limit}" };
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
        Add("search", query.Search);
        Add("os", query.Os);
        // "maker:Nintendo" selects every console a maker made.
        if (query.Console?.StartsWith("maker:", StringComparison.Ordinal) == true) Add("maker", query.Console["maker:".Length..]);
        else Add("console", query.Console);
        Add("projectType", query.ProjectType);
        Add("ai", query.Ai);
        // A search is ordered by relevance; the API ignores sort then.
        if (string.IsNullOrWhiteSpace(query.Search)) Add("sort", query.Sort);
        Add("cursor", cursor);
        return GetPageAsync<QuiverCatalogApp>("/apps?" + string.Join('&', parameters), token);
    }

    public async Task<QuiverCatalogFacets> GetFacetsAsync(CancellationToken token) =>
        await GetAsync<QuiverCatalogFacets>("/facets", token) ?? new();

    /// <summary>A game and the apps that play it; null when the catalog has no such game.</summary>
    public Task<QuiverCatalogGameDetail?> GetGameAsync(string slug, CancellationToken token) =>
        GetAsync<QuiverCatalogGameDetail>($"/games/{Uri.EscapeDataString(slug)}", token);

    public async Task<QuiverCatalogDetail> GetDetailAsync(string slug, CancellationToken token) =>
        await GetAsync<QuiverCatalogDetail>($"/apps/{Uri.EscapeDataString(slug)}", token)
        ?? throw new HttpRequestException("This app is no longer in the catalog.", null, HttpStatusCode.NotFound);

    public Task<QuiverCatalogPage<QuiverCatalogReview>> GetReviewsAsync(string slug, int limit, CancellationToken token) =>
        GetPageAsync<QuiverCatalogReview>($"/apps/{Uri.EscapeDataString(slug)}/reviews?limit={limit}", token);

    public Task<QuiverCatalogReadme?> GetReadmeAsync(string slug, CancellationToken token) =>
        GetAsync<QuiverCatalogReadme>($"/apps/{Uri.EscapeDataString(slug)}/readme", token);

    public Task<QuiverCatalogPage<QuiverCatalogStatus>> GetReleaseStatusAsync(string? cursor, CancellationToken token) =>
        GetPageAsync<QuiverCatalogStatus>("/release-status?limit=100" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), token);

    /// <summary>Every release the site knows for an app, newest first, each verified, unverified or blocked.</summary>
    public Task<QuiverCatalogPage<QuiverCatalogRelease>> GetReleaseHistoryAsync(string slug, CancellationToken token) =>
        GetPageAsync<QuiverCatalogRelease>($"/apps/{Uri.EscapeDataString(slug)}/release-history?limit=100", token);

    /// <summary>
    /// Tells quiverlauncher.com that a download of one of an app's files failed: gone ("missing", a 404) or not the
    /// file it checked ("mismatch"). The site reads that release back from its repository straight away and takes it
    /// down or pulls it if it should; the report changes nothing by itself. Never throws: it's only a nudge.
    /// </summary>
    public async Task ReportDownloadProblemAsync(string slug, string version, string fileName, string problem, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);
        try
        {
            var body = JsonSerializer.Serialize(new { version, file = fileName, problem });
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(
                (_usingFallback ? FallbackBaseUrl : _base) + $"/apps/{Uri.EscapeDataString(slug)}/download-problem",
                content, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't report a broken download of {slug}: {ex.Message}");
        }
    }

    private async Task<QuiverCatalogPage<T>> GetPageAsync<T>(string path, CancellationToken token)
    {
        var page = await GetAsync<JsonObject>(path, token);
        var items = page?["items"]?.Deserialize<List<T>>(Json) ?? [];
        var next = page?["nextCursor"]?.GetValue<string>();
        var done = page?["isDone"]?.GetValue<bool>() ?? true;
        return new(items, done ? null : next, done || next == null);
    }

    /// <summary>
    /// Returns null for a 404; any other failure throws with the site's own message. When api.quiverlauncher.com can't
    /// be reached at all (a failed TLS handshake or connection, or the 499 its proxy sometimes answers), asks the
    /// deployment's own host instead, and keeps asking it from then on.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken token) where T : class
    {
        if (_usingFallback) return await GetAsync<T>(FallbackBaseUrl, path, token).ConfigureAwait(false);
        try
        {
            return await GetAsync<T>(_base, path, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (_base == DefaultBaseUrl && Unreachable(ex) && !token.IsCancellationRequested)
        {
            T? answer = null;
            var failed = false;
            try
            {
                answer = await GetAsync<T>(FallbackBaseUrl, path, token).ConfigureAwait(false);
            }
            catch (Exception fallback) when (fallback is HttpRequestException or TimeoutException or JsonException)
            {
                failed = true;
            }
            // The player's real problem is the first one; say that.
            if (failed) ExceptionDispatchInfo.Throw(ex);
            _usingFallback = true;
            Telemetry.Current.Track("catalog_fallback_used", new Dictionary<string, object?>
            {
                ["reason"] = Telemetry.ReasonOf(ex),
                ["cause"] = Telemetry.CauseOf(ex),
            });
            return answer;
        }
    }

    /// <summary>The request never got a real answer: no status at all, or the proxy's 499.</summary>
    private static bool Unreachable(HttpRequestException ex) => ex.StatusCode is null || (int)ex.StatusCode == 499;

    /// <summary>What went wrong, in words: the wrapped cause when the message only says to look at it.</summary>
    public static string Explain(Exception ex)
    {
        var inner = ex.InnerException;
        while (inner?.InnerException != null && inner.Message.Contains("inner exception", StringComparison.OrdinalIgnoreCase))
            inner = inner.InnerException;
        return inner != null && ex.Message.Contains("inner exception", StringComparison.OrdinalIgnoreCase)
            ? $"{ex.Message.Replace(", see inner exception.", ":", StringComparison.OrdinalIgnoreCase)} {inner.Message}"
            : ex.Message;
    }

    private async Task<T?> GetAsync<T>(string baseUrl, string path, CancellationToken token) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.GetAsync(baseUrl + path, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(ErrorMessage(body) ?? $"quiverlauncher.com answered {(int)response.StatusCode}.", null, response.StatusCode);
            return JsonSerializer.Deserialize<T>(body, Json);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("quiverlauncher.com took too long to answer.");
        }
    }

    private static string? ErrorMessage(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }
}

/// <summary>Turns a catalog app into a library entry, through the same parser as app list files.</summary>
public static class QuiverCatalogMapping
{
    /// <summary>The app as an entry of an app list file (the format of apps.json).</summary>
    public static JsonObject ToListEntry(QuiverCatalogApp app, QuiverCatalogProject project)
    {
        var folder = FolderFor(app);
        var entry = new JsonObject
        {
            ["catalogEntryId"] = string.IsNullOrWhiteSpace(app.Id) ? null : app.Id,
            ["name"] = app.Name,
            ["project"] = app.ProjectName,
            ["folderName"] = folder,
            // An icon borrowed from the game would show another app's art in the library.
            ["appIconUrl"] = app.ArtworkFromGame ? null : app.Artwork,
            ["tags"] = new JsonArray(app.Tags.Select(t => (JsonNode?)t).ToArray()),
            ["filesToAdd"] = new JsonArray(app.Launcher.FilesToAdd.Select(f => (JsonNode?)f).ToArray()),
        };
        // A "manual" project has no repository, which the parser treats as a manually managed app.
        if (project.Provider is "github" or "gitlab" && !string.IsNullOrWhiteSpace(project.Repository))
        {
            entry["repository"] = project.Repository.Trim();
            if (project.Provider == "gitlab") entry["repositorySource"] = "gitlab";
            if (!string.IsNullOrWhiteSpace(app.Launcher.ReleaseAssetFilter)) entry["releaseAssetFilter"] = app.Launcher.ReleaseAssetFilter;
        }
        if (app.Launcher.Mods is { Path: { Length: > 0 } path } mods)
        {
            entry["mods"] = new JsonObject
            {
                ["path"] = path,
                ["layout"] = mods.Layout,
                ["sources"] = new JsonArray(mods.Sources.Select(s => (JsonNode?)new JsonObject
                {
                    ["provider"] = s.Provider,
                    ["sourceUrl"] = s.SourceUrl,
                }).ToArray()),
            };
        }
        return entry;
    }

    /// <summary>The app's folder: the catalog's, or its slug when the catalog gives none.</summary>
    public static string FolderFor(QuiverCatalogApp app) => string.IsNullOrWhiteSpace(app.Launcher.FolderName)
        ? Mods.GameModsConfig.SanitizeFolderName(app.Slug)
        : app.Launcher.FolderName.Trim();

    public static Models.GameInfo ToGameInfo(AppCatalogService parser, QuiverCatalogApp app, QuiverCatalogProject project) =>
        parser.ParseAppsFromJson(new JsonObject { ["apps"] = new JsonArray(ToListEntry(app, project)) }.ToJsonString()).Single();
}
