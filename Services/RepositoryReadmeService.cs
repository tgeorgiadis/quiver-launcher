using System.Net;
using System.Net.Http.Headers;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

public enum RepositoryReadmeStatus
{
    Markdown,
    NotFound,
    NoRepository,
    Error,
}

public readonly record struct RepositoryReadmeResult(
    RepositoryReadmeStatus Status,
    string? Markdown,
    string? ErrorMessage,
    string? RawRootUrl);

public sealed class RepositoryReadmeService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);
    public const string UserAgent = "QuiverLauncher";

    private static readonly string[] GitLabReadmeNames =
    [
        "README.md",
        "readme.md",
        "README.rst",
        "README",
    ];

    public static string BuildGitHubReadmeUrl(string repository) =>
        $"https://api.github.com/repos/{repository.Trim()}/readme";

    public static string BuildGitLabReadmeUrl(string repository, string fileName)
    {
        var encodedProject = Uri.EscapeDataString(repository.Trim());
        var encodedFile = Uri.EscapeDataString(fileName);
        return $"{GitLabReleaseSource.ApiBaseUrl}/projects/{encodedProject}/repository/files/{encodedFile}/raw?ref=HEAD";
    }

    public static string BuildRawRootUrl(string? repositorySource, string? repository)
    {
        var repo = repository?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(repo))
            return string.Empty;

        return RepositorySourceHelper.IsGitLab(repositorySource)
            ? $"https://gitlab.com/{repo}/-/raw/HEAD/"
            : $"https://raw.githubusercontent.com/{repo}/HEAD/";
    }

    public static string GetCacheHost(string? repositorySource) =>
        RepositorySourceHelper.IsGitLab(repositorySource) ? "gitlab" : "github";

    public static string SanitizeRepoFileName(string repository)
    {
        var value = repository.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace('/', '_').Replace('\\', '_');
    }

    public static string GetCachePath(string cacheRoot, string? repositorySource, string repository) =>
        Path.Combine(cacheRoot, "Readmes", GetCacheHost(repositorySource), SanitizeRepoFileName(repository) + ".md");

    public async Task<RepositoryReadmeResult> GetReadmeAsync(
        HttpClient httpClient,
        string? repositorySource,
        string? repository,
        string? githubToken,
        string? gitlabToken,
        string cacheRoot,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return new RepositoryReadmeResult(
                RepositoryReadmeStatus.NoRepository,
                Markdown: null,
                ErrorMessage: null,
                RawRootUrl: null);
        }

        var repo = repository.Trim();
        var rawRoot = BuildRawRootUrl(repositorySource, repo);
        var cachePath = GetCachePath(cacheRoot, repositorySource, repo);
        if (TryReadCache(cachePath, utcNow, out var cached))
        {
            return string.IsNullOrEmpty(cached)
                ? new RepositoryReadmeResult(RepositoryReadmeStatus.NotFound, null, null, rawRoot)
                : new RepositoryReadmeResult(RepositoryReadmeStatus.Markdown, cached, null, rawRoot);
        }

        try
        {
            if (RepositorySourceHelper.IsGitLab(repositorySource))
                return await FetchGitLabAsync(httpClient, repo, gitlabToken, cachePath, rawRoot, cancellationToken)
                    .ConfigureAwait(false);

            return await FetchGitHubAsync(httpClient, repo, githubToken, cachePath, rawRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RepositoryReadmeResult(RepositoryReadmeStatus.Error, null, ex.Message, rawRoot);
        }
    }

    private static async Task<RepositoryReadmeResult> FetchGitHubAsync(
        HttpClient httpClient,
        string repository,
        string? token,
        string cachePath,
        string rawRoot,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildGitHubReadmeUrl(repository));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("Authorization", $"token {token}");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await WriteCacheAsync(cachePath, string.Empty, cancellationToken).ConfigureAwait(false);
            return new RepositoryReadmeResult(RepositoryReadmeStatus.NotFound, null, null, rawRoot);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new RepositoryReadmeResult(
                RepositoryReadmeStatus.Error,
                null,
                $"Failed to fetch README from GitHub ({(int)response.StatusCode}).",
                rawRoot);
        }

        var markdown = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            await WriteCacheAsync(cachePath, string.Empty, cancellationToken).ConfigureAwait(false);
            return new RepositoryReadmeResult(RepositoryReadmeStatus.NotFound, null, null, rawRoot);
        }

        await WriteCacheAsync(cachePath, markdown, cancellationToken).ConfigureAwait(false);
        return new RepositoryReadmeResult(RepositoryReadmeStatus.Markdown, markdown, null, rawRoot);
    }

    private static async Task<RepositoryReadmeResult> FetchGitLabAsync(
        HttpClient httpClient,
        string repository,
        string? token,
        string cachePath,
        string rawRoot,
        CancellationToken cancellationToken)
    {
        HttpStatusCode? lastError = null;
        foreach (var fileName in GitLabReadmeNames)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildGitLabReadmeUrl(repository, fileName));
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;

            if (!response.IsSuccessStatusCode)
            {
                lastError = response.StatusCode;
                continue;
            }

            var markdown = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(markdown))
                continue;

            await WriteCacheAsync(cachePath, markdown, cancellationToken).ConfigureAwait(false);
            return new RepositoryReadmeResult(RepositoryReadmeStatus.Markdown, markdown, null, rawRoot);
        }

        if (lastError is { } status && status != HttpStatusCode.NotFound)
        {
            return new RepositoryReadmeResult(
                RepositoryReadmeStatus.Error,
                null,
                $"Failed to fetch README from GitLab ({(int)status}).",
                rawRoot);
        }

        await WriteCacheAsync(cachePath, string.Empty, cancellationToken).ConfigureAwait(false);
        return new RepositoryReadmeResult(RepositoryReadmeStatus.NotFound, null, null, rawRoot);
    }

    internal static bool TryReadCache(string cachePath, DateTime utcNow, out string markdown)
    {
        markdown = string.Empty;
        try
        {
            if (!File.Exists(cachePath))
                return false;

            var age = utcNow - File.GetLastWriteTimeUtc(cachePath);
            if (age > CacheTtl)
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

    private static async Task WriteCacheAsync(string cachePath, string markdown, CancellationToken cancellationToken)
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
}
