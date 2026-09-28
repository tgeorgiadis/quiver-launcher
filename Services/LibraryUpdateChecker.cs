using System.Diagnostics;
using System.Net;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public enum AppCheckOutcome { Successful, Failed, Cancelled, RateLimited }
public sealed record AppCheckResult(string IdentityKey, AppCheckOutcome Outcome, string? AppName = null, string? Reason = null)
{
    public bool CanRetry { get; init; }
}
public sealed record AppCheckProgress(int Completed, int Total, AppCheckResult? Result = null,
    IReadOnlyList<AppCheckResult>? Targets = null);
public sealed record LibraryCheckResult(IReadOnlyList<AppCheckResult> Apps, IReadOnlyList<string>? InvalidTokenProviders = null)
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
    internal async Task<LibraryCheckResult> CheckStartupAsync(IEnumerable<GameInfo> apps, CancellationToken token,
        TimeSpan? retryDelay = null)
    {
        var all = apps.Where(app => !app.IsManuallyManaged && !string.IsNullOrWhiteSpace(app.Repository)).ToArray();
        var targets = all.Where(app => !StartupVersionResolver.Apply(app)).ToArray();
        var resolved = all.Except(targets).Select(app => new AppCheckResult(app.InstanceKey, AppCheckOutcome.Successful, app.DisplayName));
        var checkedApps = await CheckAsync(targets, true, TimeSpan.Zero, null, token, preservePreferredVersion: true);
        var first = new LibraryCheckResult(resolved.Concat(checkedApps.Apps).ToArray(), checkedApps.InvalidTokenProviders);
        var retryKeys = first.Apps.Where(a => a.CanRetry).Select(a => a.IdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (retryKeys.Count == 0) return first;
        await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(5), token);
        var retry = await CheckAsync(targets.Where(a => retryKeys.Contains(a.InstanceKey)), true, TimeSpan.Zero, null, token, preservePreferredVersion: true);
        return new(first.Apps.Where(a => !retryKeys.Contains(a.IdentityKey)).Concat(retry.Apps).ToArray(),
            (first.InvalidTokenProviders ?? []).Concat(retry.InvalidTokenProviders ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<LibraryCheckResult> CheckAsync(IEnumerable<GameInfo> apps, bool force,
        TimeSpan cacheAge, IProgress<AppCheckProgress>? progress, CancellationToken token, bool preservePreferredVersion = false)
    {
        var targets = apps.Where(a => !a.IsManuallyManaged && !string.IsNullOrWhiteSpace(a.Repository)).ToArray();
        var results = new AppCheckResult[targets.Length];
        var pass = new ReleasePass(client);
        using var cacheScope = force ? null : ReleaseRequestCoordinator.AllowCachedMetadata(cacheAge);
        var coordinator = ReleaseRequestCoordinator.For(client);
        var credentialsToValidate = new Dictionary<string, (string Provider, string Token)>();
        var startRequests = coordinator.RequestCount;
        var watch = Stopwatch.StartNew();
        progress?.Report(new(0, targets.Length, Targets: targets.Select(app =>
            new AppCheckResult(app.InstanceKey, AppCheckOutcome.Cancelled, app.DisplayName,
                "The update check was cancelled before this app could be checked.")).ToArray()));
        var completed = 0;
        await Task.WhenAll(targets.Select(async (app, index) =>
        {
            AppCheckOutcome outcome;
            string? reason = null;
            var canRetry = false;
            try
            {
                token.ThrowIfCancellationRequested();
                // quiverlauncher.com already said which release a catalog app updates to; GitHub isn't asked.
                if (StartupVersionResolver.FromCatalog(app) is { } listed && listed.IsFresh(DateTimeOffset.UtcNow))
                {
                    app.ApplyStartupVersion(listed);
                    app.RepositoryCheckError = null;
                    var catalogResult = new AppCheckResult(app.InstanceKey, AppCheckOutcome.Successful, app.DisplayName);
                    results[index] = catalogResult;
                    progress?.Report(new(Interlocked.Increment(ref completed), targets.Length, catalogResult));
                    return;
                }
                var selected = await app.CatalogReleaseAsync(token) ?? await pass.SelectAsync(app, app.GetReleaseApiToken(settings), token);
                token.ThrowIfCancellationRequested();
                if (selected == null || string.IsNullOrWhiteSpace(selected.tag_name))
                {
                    outcome = AppCheckOutcome.Failed;
                    reason = NoEligibleReleaseReason(app);
                }
                else
                {
                    if (preservePreferredVersion)
                        app.ApplyStartupVersion(new(selected.tag_name, DateTimeOffset.UtcNow, selected));
                    else app.ApplyCachedRelease(selected.tag_name, selected);
                    GitHubApiCache.SetCache(app.RepositorySource, app.Repository!, selected.tag_name, "", selected);
                    app.RefreshInstalledStatus();
                    outcome = AppCheckOutcome.Successful;
                }
            }
            catch (ReleaseFetchException ex) when (ex.Result.IsRateLimited)
            { outcome = AppCheckOutcome.RateLimited; reason = "Release service rate limit reached. Try again later."; }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { outcome = AppCheckOutcome.Cancelled; reason = "The update check was cancelled before this app could be checked."; }
            catch (Exception ex)
            {
                outcome = AppCheckOutcome.Failed;
                reason = DescribeFailure(ex);
                canRetry = IsTransient(ex);
                var credential = app.GetReleaseApiToken(settings);
                if (IsUnauthorized(ex) && !string.IsNullOrWhiteSpace(credential))
                {
                    var key = app.EffectiveRepositorySource + ":" + ReleaseRequestCoordinator.CredentialKey(credential);
                    lock (credentialsToValidate) credentialsToValidate.TryAdd(key, (app.EffectiveRepositorySource, credential));
                }
            }
            if (outcome != AppCheckOutcome.Cancelled)
                app.RepositoryCheckError = outcome == AppCheckOutcome.Successful ? null : reason;
            var result = new AppCheckResult(app.InstanceKey, outcome, app.DisplayName, reason) { CanRetry = canRetry };
            results[index] = result;
            progress?.Report(new(Interlocked.Increment(ref completed), targets.Length, result));
        }));
        var invalidProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, credential) in credentialsToValidate)
        {
            var validation = await ReleaseTokenValidator.ValidateAsync(client, credential.Provider, credential.Token, token);
            if (!validation.IsValid) invalidProviders.Add(credential.Provider);
        }
        Trace.WriteLine($"Update check: apps={targets.Length}, successful={results.Count(r => r.Outcome == AppCheckOutcome.Successful)}, requests={coordinator.RequestCount - startRequests}, elapsedMs={watch.ElapsedMilliseconds}");
        return new(results, invalidProviders.ToArray());
    }

    private static bool IsTransient(Exception error) => error switch
    {
        ReleaseFetchException { Result.IsRateLimited: true } => false,
        OperationCanceledException or TimeoutException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500 || status == HttpStatusCode.RequestTimeout,
        _ => false
    };

    private static bool IsUnauthorized(Exception error) =>
        error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized };

    internal static string NoEligibleReleaseReason(GameInfo app) => string.IsNullOrWhiteSpace(app.PreferredVersion)
        ? "No eligible release was found."
        : "No eligible release matched the preferred version.";

    internal static string DescribeFailure(Exception error) => error switch
    {
        ReleaseFetchException { Result.IsRateLimited: true } => "Release service rate limit reached. Try again later.",
        OperationCanceledException or TimeoutException => "The release service took too long to respond. Try again.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "Authentication failed (HTTP 401). Check your API token.",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "Access denied (HTTP 403). Check repository access and API token permissions.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "Repository or releases not found (HTTP 404).\n\nOpen Customize → Edit Entry to fix the repository or select ‘Manually managed’ to disable update checks.",
        HttpRequestException { StatusCode: { } status } => $"Release service returned HTTP {(int)status}. Try again later.",
        HttpRequestException => "Could not connect to the release service. Check your connection and try again.",
        System.Text.Json.JsonException => "The release service returned an unreadable response.",
        _ => "Could not read release information. Try again; if it persists, check the app's repository settings.",
    };

    private sealed class ReleasePass(HttpClient client)
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Task<GitHubReleaseFetchResult>> _responses = new(StringComparer.Ordinal);
        private Task<GitHubReleaseFetchResult> Once(string key, Func<Task<GitHubReleaseFetchResult>> fetch)
        {
            lock (_gate)
            {
                if (!_responses.TryGetValue(key, out var task)) _responses[key] = task = fetch();
                return task;
            }
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
                return ReleaseSelection.SelectLatestRelease(other.Releases, app.ReleaseTarget, app.InstalledVersion, other.LatestTag);
            }
            var latest = await Once(key + ":latest", () => GitHubReleaseService.FetchLatestReleaseIndexAsync(client, repository, credential, cancellationToken: token));
            if (latest.IsRateLimited || latest.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) latest.EnsureSuccess();
            // A pinned or verified release may not be GitHub's latest; that needs the release list.
            if (string.IsNullOrWhiteSpace(app.ReleaseTarget) && latest.StatusCode == HttpStatusCode.OK &&
                ReleaseSelection.SelectLatestRelease(latest.Releases, githubLatestTag: latest.LatestTag) is { } selected)
                return selected;
            var list = await Once(key + ":list", () => GitHubReleaseService.FetchReleaseListAsync(client, repository, credential, token));
            list.EnsureSuccess();
            var releases = list.Releases.Concat(latest.StatusCode == HttpStatusCode.OK ? latest.Releases : []).ToArray();
            return ReleaseSelection.SelectLatestRelease(releases, app.ReleaseTarget, app.InstalledVersion, latest.LatestTag);
        }
    }
}
