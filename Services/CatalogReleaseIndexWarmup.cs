using System.Net;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public readonly record struct CatalogReleaseWarmupTarget(
    string? RepositorySource,
    string Repository,
    string? PreferredVersion,
    Func<string> GetApiToken);

public static class CatalogReleaseIndexWarmup
{
    public const int MaxConcurrency = 2;
    public const int UpdateDebounceMs = 750;
    public const int DiskFlushEvery = 10;

    public static IReadOnlyList<CatalogReleaseWarmupTarget> CollectTargets(
        IEnumerable<CatalogSyncRowItem> rows)
    {
        var targets = new Dictionary<string, CatalogReleaseWarmupTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var game = row.External ?? row.Local;
            if (game == null || string.IsNullOrWhiteSpace(game.Repository))
                continue;

            var key = GitHubApiCache.GetCacheKey(game.EffectiveRepositorySource, game.Repository);
            if (targets.ContainsKey(key))
                continue;

            var captured = game;
            targets[key] = new CatalogReleaseWarmupTarget(
                captured.EffectiveRepositorySource,
                captured.Repository,
                captured.PreferredVersion,
                () => captured.GetReleaseApiToken());
        }

        return targets.Values.ToList();
    }

    public static IReadOnlyList<CatalogReleaseWarmupTarget> CollectPending(
        IEnumerable<CatalogSyncRowItem> rows) =>
        CollectTargets(rows)
            .Where(target => !GitHubApiCache.HasFreshAssetIndex(target.RepositorySource, target.Repository))
            .ToList();

    public static async Task WarmAsync(
        HttpClient httpClient,
        IEnumerable<CatalogSyncRowItem> rows,
        Func<GameInfo, string>? getApiToken,
        CancellationToken cancellationToken,
        Func<Task>? onUpdated = null)
    {
        var rowList = rows as IReadOnlyList<CatalogSyncRowItem> ?? rows.ToList();
        var pending = CollectPending(rowList);
        if (pending.Count == 0)
            return;

        var dirty = 0;
        var warmedSinceFlush = 0;
        using var debounceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var debounceLoop = onUpdated == null
            ? Task.CompletedTask
            : RunDebounceLoopAsync(onUpdated, () => Interlocked.Exchange(ref dirty, 0), debounceCts.Token);

        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var stop = false;

        var tasks = pending.Select(async target =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (stop || cancellationToken.IsCancellationRequested)
                    return;

                var token = ResolveToken(rowList, target, getApiToken);
                var updated = await WarmOneAsync(httpClient, target, token, cancellationToken).ConfigureAwait(false);
                if (!updated)
                    return;

                Interlocked.Increment(ref dirty);
                if (Interlocked.Increment(ref warmedSinceFlush) % DiskFlushEvery == 0)
                    GitHubApiCache.Flush();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (IsConfirmedRateLimit(ex.StatusCode, ex.Message))
            {
                stop = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Catalog release index warmup skipped {target.Repository}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller left the catalog review list.
        }
        finally
        {
            GitHubApiCache.Flush();
            try
            {
                await debounceCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                await debounceLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            if (onUpdated != null && Interlocked.Exchange(ref dirty, 0) > 0)
            {
                try
                {
                    await onUpdated().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    internal static async Task<bool> WarmOneAsync(
        HttpClient httpClient,
        CatalogReleaseWarmupTarget target,
        string? apiToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var useLatestOnly = RepositorySourceHelper.IsGitHub(target.RepositorySource) &&
                            string.IsNullOrWhiteSpace(target.PreferredVersion);
        var etag = GitHubApiCache.GetETag(target.RepositorySource, target.Repository);
        GitHubReleaseFetchResult result;
        try
        {
            result = useLatestOnly
                ? await GitHubReleaseService.FetchLatestReleaseIndexAsync(
                    httpClient,
                    target.Repository,
                    apiToken,
                    string.IsNullOrWhiteSpace(etag) ? null : etag).ConfigureAwait(false)
                : await ReleaseSourceRegistry.Default.FetchReleasesAsync(
                    httpClient,
                    target.RepositorySource,
                    target.Repository,
                    apiToken,
                    string.IsNullOrWhiteSpace(etag) ? null : etag).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return HandleFetchFailure(target, ex);
        }

        ThrowIfRateLimited(result);

        if (IsPerRepoAccessDenied(result.StatusCode))
            return CacheEmptyIndex(target, result.ETag);

        if (result.IsNotModified)
        {
            if (TryRefreshNotModified(target))
                return true;

            if (GitHubApiCache.HasFreshAssetIndex(target.RepositorySource, target.Repository))
                return false;

            try
            {
                result = await FetchFullIndexAsync(httpClient, target, apiToken, etag: null).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                return HandleFetchFailure(target, ex);
            }
        }
        else if (useLatestOnly &&
                 (result.StatusCode == HttpStatusCode.NotFound ||
                  (result.StatusCode == HttpStatusCode.OK && result.Releases.Count == 0)))
        {
            try
            {
                result = await FetchFullIndexAsync(httpClient, target, apiToken, etag: null).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                return HandleFetchFailure(target, ex);
            }
        }

        ThrowIfRateLimited(result);

        if (IsPerRepoAccessDenied(result.StatusCode))
            return CacheEmptyIndex(target, result.ETag);

        if (!IsUsableIndexStatus(result.StatusCode))
            return false;

        if (result.Releases.Count == 0)
            return CacheEmptyIndex(target, result.ETag);

        var latest = ReleaseSelection.SelectLatestRelease(
            result.Releases,
            target.PreferredVersion,
            installedVersion: null,
            result.LatestTag);
        if (latest == null || string.IsNullOrWhiteSpace(latest.tag_name))
            return CacheEmptyIndex(target, result.ETag);

        GitHubApiCache.SetCache(
            target.RepositorySource,
            target.Repository,
            latest.tag_name,
            result.ETag ?? string.Empty,
            latest,
            persist: false);
        return true;
    }

    private static async Task RunDebounceLoopAsync(
        Func<Task> onUpdated,
        Func<int> takeDirty,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(UpdateDebounceMs, cancellationToken).ConfigureAwait(false);
                if (takeDirty() <= 0)
                    continue;

                await onUpdated().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool TryRefreshNotModified(CatalogReleaseWarmupTarget target)
    {
        if (!GitHubApiCache.TryGetCachedVersion(target.RepositorySource, target.Repository, out var existing) ||
            existing?.CachedRelease == null)
        {
            return false;
        }

        GitHubApiCache.SetCache(
            target.RepositorySource,
            target.Repository,
            existing.Version,
            existing.ETag,
            existing.CachedRelease,
            persist: false);
        return true;
    }

    private static Task<GitHubReleaseFetchResult> FetchFullIndexAsync(
        HttpClient httpClient,
        CatalogReleaseWarmupTarget target,
        string? apiToken,
        string? etag) =>
        ReleaseSourceRegistry.Default.FetchReleasesAsync(
            httpClient,
            target.RepositorySource,
            target.Repository,
            apiToken,
            etag);

    private static string? ResolveToken(
        IEnumerable<CatalogSyncRowItem> rows,
        CatalogReleaseWarmupTarget target,
        Func<GameInfo, string>? getApiToken)
    {
        if (getApiToken == null)
            return target.GetApiToken();

        var game = rows
            .Select(row => row.External ?? row.Local)
            .FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.Repository, target.Repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    candidate.EffectiveRepositorySource,
                    RepositorySourceHelper.Normalize(target.RepositorySource),
                    StringComparison.OrdinalIgnoreCase));

        return game == null ? target.GetApiToken() : getApiToken(game);
    }

    private static bool HandleFetchFailure(CatalogReleaseWarmupTarget target, HttpRequestException ex)
    {
        if (IsConfirmedRateLimit(ex.StatusCode, ex.Message))
            throw new HttpRequestException("Release index warmup was rate limited.", ex, ex.StatusCode);

        if (ex.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.NotFound)
        {
            return CacheEmptyIndex(target, etag: null);
        }

        System.Diagnostics.Debug.WriteLine(
            $"Catalog release index warmup skipped {target.Repository}: {ex.Message}");
        return false;
    }

    private static bool CacheEmptyIndex(CatalogReleaseWarmupTarget target, string? etag)
    {
        GitHubApiCache.SetCache(
            target.RepositorySource,
            target.Repository,
            version: string.Empty,
            etag ?? string.Empty,
            new GitHubRelease { tag_name = string.Empty, assets = [] },
            persist: false,
            replaceAssetNames: true);
        return true;
    }

    private static void ThrowIfRateLimited(GitHubReleaseFetchResult result)
    {
        if (result.IsRateLimited ||
            IsConfirmedRateLimit(result.StatusCode, result.ErrorMessage))
        {
            throw new HttpRequestException("Release index warmup was rate limited.", null, result.StatusCode);
        }
    }

    private static bool IsConfirmedRateLimit(HttpStatusCode? status, string? message) =>
        status == HttpStatusCode.TooManyRequests ||
        (status == HttpStatusCode.Forbidden && GitHubReleaseService.LooksLikeRateLimitMessage(message));

    private static bool IsPerRepoAccessDenied(HttpStatusCode status) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;

    private static bool IsUsableIndexStatus(HttpStatusCode status) =>
        status == HttpStatusCode.OK || status == HttpStatusCode.NotFound;
}
