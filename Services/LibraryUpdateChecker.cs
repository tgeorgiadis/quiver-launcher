using System.Diagnostics;
using System.Net;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public enum AppCheckOutcome { Successful, Failed, Cancelled, RateLimited }
public sealed record AppCheckResult(string IdentityKey, AppCheckOutcome Outcome);
public sealed record AppCheckProgress(int Completed, int Total, AppCheckResult? Result = null);
public sealed record LibraryCheckResult(IReadOnlyList<AppCheckResult> Apps)
{
    public int Successful => Apps.Count(a => a.Outcome == AppCheckOutcome.Successful);
    public int Failed => Apps.Count(a => a.Outcome == AppCheckOutcome.Failed);
    public int Cancelled => Apps.Count(a => a.Outcome == AppCheckOutcome.Cancelled);
    public int RateLimited => Apps.Count(a => a.Outcome == AppCheckOutcome.RateLimited);
    public bool Complete => Successful == Apps.Count;
    public static LibraryCheckResult Empty { get; } = new([]);
}

/// <summary>Metadata-only checks. A pass shares raw responses, never selected releases, between entries.</summary>
public sealed class LibraryUpdateChecker(HttpClient client, AppSettings settings)
{
    public async Task<LibraryCheckResult> CheckAsync(IEnumerable<GameInfo> apps, bool force,
        TimeSpan cacheAge, IProgress<AppCheckProgress>? progress, CancellationToken token)
    {
        var targets = apps.Where(a => !a.IsManuallyManaged && !string.IsNullOrWhiteSpace(a.Repository)).ToArray();
        var results = new List<AppCheckResult>();
        var pass = new ReleasePass(client);
        using var cacheScope = force ? null : ReleaseRequestCoordinator.AllowCachedMetadata(cacheAge);
        var coordinator = ReleaseRequestCoordinator.For(client);
        var startRequests = coordinator.RequestCount;
        var watch = Stopwatch.StartNew();
        progress?.Report(new(0, targets.Length));
        foreach (var app in targets)
        {
            AppCheckOutcome outcome;
            try
            {
                token.ThrowIfCancellationRequested();
                var selected = await pass.SelectAsync(app, app.GetReleaseApiToken(settings), token);
                token.ThrowIfCancellationRequested();
                if (selected == null || string.IsNullOrWhiteSpace(selected.tag_name)) outcome = AppCheckOutcome.Failed;
                else
                {
                    app.ApplyCachedRelease(selected.tag_name, selected);
                    GitHubApiCache.SetCache(app.RepositorySource, app.Repository!, selected.tag_name, "", selected);
                    app.RefreshInstalledStatus();
                    outcome = AppCheckOutcome.Successful;
                }
            }
            catch (ReleaseFetchException ex) when (ex.Result.IsRateLimited) { outcome = AppCheckOutcome.RateLimited; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { outcome = AppCheckOutcome.Cancelled; }
            catch (Exception) { outcome = AppCheckOutcome.Failed; }
            results.Add(new(app.InstanceKey, outcome));
            progress?.Report(new(results.Count, targets.Length, results[^1]));
        }
        Trace.WriteLine($"Update check: apps={targets.Length}, successful={results.Count(r => r.Outcome == AppCheckOutcome.Successful)}, requests={coordinator.RequestCount - startRequests}, elapsedMs={watch.ElapsedMilliseconds}");
        return new(results);
    }

    private sealed class ReleasePass(HttpClient client)
    {
        private readonly Dictionary<string, Task<GitHubReleaseFetchResult>> _responses = new(StringComparer.Ordinal);
        private Task<GitHubReleaseFetchResult> Once(string key, Func<Task<GitHubReleaseFetchResult>> fetch)
        {
            if (!_responses.TryGetValue(key, out var task)) _responses[key] = task = fetch();
            return task;
        }

        public async Task<GitHubRelease?> SelectAsync(GameInfo app, string? credential, CancellationToken token)
        {
            var repository = app.Repository!.Trim();
            var key = app.EffectiveRepositorySource + ":" + repository.ToLowerInvariant() + ":" + ReleaseRequestCoordinator.CredentialKey(credential);
            if (!RepositorySourceHelper.IsGitHub(app.RepositorySource))
            {
                var other = await Once(key, () => ReleaseSourceRegistry.Default.FetchReleasesAsync(client,
                    app.RepositorySource, repository, credential, cancellationToken: token));
                other.EnsureSuccess();
                return ReleaseSelection.SelectLatestRelease(other.Releases, app.PreferredVersion, app.InstalledVersion, other.LatestTag);
            }
            var latest = await Once(key + ":latest", () => GitHubReleaseService.FetchLatestReleaseIndexAsync(client, repository, credential, cancellationToken: token));
            if (latest.IsRateLimited || latest.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) latest.EnsureSuccess();
            if (string.IsNullOrWhiteSpace(app.PreferredVersion) && latest.StatusCode == HttpStatusCode.OK &&
                ReleaseSelection.SelectLatestRelease(latest.Releases, githubLatestTag: latest.LatestTag) is { } selected)
                return selected;
            var list = await Once(key + ":list", () => GitHubReleaseService.FetchReleaseListAsync(client, repository, credential, token));
            list.EnsureSuccess();
            var releases = list.Releases.Concat(latest.StatusCode == HttpStatusCode.OK ? latest.Releases : []).ToArray();
            return ReleaseSelection.SelectLatestRelease(releases, app.PreferredVersion, app.InstalledVersion, latest.LatestTag);
        }
    }
}
