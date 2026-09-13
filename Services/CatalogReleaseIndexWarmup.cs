using System.Net;
using System.Runtime.CompilerServices;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public enum CatalogReleaseWarmupOutcome { Running, Completed, RateLimited, Failed }
public readonly record struct CatalogReleaseWarmupProgress(int Completed, int Total, CatalogReleaseWarmupOutcome Outcome, GitHubReleaseFetchResult? Failure = null, bool WillRetry = false, int Attempted = 0)
{
    public int Unresolved => Total - Completed;
}
public enum CatalogPlatformCheckState { Pending, Checking, Successful, Failed, Paused }
public sealed record CatalogPlatformTargetState(CatalogPlatformCheckState State, GitHubReleaseFetchResult? Failure = null);

public readonly record struct CatalogReleaseWarmupTarget(
    string? RepositorySource,
    string Repository,
    string? PreferredVersion,
    Func<string> GetApiToken);

public static class CatalogReleaseIndexWarmup
{
    private static readonly ConditionalWeakTable<HttpClient, System.Collections.Concurrent.ConcurrentDictionary<string, CatalogPlatformTargetState>> States = new();
    private static string Key(CatalogReleaseWarmupTarget target) => CatalogPlatformIndex.Key(target.RepositorySource, target.Repository, target.PreferredVersion, target.GetApiToken());
    public static CatalogReleaseWarmupProgress Snapshot(HttpClient client, IEnumerable<CatalogReleaseWarmupTarget> targets, bool running = false, int attempted = 0)
    {
        var states = States.GetOrCreateValue(client);
        var list = targets.ToList();
        var resolved = list.Select(target => states.GetOrAdd(Key(target), _ => new(
            CatalogPlatformIndex.IsFresh(target.RepositorySource, target.Repository, target.PreferredVersion, target.GetApiToken())
                ? CatalogPlatformCheckState.Successful : CatalogPlatformCheckState.Pending))).ToList();
        var failure = resolved.Select(s => s.Failure).Where(f => f != null)
            .OrderByDescending(f => f!.IsRateLimited).ThenBy(f => f!.RetryAt).FirstOrDefault();
        var completed = resolved.Count(s => s.State == CatalogPlatformCheckState.Successful);
        var outcome = running ? CatalogReleaseWarmupOutcome.Running : completed == list.Count ? CatalogReleaseWarmupOutcome.Completed
            : failure?.IsRateLimited == true ? CatalogReleaseWarmupOutcome.RateLimited : CatalogReleaseWarmupOutcome.Failed;
        return new(completed, list.Count, outcome, failure, Attempted: attempted);
    }
    internal static void MarkUnavailable(HttpClient client, IEnumerable<CatalogReleaseWarmupTarget> targets, GitHubReleaseFetchResult failure)
    {
        var states = States.GetOrCreateValue(client);
        foreach (var target in targets)
            states[Key(target)] = new(failure.IsRateLimited ? CatalogPlatformCheckState.Paused : CatalogPlatformCheckState.Failed, failure);
    }
    public const int MaxConcurrency = 2;
    public const int UpdateDebounceMs = 750;
    public const int DiskFlushEvery = 10;

    public static IReadOnlyList<CatalogReleaseWarmupTarget> CollectTargets(
        IEnumerable<CatalogSyncRowItem> rows, Func<GameInfo, string>? getApiToken = null)
    {
        var targets = new Dictionary<string, CatalogReleaseWarmupTarget>(StringComparer.Ordinal);
        // Compatibility callers without an injected context load it once for the
        // whole batch. Session/UI callers supply their already-loaded settings.
        var settings = getApiToken == null ? AppSettings.Load() : null;
        foreach (var row in rows)
        {
            var game = row.External ?? row.Local;
            if (game == null || string.IsNullOrWhiteSpace(game.Repository))
                continue;

            var token = (getApiToken?.Invoke(game) ?? game.GetReleaseApiToken(settings)).Trim();
            var key = CatalogPlatformIndex.Key(game.EffectiveRepositorySource, game.Repository, game.PreferredVersion, token);
            if (targets.ContainsKey(key))
                continue;

            var captured = game;
            targets[key] = new CatalogReleaseWarmupTarget(
                captured.EffectiveRepositorySource,
                captured.Repository,
                captured.PreferredVersion,
                () => token);
        }

        return targets.Values.ToList();
    }

    public static IReadOnlyList<CatalogReleaseWarmupTarget> CollectPending(
        IEnumerable<CatalogSyncRowItem> rows) =>
        CollectTargets(rows)
            .Where(target => !CatalogPlatformIndex.IsFresh(target.RepositorySource, target.Repository, target.PreferredVersion, target.GetApiToken()))
            .ToList();

