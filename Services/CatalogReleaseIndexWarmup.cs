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
        var pending = CollectPending(rows);
        if (pending.Count == 0)
            return;

        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var stop = false;

        var tasks = pending.Select(async target =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (stop || cancellationToken.IsCancellationRequested)
                    return;

                var token = ResolveToken(rows, target, getApiToken);
                var updated = await WarmOneAsync(httpClient, target, token, cancellationToken).ConfigureAwait(false);
                if (updated && onUpdated != null)
                    await onUpdated().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (IsRateLimited(ex.StatusCode))
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
    }

    internal static async Task<bool> WarmOneAsync(
        HttpClient httpClient,
        CatalogReleaseWarmupTarget target,
        string? apiToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var etag = GitHubApiCache.GetETag(target.RepositorySource, target.Repository);
        var result = await ReleaseSourceRegistry.Default.FetchReleasesAsync(
            httpClient,
            target.RepositorySource,
            target.Repository,
            apiToken,
            string.IsNullOrWhiteSpace(etag) ? null : etag).ConfigureAwait(false);

        if (result.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Release index warmup was rate limited.", null, result.StatusCode);

        if (result.IsNotModified)
        {
            if (GitHubApiCache.TryGetCachedVersion(target.RepositorySource, target.Repository, out var existing) &&
                existing?.CachedRelease != null)
            {
                GitHubApiCache.SetCache(
                    target.RepositorySource,
                    target.Repository,
                    existing.Version,
                    existing.ETag,
                    existing.CachedRelease);
                return true;
            }

            if (GitHubApiCache.HasFreshAssetIndex(target.RepositorySource, target.Repository))
                return false;

            result = await ReleaseSourceRegistry.Default.FetchReleasesAsync(
                httpClient,
                target.RepositorySource,
                target.Repository,
                apiToken,
                etag: null).ConfigureAwait(false);
        }

        if (result.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Release index warmup was rate limited.", null, result.StatusCode);

        if (result.Releases.Count == 0)
            return false;

        var latest = ReleaseSelection.SelectLatestRelease(
            result.Releases,
            target.PreferredVersion,
            installedVersion: null,
            result.LatestTag);
        if (latest == null || string.IsNullOrWhiteSpace(latest.tag_name))
            return false;

        GitHubApiCache.SetCache(
            target.RepositorySource,
            target.Repository,
            latest.tag_name,
            result.ETag ?? string.Empty,
            latest);
        return true;
    }

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

    private static bool IsRateLimited(HttpStatusCode? status) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests;
}
