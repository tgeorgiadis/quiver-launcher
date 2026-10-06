using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public enum LibraryAddOutcome { Added, AlreadyAdded, FolderConflict }
public sealed record LibraryAddResult(LibraryAddOutcome Outcome, GameInfo? App, List<GameInfo> Library, string? Error = null);

/// <summary>Adds one app from Browse to the library: save, prepare its folder, then show it.</summary>
public sealed class LibraryAddService(GameManager manager, SettingsViewModel settings, LauncherSession session)
{
    /// <summary>Saves through the session's write queue, so adds and removes never interleave.</summary>
    public async Task<LibraryAddResult> AddAsync(GameInfo external)
    {
        LibraryAddResult? result = null;
        await session.CatalogMutations.Enqueue(async () =>
        {
            result = await CommitAddAsync(external, settings.Current.AutoUpdateNewlyAddedApps);
            if (result.Outcome == LibraryAddOutcome.Added)
                await PresentAddedAsync(result.App!);
        });
        return result ?? throw new InvalidOperationException("The app could not be added.");
    }

    public static LibraryAddResult PlanAdd(List<GameInfo> local, GameInfo external, bool autoUpdate)
    {
        var existing = FindExisting(local, external);
        if (existing != null) return new(LibraryAddOutcome.AlreadyAdded, existing, local);
        var occupant = local.FirstOrDefault(a => !string.IsNullOrWhiteSpace(external.FolderName) &&
            string.Equals(a.FolderName?.Trim(), external.FolderName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (occupant != null) return new(LibraryAddOutcome.FolderConflict, null, local, FormatFolderConflict(external, occupant));
        var app = CloneForLocal(external, autoUpdate);
        return new(LibraryAddOutcome.Added, app, [.. local, app]);
    }

    /// <summary>
    /// The library app this entry already is: the same instance, or the same repository with the
    /// same download filter in another folder. Apps sharing a repository differ by their filter.
    /// </summary>
    public static GameInfo? FindExisting(IEnumerable<GameInfo> local, GameInfo external)
    {
        var apps = local as IList<GameInfo> ?? local.ToList();
        var exact = apps.FirstOrDefault(a => string.Equals(a.InstanceKey, external.InstanceKey, StringComparison.OrdinalIgnoreCase));
        if (exact != null || external.IsManuallyManaged) return exact;
        var filter = RepositorySourceHelper.NormalizeReleaseAssetFilter(external.ReleaseAssetFilter) ?? "";
        return apps.FirstOrDefault(a => !a.IsManuallyManaged &&
            string.Equals(a.IdentityKey, external.IdentityKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RepositorySourceHelper.NormalizeReleaseAssetFilter(a.ReleaseAssetFilter) ?? "", filter, StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatFolderConflict(GameInfo external, GameInfo occupant)
    {
        var name = AppDisplayName.Resolve(occupant.Name, occupant.Project, occupant.CustomDisplayName, LibraryNameStyle.NameAndProjectInTitle);
        if (string.IsNullOrWhiteSpace(name))
            name = occupant.FolderName ?? occupant.Repository ?? "another library app";
        return $"Folder \"{external.FolderName.Trim()}\" is already used by {name}.";
    }

    public static GameInfo CloneForLocal(GameInfo external, bool autoUpdate = false)
    {
        var manual = external.IsManuallyManaged;
        return new GameInfo
        {
            Name = external.Name,
            Project = string.IsNullOrWhiteSpace(external.Project) ? null : external.Project.Trim(),
            Repository = manual ? string.Empty : external.Repository,
            RepositorySource = manual || RepositorySourceHelper.IsGitHub(external.RepositorySource)
                ? null
                : RepositorySourceHelper.Normalize(external.RepositorySource),
            FolderName = external.FolderName,
            InstallPath = external.InstallPath,
            GameIconUrl = external.GameIconUrl,
            PreferredVersion = manual ? null : external.PreferredVersion,
            SkippedUpdateVersion = manual ? null : external.SkippedUpdateVersion,
            AutoUpdate = autoUpdate && !manual,
            Tags = TagHelper.NormalizeTags(external.Tags),
            FilesToAdd = AppFilesToAddService.Normalize(external.FilesToAdd),
            ReleaseAssetFilter = manual ? null : RepositorySourceHelper.NormalizeReleaseAssetFilter(external.ReleaseAssetFilter),
            ModsPath = GameModsConfig.NormalizePath(external.ModsPath) is { Length: > 0 } path ? path : null,
            ModsSources = GameModsConfig.NormalizeSources(external.ModsSources),
            ModsLayout = GameModsConfig.NormalizeLayout(external.ModsLayout) is { } layout && layout != GameModsConfig.LayoutFlat
                ? layout
                : null,
            IsCustom = true,
            GameManager = external.GameManager,
        };
    }

    public Task<LibraryAddResult> CommitAddAsync(GameInfo external, bool autoUpdate) => Task.Run(async () =>
    {
        // This read is inside the session queue, never a snapshot captured at click time.
        var result = PlanAdd(await manager.CatalogService.LoadLocalAppsForMutationAsync(), external, autoUpdate);
        if (result.Outcome == LibraryAddOutcome.Added)
            await manager.CatalogService.SaveLocalAppsAsync(result.Library);
        return result;
    });

    public async Task PresentAddedAsync(GameInfo app)
    {
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
            app.LoadCustomIcon(manager.CacheFolder);
            await app.LoadAndCacheDefaultIconAsync(manager.CacheFolder, allowDownload: false);
        }); }
        catch (Exception ex) { failure ??= ex; }
        await manager.InsertCatalogAppAsync(app, settings.Current);
        // Network enrichment has its own session lifetime and never holds the save
        // queue, the Add feedback, or another app's addition open.
        _ = session.RunAsync(() => Task.Run(() => EnrichAddedAsync(app, session.Token), session.Token));
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
}
