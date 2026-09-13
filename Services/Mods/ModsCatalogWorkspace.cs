using System.Diagnostics;
using System.IO;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Services.Mods.Providers.GameBanana;
using QuiverLauncher.Services.Mods.Providers.Thunderstore;

namespace QuiverLauncher.Services.Mods;

/// <summary>Owns mods browsing, search, pagination, and installed-state reconciliation.</summary>
public sealed class ModsCatalogWorkspace
{
    private readonly GameManager _gameManager;
    private readonly LauncherSession _session;
    private readonly Func<Action, Task> _dispatch;
    private readonly ModsViewModel Model;
    private CancellationTokenSource? _loadCancellation;
    private int _generation;
    public ModCatalogLoader ModCatalog { get; }
    public ModInstallService ModInstaller { get; }
    public event Action? RowsChanging;
    public event Action? RowsChanged;
    public ModsCatalogWorkspace(GameManager games, LauncherSession session, ModsViewModel model,
        Func<Action, Task> dispatch, ModCatalogLoader? catalog = null)
    {
        _gameManager = games; _session = session; Model = model; _dispatch = dispatch;
        ModCatalog = catalog ?? new ModCatalogLoader(games.ModProviderRegistry);
        ModInstaller = new ModInstallService(games.ModProviderRegistry);
    }
    public bool CanLoadMore => !_session.IsClosed && Model.Game != null && Model.UsesPagedBrowse
        && Model.BrowseSession?.CanLoadMore == true && !Model.IsLoadingMore && !Model.ListIsLoading;
    public void Cancel()
    {
        ++_generation;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        Model.SearchDebounceCts?.Cancel();
        Model.SearchDebounceCts = null;
        Model.BackgroundLoadCts?.Cancel();
        Model.BackgroundLoadCts?.Dispose();
        Model.BackgroundLoadCts = null;
        Model.IsLoadingMore = false;
        Model.ListIsLoading = false;
    }
    private bool Current(GameInfo game, int generation) => !_session.IsClosed
        && generation == _generation && ReferenceEquals(Model.Game, game);
    public void ApplyFilters()
    {
        if (_session.IsClosed) return;
        RowsChanging?.Invoke();
        Model.ApplyFilters();
        RowsChanged?.Invoke();
    }
    public Task SearchAsync() => _session.RunAsync(async () =>
    {
        Cancel();
        using var debounce = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        Model.SearchDebounceCts = debounce;
        try
        {
            await Task.Delay(300, debounce.Token);
            if (Model.Game == null || debounce.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(Model.SearchText) && Model.BrowseSession?.IsSearch != true)
                ApplyFilters();
            else if (Model.Tab == "Installed" || !ModCatalog.HasRemoteSearchSources(Model.Game.ModsSources))
                ApplyFilters();
            else await RefreshAsync(false);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(Model.SearchDebounceCts, debounce)) Model.SearchDebounceCts = null; }
    });
    public Task RefreshAsync(bool forceRefresh) => _session.RunAsync(async () =>
    {
        if (Model.Game is not { } game) return;
        Cancel();
        var generation = _generation;
        using var load = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        _loadCancellation = load;
        Model.OrphanEnrichAttempted.Clear();
        var search = !string.IsNullOrWhiteSpace(Model.SearchText) && Model.Tab != "Installed"
            && ModCatalog.HasRemoteSearchSources(game.ModsSources);
        Model.LoadingCaption = search ? "Searching…" : forceRefresh ? "Refreshing…" : "Loading mods…";
        Model.ListIsLoading = true;
        try
        {
            var options = CurrentModsListOptions();
            var paged = search || ModCatalog.HasPagedSources(game.ModsSources);
            ModBrowseSession? browse = null;
            IReadOnlyList<ModPackage> packages;
            if (search)
            {
                browse = await ModCatalog.LoadSearchSessionAsync(game.ModsSources, Model.SourceFilterKey,
                    Model.SearchText, ModCatalogLoader.DefaultPageSize, options, load.Token);
                packages = browse.Packages;
            }
            else if (paged)
            {
                browse = await ModCatalog.LoadBrowseSessionAsync(game.ModsSources, Model.SourceFilterKey,
                    forceRefresh, ModCatalogLoader.DefaultPageSize, options, load.Token);
                packages = browse.Packages;
            }
            else packages = await ModCatalog.LoadAllPackagesAsync(game.ModsSources, forceRefresh, options, load.Token);
            if (!Current(game, generation) || load.IsCancellationRequested) return;
            Model.BrowseSession = browse;
            Model.UsesPagedBrowse = paged;
            SetModsCatalog(packages);
            SyncModListItemsFromCatalog();
            ApplyFilters();
            await RefreshModUpdateFlagsForGameAsync(game);
            if (Current(game, generation)) Model.Status = FormatModsLoadedStatus();
        }
        catch (Exception) when (!Current(game, generation) || load.IsCancellationRequested) { }
        catch (Exception ex) { Model.Status = $"{(search ? "Search failed" : "Failed to load mods")}: {ex.Message}"; }
        finally
        {
            if (Current(game, generation)) Model.ListIsLoading = false;
            if (ReferenceEquals(_loadCancellation, load)) _loadCancellation = null;
        }
    });
    public Task LoadMoreAsync() => _session.RunAsync(async () =>
    {
        if (!CanLoadMore || Model.Game is not { } game || Model.BrowseSession is not { } browse) return;
        var generation = _generation;
        using var load = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        _loadCancellation = load;
        Model.IsLoadingMore = true;
        Model.Status = "Loading more…";
        try
        {
            var next = browse.IsSearch
                ? await ModCatalog.LoadMoreSearchSessionAsync(browse, ModCatalogLoader.DefaultPageSize, CurrentModsListOptions(), load.Token)
                : await ModCatalog.LoadMoreBrowseSessionAsync(browse, ModCatalogLoader.DefaultPageSize, CurrentModsListOptions(), load.Token);
            if (!Current(game, generation) || load.IsCancellationRequested) return;
            Model.BrowseSession = next;
            SetModsCatalog(next.Packages);
            SyncModListItemsFromCatalog();
            ApplyFilters();
            Model.Status = FormatModsLoadedStatus();
        }
        catch (Exception) when (!Current(game, generation) || load.IsCancellationRequested) { }
        catch (Exception ex) { Model.Status = $"Failed to load more mods: {ex.Message}"; }
        finally
        {
            if (Current(game, generation)) Model.IsLoadingMore = false;
            if (ReferenceEquals(_loadCancellation, load)) _loadCancellation = null;
        }
    });

    internal ModListOptions CurrentModsListOptions() =>
        new()
        {
            IncludeNsfw = Model.IncludeNsfw,
            SortMode = Model.SortBy,
        };


    internal string FormatModsLoadedStatus() =>
        FormatModsLoadedStatus(
            Model.Catalog.Count,
            Model.BrowseSession?.IsSearch == true,
            Model.BrowseSession?.CanLoadMore == true,
            Model.BrowseSession?.TotalCountHint);


    /// <summary>
    /// Browse/search status. When paging and a TotalCountHint is available, shows "N of M"
    /// so gaps between API totals and catalog size are visible while debugging.
    /// </summary>
    internal static string FormatModsLoadedStatus(
        int loaded,
        bool isSearch,
        bool canLoadMore,
        int? totalCountHint)
    {
        if (isSearch)
        {
            if (canLoadMore && totalCountHint is int searchTotal && searchTotal > loaded)
                return $"{loaded} of {searchTotal} search results";
            if (canLoadMore)
                return $"{loaded} search results (more available)";
            return $"{loaded} search results";
        }

        if (canLoadMore && totalCountHint is int total && total > loaded)
            return $"{loaded} of {total} mods loaded";
        if (canLoadMore)
            return $"{loaded} mods loaded (more available)";

        return $"{loaded} mods loaded";
    }


    /// <summary>
    /// True when the list-area loading panel should show (catalog load/search and no rows yet).
    /// </summary>
    internal static bool ShouldShowModsListLoading(bool isLoading, int rowCount) =>
        isLoading && rowCount == 0;


    internal void SyncModListItemsFromCatalog()
    {
        if (Model.Game == null)
            return;

        var installRoot = Model.Game.GetInstallPath(_gameManager.GamesFolder);
        var installedDoc = string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)
            ? new InstalledModsDocument()
            : ModInstaller.LoadInstalled(installRoot);

        // Remote search: API hits only (no sidecar stubs). Browse / Installed: include orphans,
        // enriched from packages seen earlier in this overlay session.
        var orphanMode = Model.BrowseSession?.IsSearch == true &&
                         !string.Equals(Model.Tab, "Installed", StringComparison.OrdinalIgnoreCase)
            ? ModOrphanInstallMode.Exclude
            : ModOrphanInstallMode.Include;

        Model.AllItems = ModCatalogListBuilder
            .BuildItems(
                Model.Catalog,
                installedDoc,
                out var idsMigrated,
                orphanMode,
                Model.KnownPackages)
            .ToList();

        // Persist Thunderstore UUID → Owner-Name Id migrations so future syncs stay Id-stable.
        if (idsMigrated && !string.IsNullOrWhiteSpace(installRoot) && Directory.Exists(installRoot))
            new InstalledModsStore().Save(installRoot, installedDoc);

        ScheduleBareOrphanEnrichment();
    }


    /// <summary>
    /// Fetches icon/description for installed cards that are still bare stubs (not on loaded pages
    /// and not in the session known cache).
    /// </summary>
    internal void ScheduleBareOrphanEnrichment()
    {
        if (Model.Game == null || Model.Game == null)
            return;

        var bare = Model.AllItems
            .Where(i => i.Status is ModInstallStatus.Installed or ModInstallStatus.UpdateAvailable)
            .Where(i => string.IsNullOrWhiteSpace(i.Package.IconUrl) &&
                        string.IsNullOrWhiteSpace(i.Package.Description))
            .Where(i => !string.IsNullOrWhiteSpace(i.Package.Owner) &&
                        !string.IsNullOrWhiteSpace(i.Package.Name))
            .Select(i => i.Package)
            .Where(p =>
            {
                var key = ModCatalogListBuilder.PackageIdentityKey(p);
                return key != null && Model.OrphanEnrichAttempted.Add(key);
            })
            .ToList();

        if (bare.Count == 0)
            return;

        // Do not cancel in-flight enrich on load-more sync — only refresh/close reset the CTS.
        if (Model.BackgroundLoadCts == null || Model.BackgroundLoadCts.IsCancellationRequested)
            Model.BackgroundLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);

        _ = _session.RunAsync(() => EnrichBareOrphansAsync(bare, Model.BackgroundLoadCts.Token));
    }


    internal async Task EnrichBareOrphansAsync(
        IReadOnlyList<ModPackage> packages,
        CancellationToken cancellationToken)
    {
        const int maxConcurrency = 3;
        using var gate = new SemaphoreSlim(maxConcurrency);
        var enrichedCount = 0;

        var tasks = packages.Select(async package =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (cancellationToken.IsCancellationRequested || Model.Game == null)
                    return;

                if (!_gameManager.ModProviderRegistry.TryGet(package.ProviderId, out var provider))
                    return;

                ModPackage enriched = package;
                if (provider is ThunderstoreModProvider thunderstore)
                {
                    enriched = await thunderstore
                        .EnrichForInstallAsync(package, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (provider is GameBananaModProvider gameBanana)
                {
                    enriched = await gameBanana
                        .EnrichWithFilesAsync(package, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(enriched.IconUrl) &&
                    string.IsNullOrWhiteSpace(enriched.Description))
                    return;

                await _dispatch(() =>
                {
                    if (Model.Game == null || cancellationToken.IsCancellationRequested)
                        return;
                    RememberModsCatalogPackages([enriched]);
                });
                Interlocked.Increment(ref enrichedCount);
            }
            catch (OperationCanceledException)
            {
                // Overlay closed or refresh started.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enrich installed mod {package.FullName}: {ex.Message}");
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
            return;
        }

        if (enrichedCount == 0 || cancellationToken.IsCancellationRequested || Model.Game == null)
            return;

        await _dispatch(() =>
        {
            if (Model.Game == null || cancellationToken.IsCancellationRequested)
                return;

            SyncModListItemsFromCatalogWithoutEnrichSchedule();
            ApplyFilters();
        });
    }


    /// <summary>Rebuilds list items without re-queuing orphan enrichment (avoids a loop after enrich).</summary>
    internal void SyncModListItemsFromCatalogWithoutEnrichSchedule()
    {
        if (Model.Game == null)
            return;

        var installRoot = Model.Game.GetInstallPath(_gameManager.GamesFolder);
        var installedDoc = string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)
            ? new InstalledModsDocument()
            : ModInstaller.LoadInstalled(installRoot);

        var orphanMode = Model.BrowseSession?.IsSearch == true &&
                         !string.Equals(Model.Tab, "Installed", StringComparison.OrdinalIgnoreCase)
            ? ModOrphanInstallMode.Exclude
            : ModOrphanInstallMode.Include;

        Model.AllItems = ModCatalogListBuilder
            .BuildItems(
                Model.Catalog,
                installedDoc,
                out var idsMigrated,
                orphanMode,
                Model.KnownPackages)
            .ToList();

        if (idsMigrated && !string.IsNullOrWhiteSpace(installRoot) && Directory.Exists(installRoot))
            new InstalledModsStore().Save(installRoot, installedDoc);
    }


    internal void RememberModsCatalogPackages(IEnumerable<ModPackage> packages)
    {
        ModCatalogListBuilder.RememberPackages(Model.KnownPackages, packages);
    }


    internal void SetModsCatalog(IReadOnlyList<ModPackage> packages)
    {
        Model.Catalog = packages.ToList();
        RememberModsCatalogPackages(Model.Catalog);
    }


    internal static IReadOnlyList<InstalledModRecord> FindInstalledRecords(
        InstalledModsDocument doc,
        ModPackage package) =>
        ModCatalogListBuilder.FindMatchingRecords(doc, package);


    /// <summary>
    /// Updates install status on every catalog row from the sidecar (root plus dependencies)
    /// without rebuilding ModListItem instances.
    /// </summary>
    internal void ApplyInstalledStateToAllItems()
    {
        if (Model.Game == null)
            return;

        var installRoot = Model.Game.GetInstallPath(_gameManager.GamesFolder);
        var installedDoc = string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)
            ? new InstalledModsDocument()
            : ModInstaller.LoadInstalled(installRoot);

        ModCatalogListBuilder.ApplyInstalledState(Model.AllItems, installedDoc);
    }


    internal void ReplaceCatalogPackage(ModPackage package)
    {
        for (var i = 0; i < Model.Catalog.Count; i++)
        {
            if (!ModCatalogListBuilder.PackagesMatch(Model.Catalog[i], package))
                continue;

            Model.Catalog[i] = package;
            RememberModsCatalogPackages([package]);
            foreach (var item in Model.AllItems)
            {
                if (!ModCatalogListBuilder.PackagesMatch(item.Package, package))
                    continue;
                item.SetKnownDownloadFiles(package.DownloadFiles);
            }
            return;
        }

        Model.Catalog.Add(package);
        RememberModsCatalogPackages([package]);
        foreach (var item in Model.AllItems)
        {
            if (!ModCatalogListBuilder.PackagesMatch(item.Package, package))
                continue;
            item.SetKnownDownloadFiles(package.DownloadFiles);
        }
    }


    internal async Task RefreshModUpdateFlagsForGameAsync(GameInfo game)
    {
        if (!game.CanOpenMods || !game.IsInstalled)
        {
            game.HasModUpdates = false;
            return;
        }

        try
        {
            var installRoot = game.GetInstallPath(_gameManager.GamesFolder);
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                game.HasModUpdates = false;
                return;
            }

            var installed = ModInstaller.LoadInstalled(installRoot);
            if (installed.Mods.Count == 0)
            {
                game.HasModUpdates = false;
                return;
            }

            // Prefer already-loaded catalog when viewing this game's overlay.
            IReadOnlyList<ModPackage> packages = ReferenceEquals(game, Model.Game) && Model.Catalog.Count > 0
                ? Model.Catalog
                : [];

            var hasUpdates = false;
            foreach (var record in installed.Mods)
            {
                var package = packages.FirstOrDefault(p =>
                    string.Equals(p.ProviderId, record.Provider, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(p.Id, record.Id, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(p.FullName, record.FullName, StringComparison.OrdinalIgnoreCase) ||
                     (string.Equals(p.Owner, record.Owner, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(p.Name, record.Name, StringComparison.OrdinalIgnoreCase))));

                package ??= new ModPackage
                {
                    ProviderId = record.Provider,
                    SourceKey = record.SourceKey,
                    Id = record.Id,
                    Owner = record.Owner,
                    Name = record.Name,
                    FullName = record.FullName,
                    LatestVersion = new ModPackageVersion { Version = record.Version, DownloadUrl = string.Empty },
                };

                if (_gameManager.ModProviderRegistry.TryGet(record.Provider, out var provider))
                {
                    if (provider is ThunderstoreModProvider thunderstore)
                        package = await thunderstore.EnrichForInstallAsync(package, _session.Token).ConfigureAwait(true);
                    else if (provider is GameBananaModProvider gameBanana)
                        package = await gameBanana.EnrichWithFilesAsync(package, _session.Token).ConfigureAwait(true);
                }

                if (ModDownloadFileSelection.IsRecordUpdateAvailable(record, package))
                {
                    hasUpdates = true;
                    break;
                }
            }

            if (!_session.IsClosed) game.HasModUpdates = hasUpdates;
        }
        catch
        {
            // Non-fatal.
        }
    }


    public async Task RefreshAllModUpdateBadgesAsync()
    {
        foreach (var game in _gameManager.Games.Where(g => g.CanOpenMods && g.IsInstalled))
            await RefreshModUpdateFlagsForGameAsync(game).ConfigureAwait(true);
    }

}