    public static async Task WarmAsync(
        HttpClient httpClient, IEnumerable<CatalogSyncRowItem> rows, Func<GameInfo, string>? getApiToken,
        CancellationToken cancellationToken, Func<Task>? onUpdated = null,
        Func<CatalogReleaseWarmupProgress, Task>? onProgress = null, bool forceRefresh = false)
    {
        var rowList = rows.ToList();
        var targets = CollectTargets(rowList, getApiToken);
        var states = States.GetOrCreateValue(httpClient);
        var pending = targets.Where(target => forceRefresh ||
            !CatalogPlatformIndex.IsFresh(target.RepositorySource, target.Repository, target.PreferredVersion, target.GetApiToken()) ||
            states.TryGetValue(Key(target), out var state) && state.State != CatalogPlatformCheckState.Successful).ToList();
        foreach (var target in targets)
            if (!pending.Contains(target)) states[Key(target)] = new(CatalogPlatformCheckState.Successful);
            else if (forceRefresh || !states.TryGetValue(Key(target), out var previous) || previous.State == CatalogPlatformCheckState.Successful)
                states[Key(target)] = new(CatalogPlatformCheckState.Pending);
        if (pending.Count == 0)
        {
            if (onProgress != null && !cancellationToken.IsCancellationRequested)
                await onProgress(Snapshot(httpClient, targets)).ConfigureAwait(false);
            return;
        }
        var attempted = 0;
        var failures = new List<GitHubReleaseFetchResult>();
        var paused = new Dictionary<string, GitHubReleaseFetchResult>();
        var dirty = 0;
        using var debounceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var debounceLoop = onUpdated == null ? Task.CompletedTask
            : RunDebounceLoopAsync(onUpdated, () => Interlocked.Exchange(ref dirty, 0), debounceCts.Token);
        async Task Report(CatalogReleaseWarmupOutcome outcome)
        {
            if (onProgress == null || cancellationToken.IsCancellationRequested) return;
            await onProgress(Snapshot(httpClient, targets, outcome == CatalogReleaseWarmupOutcome.Running, attempted)).ConfigureAwait(false);
        }
        try
        {
            await Report(CatalogReleaseWarmupOutcome.Running).ConfigureAwait(false);
            foreach (var target in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = target.GetApiToken();
                var provider = RepositorySourceHelper.Normalize(target.RepositorySource);
                var context = provider + ":" + ReleaseRequestCoordinator.CredentialKey(token);
                if (paused.TryGetValue(context, out var pause))
                {
                    states[Key(target)] = new(CatalogPlatformCheckState.Paused, pause);
                    continue;
                }
                states[Key(target)] = new(CatalogPlatformCheckState.Checking);
                try
                {
                    if (await WarmOneAsync(httpClient, target, token, cancellationToken).ConfigureAwait(false))
                    {
                        states[Key(target)] = new(CatalogPlatformCheckState.Successful);
                        Interlocked.Exchange(ref dirty, 1);
                    }
                    else states[Key(target)] = new(CatalogPlatformCheckState.Failed, new() { ErrorMessage = "Release metadata was not returned." });
                }
                catch (OperationCanceledException) { states[Key(target)] = new(CatalogPlatformCheckState.Pending); throw; }
                catch (ReleaseFetchException ex)
                {
                    failures.Add(ex.Result);
                    states[Key(target)] = new(ex.Result.IsRateLimited ? CatalogPlatformCheckState.Paused : CatalogPlatformCheckState.Failed, ex.Result);
                    if (ex.Result.IsRateLimited || ex.StatusCode == HttpStatusCode.Unauthorized) paused[context] = ex.Result;
                }
                catch (Exception)
                {
                    failures.Add(new() { Provider = provider, IsAuthenticated = !string.IsNullOrWhiteSpace(token),
                        StatusCode = HttpStatusCode.ServiceUnavailable, ErrorMessage = "Release metadata could not be fetched." });
                    states[Key(target)] = new(CatalogPlatformCheckState.Failed, failures[^1]);
                }
                attempted++;
                if (attempted % DiskFlushEvery == 0) CatalogPlatformIndex.Flush();
                await Report(CatalogReleaseWarmupOutcome.Running).ConfigureAwait(false);
            }
        }
        finally
        {
            CatalogPlatformIndex.Flush();
            await debounceCts.CancelAsync().ConfigureAwait(false);
            await debounceLoop.ConfigureAwait(false);
            if (onUpdated != null && Interlocked.Exchange(ref dirty, 0) > 0 && !cancellationToken.IsCancellationRequested)
                await onUpdated().ConfigureAwait(false);
        }
        await Report(failures.Any(f => f.IsRateLimited) ? CatalogReleaseWarmupOutcome.RateLimited
            : failures.Count > 0 ? CatalogReleaseWarmupOutcome.Failed : CatalogReleaseWarmupOutcome.Completed).ConfigureAwait(false);
    }

    internal static async Task<bool> WarmOneAsync(HttpClient httpClient, CatalogReleaseWarmupTarget target,
        string? apiToken, CancellationToken cancellationToken)
    {
        var release = await CatalogReleaseSelection.FetchSelectedAsync(httpClient, target.RepositorySource,
            target.Repository, target.PreferredVersion, apiToken, cancellationToken).ConfigureAwait(false);
        CatalogPlatformIndex.Set(target.RepositorySource, target.Repository, target.PreferredVersion, apiToken, release);
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

}
