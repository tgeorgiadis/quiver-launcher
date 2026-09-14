using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public enum CatalogAddOutcome { Added, AlreadyAdded, FolderConflict }
public sealed record CatalogAddCommit(CatalogAddOutcome Outcome, GameInfo? App, List<GameInfo> Library, string? Error = null);

public interface ICatalogReviewService
{
    async Task<CatalogAddCommit> CommitAddAsync(AppCatalogSource source, GameInfo external, bool autoUpdate)
    {
        var local = await LoadLocalAsync();
        var result = CatalogReviewService.PlanAdd(local, external, autoUpdate);
        if (result.Outcome == CatalogAddOutcome.Added)
            await SaveLocalAsync(result.Library, source, CancellationToken.None);
        return result;
    }
    Task PresentAddedAsync(GameInfo app) => Task.CompletedTask;
    Task ReconcileAddedAsync(List<GameInfo> library, AppCatalogSource? activeSource, IReadOnlyList<CatalogSyncRowItem> activeRows) => Task.CompletedTask;
    Task<List<GameInfo>> LoadLocalAsync();
    Task<List<GameInfo>> LoadCachedAsync(string id);
    Task FetchAsync(AppCatalogSource source);
    Task RefreshPlatformMetadataAsync(AppCatalogSource source, bool force, CancellationToken token) => Task.CompletedTask;
    Task SaveLocalAsync(List<GameInfo> apps, AppCatalogSource source, CancellationToken token);
    void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external);
    void Acknowledge(AppCatalogSource source);
}

