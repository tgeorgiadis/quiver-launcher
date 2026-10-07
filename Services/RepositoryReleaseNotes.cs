using System.Text.Json;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

/// <summary>
/// A library app's recent releases and their notes, read from its GitHub or GitLab repository. The app page's Releases
/// tab shows these for an app that isn't in the App Catalog (the catalog's own release history covers the rest).
/// </summary>
public static class RepositoryReleaseNotes
{
    /// <summary>The state given to releases read from a repository: Quiver hasn't judged them, so no badge shows.</summary>
    public const string RepositoryState = "repository";
    public const int Count = 10;

    public static async Task<IReadOnlyList<QuiverCatalogRelease>> FetchAsync(GameInfo game, AppSettings settings, HttpClient client,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(game.Repository)) return [];
        var gitLab = RepositorySourceHelper.IsGitLab(game.EffectiveRepositorySource);
        var url = gitLab
            ? $"{GitLabReleaseSource.ApiBaseUrl}/projects/{Uri.EscapeDataString(game.Repository)}/releases?per_page={Count}"
            : $"https://api.github.com/repos/{game.Repository}/releases?per_page={Count}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "QuiverLauncher");
        if (gitLab && !string.IsNullOrEmpty(settings.GitLabApiToken))
            request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", settings.GitLabApiToken);
        else if (!gitLab && !string.IsNullOrEmpty(settings.GitHubApiToken))
            request.Headers.TryAddWithoutValidation("Authorization", $"token {settings.GitHubApiToken}");
        using var response = await client.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(gitLab ? "GitLab" : "GitHub")} answered {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        return document.RootElement.EnumerateArray().Take(Count).Select(release => new QuiverCatalogRelease
        {
            Version = Text(release, "tag_name") ?? Text(release, "name") ?? "",
            Notes = Text(release, gitLab ? "description" : "body") ?? "",
            Prerelease = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
            ReleasedAt = DateTimeOffset.TryParse(Text(release, gitLab ? "released_at" : "published_at"), out var at)
                ? at.ToUnixTimeMilliseconds() : null,
            State = RepositoryState,
        }).ToList();
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
