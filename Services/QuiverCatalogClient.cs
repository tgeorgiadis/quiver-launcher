using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuiverLauncher.Services;

public sealed record QuiverCatalogPage<T>(List<T> Items, string? NextCursor, bool IsDone);

public sealed class QuiverCatalogGame
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
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
    public int Recommended { get; set; }
    public int ReportIssues { get; set; }
    public int ReportBroken { get; set; }
    // Times are JavaScript milliseconds, which can have a fraction.
    public double AddedAt { get; set; }
    public double? LastReleaseAt { get; set; }
    public string? LastReleaseVersion { get; set; }
    public QuiverCatalogVerified? Verified { get; set; }
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

public sealed record QuiverCatalogQuery(string? Search = null, string? Os = null, string? Console = null,
    string? ProjectType = null, string Sort = "added");

/// <summary>Reads the public quiverlauncher.com catalog API. Every call is a plain GET.</summary>
public sealed class QuiverCatalogClient(HttpClient http, string? baseUrl = null)
{
    public const string DefaultBaseUrl = "https://api.quiverlauncher.com/api/v1";
    public const string WebsiteUrl = "https://quiverlauncher.com";
    public const int PageSize = 48;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _base = (baseUrl ?? Environment.GetEnvironmentVariable("QUIVER_API") ?? DefaultBaseUrl).TrimEnd('/');

    public static string AppPageUrl(string slug) => $"{WebsiteUrl}/apps/{Uri.EscapeDataString(slug)}";
    public static string ReviewPageUrl(string slug) => AppPageUrl(slug) + "?tab=how-it-runs";

    public Task<QuiverCatalogPage<QuiverCatalogApp>> GetAppsAsync(QuiverCatalogQuery query, string? cursor, CancellationToken token)
    {
        var parameters = new List<string> { $"limit={PageSize}" };
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
        // A search is ordered by relevance; the API ignores sort then.
        if (string.IsNullOrWhiteSpace(query.Search)) Add("sort", query.Sort);
        Add("cursor", cursor);
        return GetPageAsync<QuiverCatalogApp>("/apps?" + string.Join('&', parameters), token);
    }

    public async Task<QuiverCatalogFacets> GetFacetsAsync(CancellationToken token) =>
        await GetAsync<QuiverCatalogFacets>("/facets", token) ?? new();

    public async Task<QuiverCatalogDetail> GetDetailAsync(string slug, CancellationToken token) =>
        await GetAsync<QuiverCatalogDetail>($"/apps/{Uri.EscapeDataString(slug)}", token)
        ?? throw new HttpRequestException("This app is no longer in the catalog.", null, HttpStatusCode.NotFound);

    public Task<QuiverCatalogPage<QuiverCatalogReview>> GetReviewsAsync(string slug, int limit, CancellationToken token) =>
        GetPageAsync<QuiverCatalogReview>($"/apps/{Uri.EscapeDataString(slug)}/reviews?limit={limit}", token);

    public Task<QuiverCatalogReadme?> GetReadmeAsync(string slug, CancellationToken token) =>
        GetAsync<QuiverCatalogReadme>($"/apps/{Uri.EscapeDataString(slug)}/readme", token);

    private async Task<QuiverCatalogPage<T>> GetPageAsync<T>(string path, CancellationToken token)
    {
        var page = await GetAsync<JsonObject>(path, token);
        var items = page?["items"]?.Deserialize<List<T>>(Json) ?? [];
        var next = page?["nextCursor"]?.GetValue<string>();
        var done = page?["isDone"]?.GetValue<bool>() ?? true;
        return new(items, done ? null : next, done || next == null);
    }

    /// <summary>Returns null for a 404; any other failure throws with the site's own message.</summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken token) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.GetAsync(_base + path, timeout.Token).ConfigureAwait(false);
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
        var folder = string.IsNullOrWhiteSpace(app.Launcher.FolderName)
            ? Mods.GameModsConfig.SanitizeFolderName(app.Slug)
            : app.Launcher.FolderName.Trim();
        var entry = new JsonObject
        {
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

    public static Models.GameInfo ToGameInfo(AppCatalogService parser, QuiverCatalogApp app, QuiverCatalogProject project) =>
        parser.ParseAppsFromJson(new JsonObject { ["apps"] = new JsonArray(ToListEntry(app, project)) }.ToJsonString()).Single();
}