public sealed class CatalogReviewService(GameManager manager, SettingsViewModel settings,
    Action sortLibrary, LauncherSession? session = null) : ICatalogReviewService
{
    private readonly Dictionary<string, List<GameInfo>> _definitions = [];
    private readonly Dictionary<string, (DateTime?, string?)> _definitionVersions = [];
    private readonly Dictionary<string, IReadOnlyList<CatalogSyncRowItem>> _comparisons = [];
    public async Task ReconcileAddedAsync(List<GameInfo> library, AppCatalogSource? activeSource, IReadOnlyList<CatalogSyncRowItem> activeRows)
    {
        foreach (var source in settings.Current.AppCatalogSources)
        {
            IReadOnlyList<CatalogSyncRowItem> rows;
            if (source.Id == activeSource?.Id) rows = activeRows;
            else
            {
                if (!_definitions.TryGetValue(source.Id, out var external) ||
                    _definitionVersions.GetValueOrDefault(source.Id) != (source.LastFetchedUtc, source.CachedListVersion))
                {
                    _definitions[source.Id] = external = await manager.CatalogService.LoadCachedAppsAsync(source.Id);
                    _definitionVersions[source.Id] = (source.LastFetchedUtc, source.CachedListVersion);
                    _comparisons.Remove(source.Id);
                }
                _comparisons.TryGetValue(source.Id, out var previous);
                rows = await Task.Run(() => CatalogCompareService.BuildCompareRows(library, external,
                    previous?.Where(r => r.External != null).ToDictionary(r => r.External!.InstanceKey, StringComparer.OrdinalIgnoreCase)));
            }
            _comparisons[source.Id] = rows;
            source.LibraryAppCount = rows.Count(r => r.Local != null);
            source.ListAppCount = rows.Count;
            CatalogReviewEligibility.Reconcile(source, rows, settings.Current);
        }
        var pending = settings.Current.AppCatalogSources.Where(s => s.Enabled)
            .SelectMany(s => _comparisons.GetValueOrDefault(s.Id, []).Where(r => r.Status == CatalogSyncStatus.Changed &&
                CatalogReviewEligibility.IsPending(r, s, [CatalogPlatformSupport.DetectRuntimePlatform()],
                    (r.External ?? r.Local)?.GetReleaseApiToken(settings.Current))))
            .Select(r => r.IdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var game in manager.Games)
            if (game.HasPendingCatalogChanges != pending.Contains(game.InstanceKey))
                game.HasPendingCatalogChanges = pending.Contains(game.InstanceKey);
    }

    public static CatalogAddCommit PlanAdd(List<GameInfo> local, GameInfo external, bool autoUpdate)
    {
        var existing = CatalogCompareService.FindExistingCatalogEntry(local, external);
        if (existing != null) return new(CatalogAddOutcome.AlreadyAdded, existing, local);
        var occupant = local.FirstOrDefault(a => !string.IsNullOrWhiteSpace(external.FolderName) &&
            string.Equals(a.FolderName?.Trim(), external.FolderName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (occupant != null) return new(CatalogAddOutcome.FolderConflict, null, local,
            CatalogCompareService.FormatAddBlockedReason(external, occupant));
        var app = CatalogCompareService.CloneForLocal(external, autoUpdate);
        return new(CatalogAddOutcome.Added, app, [.. local, app]);
    }

    public Task<CatalogAddCommit> CommitAddAsync(AppCatalogSource source, GameInfo external, bool autoUpdate) => Task.Run(async () =>
    {
        // This read is inside the session queue, never a snapshot captured at click time.
        var result = PlanAdd(await manager.CatalogService.LoadLocalAppsForMutationAsync(), external, autoUpdate);
        if (result.Outcome == CatalogAddOutcome.Added)
            await manager.CatalogService.SaveLocalAppsAsync(result.Library);
        return result;
    });

    public async Task PresentAddedAsync(GameInfo app)
    {
        var preferredVersion = app.PreferredVersion;
        app.GameManager = manager;
        // Prevent the library's image control from implicitly downloading a missing cover.
        app.CachedArtworkOnly = true;
        // Only the newly saved app needs preparation, local status, and cached artwork.
        // No remote icon or release requests belong on the Add path.
        var preparation = Task.Run(() =>
        {
            if (app.IsManuallyManaged) ManualAppFolderService.EnsurePrepared(app, manager.GamesFolder);
            else AppFilesToAddService.SyncForGame(app, manager.GamesFolder, null);
        });
        app.CatalogPreparation = preparation;
        Exception? failure = null;
        try { await preparation; }
        catch (Exception ex) { failure = ex; }
        try { await Task.Run(async () =>
        {
            await app.CheckStatusAsync(manager.HttpClient, manager.GamesFolder, checkRemoteVersion: false);
            if (CatalogPlatformIndex.TryGet(app.EffectiveRepositorySource, app.Repository, preferredVersion,
                    app.GetReleaseApiToken(settings.Current), out var metadata) && !string.IsNullOrWhiteSpace(metadata?.ReleaseTag))
                app.ApplyCatalogVersionHint(metadata.ReleaseTag, preferredVersion);
            app.LoadCustomIcon(manager.CacheFolder);
            await app.LoadAndCacheDefaultIconAsync(manager.CacheFolder, allowDownload: false);
        }); }
        catch (Exception ex) { failure ??= ex; }
        await manager.InsertCatalogAppAsync(app, settings.Current);
        // Network enrichment has its own session lifetime and never holds the save
        // queue, the Add feedback, or another app's addition open.
        if (session != null) _ = session.RunAsync(() => Task.Run(() => EnrichAddedAsync(app, session.Token), session.Token));
        if (failure != null) throw new IOException("The app was saved, but its files could not be prepared.", failure);
    }

    internal async Task EnrichAddedAsync(GameInfo app, CancellationToken cancellationToken)
    {
        async Task Artwork()
        {
            try { await app.CompleteCatalogArtworkAsync(cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { System.Diagnostics.Debug.WriteLine($"Library artwork unavailable: {ex.GetType().Name}"); }
        }
        async Task Release()
        {
            if (app.IsManuallyManaged || string.IsNullOrWhiteSpace(app.Repository)) return;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ReleaseSourceRegistry.Default.FetchReleasesAsync(manager.HttpClient,
                    app.RepositorySource, app.Repository, app.GetReleaseApiToken(settings.Current),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.IsRateLimited && attempt < 2 && result.RetryAt is { } retry)
                {
                    var delay = retry - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                    continue;
                }
                result.EnsureSuccess();
                var release = GameInfo.SelectLatestRelease(result.Releases, app.PreferredVersion, app.InstalledVersion, result.LatestTag);
                cancellationToken.ThrowIfCancellationRequested();
                if (release != null)
                {
                    app.ApplyCachedRelease(release.tag_name, release);
                    GitHubApiCache.SetCache(app.RepositorySource, app.Repository, release.tag_name, result.ETag ?? "", release);
                    app.RefreshInstalledStatus();
                }
                return;
            }
        }
        // A missing image cannot prevent release resolution (or vice versa).
        await Task.WhenAll(Artwork(), Release());
    }
    public Task<List<GameInfo>> LoadLocalAsync() => manager.CatalogService.LoadLocalAppsForMutationAsync();
    public async Task<List<GameInfo>> LoadCachedAsync(string id)
    {
        var apps = await manager.CatalogService.LoadCachedAppsAsync(id);
        _definitions[id] = apps;
        var source = settings.Current.AppCatalogSources.FirstOrDefault(s => s.Id == id);
        if (source != null) _definitionVersions[id] = (source.LastFetchedUtc, source.CachedListVersion);
        _comparisons.Remove(id);
        return apps;
    }
    public Task RefreshPlatformMetadataAsync(AppCatalogSource source, bool force, CancellationToken token) =>
        PublishedPlatformCache.RefreshAsync(manager.HttpClient, source.PlatformMetadataUrl, force, token);
    public Task FetchAsync(AppCatalogSource source) => manager.CatalogService.FetchSourceAsync(manager.HttpClient, source);
    public void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external) =>
        manager.CatalogService.RefreshUpdateAvailable(source, local, external);
    public void Acknowledge(AppCatalogSource source) => manager.CatalogService.AcknowledgeSourceVersion(source);
    public async Task SaveLocalAsync(List<GameInfo> localApps, AppCatalogSource source, CancellationToken token)
    {
        _comparisons.Clear();
        var previousApps = await LoadLocalAsync();
        token.ThrowIfCancellationRequested();
        var previousByIdentity = CatalogCompareService.IndexByInstanceKey(previousApps);
        var mutatedApps = CatalogCompareService.GetAppsNeedingInstallSync(previousApps, localApps);
        await manager.CatalogService.SaveLocalAppsAsync(localApps);
        // Complete filesystem preparation once the catalog has been saved, before dependencies can be disposed.
        foreach (var app in mutatedApps)
        {
            previousByIdentity.TryGetValue(app.InstanceKey, out var previous);
            previous ??= string.IsNullOrWhiteSpace(app.FolderName) ? null : previousApps.FirstOrDefault(p =>
                string.Equals(p.FolderName, app.FolderName, StringComparison.OrdinalIgnoreCase));
            if (app.IsManuallyManaged) ManualAppFolderService.EnsurePrepared(app, manager.GamesFolder);
            else AppFilesToAddService.SyncForGame(app, manager.GamesFolder, previous?.FilesToAdd);
        }
        token.ThrowIfCancellationRequested();
        settings.SaveCurrent();
        // Adding to the library needs only local status and cached icons. Remote
        // release checks and icon downloads must not hold up catalog mutations.
        await manager.ReloadLibraryFromDiskAsync(mutatedApps.Select(app => app.InstanceKey), allowNetwork: false);
        token.ThrowIfCancellationRequested();
        sortLibrary();
    }
}
