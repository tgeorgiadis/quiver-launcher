using System.Text.Json;
using QuiverLauncher.Models;
using QuiverLauncher.Core.Services;
namespace QuiverLauncher.Services;
public static class LibraryDocumentService
{
    public static async Task<QuiverLauncher.ViewModels.DocumentContent> LoadAsync(GameInfo game, bool readme,
        AppSettings settings, HttpClient client, RepositoryReadmeService readmes, CancellationToken token)
    {
        if (!readme) return new(await FetchChangelogAsync(game, settings, token));
        var result = await readmes.GetReadmeAsync(client, game.EffectiveRepositorySource, game.Repository,
            settings.GitHubApiToken, settings.GitLabApiToken, QuiverLauncherPaths.CacheDirectory, DateTime.UtcNow, token);
        return result.Status switch
        {
            RepositoryReadmeStatus.Markdown when !string.IsNullOrWhiteSpace(result.Markdown) => new(result.Markdown, result.RawRootUrl),
            RepositoryReadmeStatus.NoRepository => new("No repository README.", IsMarkdown: false),
            RepositoryReadmeStatus.Error => new(result.ErrorMessage ?? "Failed to load README.", IsMarkdown: false),
            _ => new("No README found for this repository.", IsMarkdown: false)
        };
    }
        public static async Task<string> FetchChangelogAsync(GameInfo game, AppSettings settings, CancellationToken token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(game.Repository))
                    return "No changelog available for this release.";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "QuiverLauncher");

                if (RepositorySourceHelper.IsGitLab(game.RepositorySource))
                {
                    if (!string.IsNullOrEmpty(settings?.GitLabApiToken))
                        client.DefaultRequestHeaders.TryAddWithoutValidation("PRIVATE-TOKEN", settings.GitLabApiToken);

                    var encoded = Uri.EscapeDataString(game.Repository);
                    var url = $"{GitLabReleaseSource.ApiBaseUrl}/projects/{encoded}/releases";
                    var response = await client.GetAsync(url, token);
                    if (!response.IsSuccessStatusCode)
                        return "Failed to fetch changelog from GitLab.";

                    var json = await response.Content.ReadAsStringAsync(token);
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Array ||
                        document.RootElement.GetArrayLength() == 0)
                    {
                        return "No changelog available for this release.";
                    }

                    var first = document.RootElement[0];
                    if (first.TryGetProperty("description", out var descriptionElement))
                    {
                        var description = descriptionElement.GetString();
                        if (!string.IsNullOrEmpty(description))
                            return description;
                    }

                    return "No changelog available for this release.";
                }

                if (!string.IsNullOrEmpty(settings?.GitHubApiToken))
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"token {settings.GitHubApiToken}");
                }

                var githubUrl = $"https://api.github.com/repos/{game.Repository}/releases/latest";
                var githubResponse = await client.GetAsync(githubUrl, token);

                if (!githubResponse.IsSuccessStatusCode)
                {
                    return "Failed to fetch changelog from GitHub.";
                }

                var githubJson = await githubResponse.Content.ReadAsStringAsync(token);
                using var githubDocument = JsonDocument.Parse(githubJson);
                var root = githubDocument.RootElement;

                if (root.TryGetProperty("body", out var bodyElement))
                {
                    var body = bodyElement.GetString();
                    if (!string.IsNullOrEmpty(body))
                    {
                        return body;
                    }
                }

                return "No changelog available for this release.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return $"Error fetching changelog: {ex.Message}";
            }
        }

}
