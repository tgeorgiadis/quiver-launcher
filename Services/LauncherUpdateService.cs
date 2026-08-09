using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

public sealed class LauncherUpdateCheckInfo
{
    public DateTime LastCheckTime { get; init; }
    public bool UpdateAvailable { get; init; }
    public string LastKnownVersion { get; init; } = string.Empty;
    public string CurrentVersion { get; init; } = string.Empty;
}

public readonly struct ReleaseFetchResult
{
    public bool IsNotModified { get; init; }
    public bool IsSuccess { get; init; }
    public GitHubRelease? Release { get; init; }
    public string? TagName { get; init; }
    public string? ETag { get; init; }
}

public sealed class LauncherUpdateService
{
    private static readonly QuiverLauncherProfile Profile = QuiverLauncherProfile.Instance;

    public static readonly TimeSpan DefaultUpdateCheckInterval = TimeSpan.FromMinutes(5);

    public static int ComputePendingUpdatesCount(bool launcherUpdatePending, int gameUpdatesPending) =>
        (launcherUpdatePending ? 1 : 0) + Math.Max(0, gameUpdatesPending);

    /// <summary>
    /// Returns true when a non-manual update check can be skipped (time-based throttle only).
    /// </summary>
    public static bool ShouldSkipUpdateCheck(DateTime lastCheckTime, DateTime utcNow, TimeSpan interval)
    {
        if (lastCheckTime == DateTime.MinValue)
            return false;

        return utcNow - lastCheckTime < interval;
    }

    public static bool ShouldSendConditionalRequest(bool isManualCheck, string? etag, string? lastKnownVersion) =>
        !isManualCheck
        && !string.IsNullOrEmpty(etag)
        && !string.IsNullOrWhiteSpace(lastKnownVersion);

    public static void ConfigureGitHubReleaseClient(HttpClient client, string userAgent, string? apiToken = null)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrEmpty(apiToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiToken);
        }
    }

    public static string? TryParseReleaseTagFromJson(string releaseJson)
    {
        if (string.IsNullOrWhiteSpace(releaseJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(releaseJson);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagElement))
                return null;

            var tag = tagElement.GetString();
            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }
        catch
        {
            return null;
        }
    }

    public static GitHubRelease? TryDeserializeRelease(string releaseJson)
    {
        if (string.IsNullOrWhiteSpace(releaseJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GitHubRelease>(releaseJson);
        }
        catch
        {
            return null;
        }
    }

    public static GitHubRelease? ParseReleaseFromJson(string releaseJson)
    {
        var tagName = TryParseReleaseTagFromJson(releaseJson);
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var release = TryDeserializeRelease(releaseJson);
        if (release == null)
        {
            return new GitHubRelease
            {
                tag_name = tagName,
                assets = [],
            };
        }

        if (string.IsNullOrWhiteSpace(release.tag_name))
            release.tag_name = tagName;

        return release;
    }

    public static async Task<ReleaseFetchResult> FetchLatestReleaseAsync(
        HttpClient httpClient,
        string repository,
        bool sendConditionalRequest,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        var apiUrl = $"https://api.github.com/repos/{repository}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

        if (sendConditionalRequest && !string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new ReleaseFetchResult { IsNotModified = true };
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var release = ParseReleaseFromJson(body);
        var tagName = release?.tag_name;

        return new ReleaseFetchResult
        {
            IsSuccess = !string.IsNullOrWhiteSpace(tagName),
            Release = release,
            TagName = tagName,
            ETag = response.Headers.ETag?.Tag,
        };
    }

    public string ReadInstalledVersion(string? baseDirectory = null)
    {
        baseDirectory ??= AppDomain.CurrentDomain.BaseDirectory;
        return LauncherVersionService.ReadInstalledVersion(baseDirectory);
    }

    public LauncherUpdateCheckInfo LoadUpdateCheckInfo(string? baseDirectory = null)
    {
        baseDirectory ??= AppDomain.CurrentDomain.BaseDirectory;
        var updateCheckFilePath = Path.Combine(baseDirectory, "update_check.json");

        if (!File.Exists(updateCheckFilePath))
        {
            return new LauncherUpdateCheckInfo
            {
                CurrentVersion = ReadInstalledVersion(baseDirectory),
            };
        }

        try
        {
            var json = File.ReadAllText(updateCheckFilePath);
            var info = JsonSerializer.Deserialize<LauncherUpdateCheckInfo>(json);
            if (info != null)
                return info;
        }
        catch
        {
            // Fall through to empty info
        }

        return new LauncherUpdateCheckInfo
        {
            CurrentVersion = ReadInstalledVersion(baseDirectory),
        };
    }

    public bool IsLauncherUpdatePending(string? baseDirectory = null)
    {
        var info = LoadUpdateCheckInfo(baseDirectory);
        if (!info.UpdateAvailable || string.IsNullOrWhiteSpace(info.LastKnownVersion))
            return false;

        var installedVersion = ReadInstalledVersion(baseDirectory);
        return LauncherVersionService.IsNewerVersion(info.LastKnownVersion, installedVersion);
    }

    public async Task<string?> FetchLatestReleaseTagAsync(HttpClient? httpClient = null, CancellationToken cancellationToken = default)
    {
        var ownsClient = httpClient == null;
        httpClient ??= CreateClient();

        try
        {
            var result = await FetchLatestReleaseAsync(
                httpClient,
                Profile.Repository,
                sendConditionalRequest: false,
                etag: null,
                cancellationToken).ConfigureAwait(false);

            return result.TagName;
        }
        finally
        {
            if (ownsClient)
                httpClient.Dispose();
        }
    }

    public bool IsUpdateAvailable(string installedVersion, string? latestTag) =>
        !string.IsNullOrWhiteSpace(latestTag) &&
        LauncherVersionService.IsNewerVersion(latestTag, installedVersion);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        ConfigureGitHubReleaseClient(client, Profile.CliUserAgent);
        return client;
    }
}
