using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.Services.Mods.Providers.GameBanana;
using QuiverLauncher.Services.Mods.Providers.Thunderstore;
using System.Diagnostics;
using System.IO;

namespace QuiverLauncher;

public partial class MainWindow
{
    private bool _isModsOverlayOpen;
    private bool _isModDetailsOpen;
    private GameInfo? _modsGame;
    private ModListItem? _modDetailsItem;
    private string _modDetailsTab = "Details";
    private string? _modDetailsReadmeMarkdown;
    private string? _modDetailsChangelogMarkdown;
    private bool _modDetailsReadmeLoaded;
    private bool _modDetailsChangelogLoaded;
    private int _modDetailsGamepadFocusIndex = -1;
    private List<ModPackage> _modsCatalog = [];
    private readonly Dictionary<string, ModPackage> _modsKnownPackages =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _modsOrphanEnrichAttempted =
        new(StringComparer.OrdinalIgnoreCase);
    private List<ModListItem> _modsAllItems = [];
    private string _modsTab = "Browse";
    private string? _modsSourceFilterKey; // null = All
    private string _modsSearchText = string.Empty;
    private string _modsSortBy = ModListSorter.InstalledFirst;
    private bool _modsIncludeNsfw;
    private ModBrowseSession? _modsBrowseSession;
    private bool _modsUsesPagedBrowse;
    private bool _modsIsLoadingMore;
    private CancellationTokenSource? _modsBackgroundLoadCts;
    private CancellationTokenSource? _modsSearchDebounceCts;
    private ModInstallService? _modInstallService;
    private ModCatalogLoader? _modCatalogLoader;
    private int _modsGamepadToolbarIndex = -1;
    private int _modsGamepadFilterIndex = -1;
    private int _modsGamepadSourceFilterIndex = -1;
    private int _modsGamepadListIndex = -1;
    private int _modsGamepadRowActionIndex = -1;

    private ModInstallService ModInstaller =>
        _modInstallService ??= new ModInstallService(_gameManager.ModProviderRegistry);

    private ModCatalogLoader ModCatalog =>
        _modCatalogLoader ??= new ModCatalogLoader(_gameManager.ModProviderRegistry);

    private void OpenMods_Click(object? sender, RoutedEventArgs e)
    {
        var game = (sender as MenuItem)?.CommandParameter as GameInfo
                   ?? (sender as Control)?.DataContext as GameInfo;
        if (game == null || !game.CanOpenMods)
            return;

        _ = OpenModsOverlayAsync(game);
    }

    private async Task OpenModsOverlayAsync(GameInfo game)
    {
        _modsGame = game;
        _isModsOverlayOpen = true;
        _isAppUpdatesReviewOpen = false;
        _mainViewMode = MainViewMode.Library;
        _modsTab = "Browse";
        _modsSourceFilterKey = null;
        _modsSearchText = string.Empty;
        _modsBrowseSession = null;
        _modsUsesPagedBrowse = false;
        _modsCatalog = [];
        _modsKnownPackages.Clear();
        _modsOrphanEnrichAttempted.Clear();
        _modsIncludeNsfw = _settings.ModsIncludeNsfw;
        if (ModsSearchTextBox != null)
            ModsSearchTextBox.Text = string.Empty;

        ApplyModsSortSelection(_settings.ModsSortBy);
        UpdateModsTabButtons();
        BuildModsSourceFilterButtons();
        ResetGamepadNavigationIndices();
        UpdateMainViewUi();

        if (ModsHeaderText != null)
            ModsHeaderText.Text = $"Mods — {game.Name}";

        SetModsStatus("Loading mods…");
        await RefreshModsCatalogAsync(forceRefresh: false).ConfigureAwait(true);

        if (IsGamepadFocusActive)
            SelectInitialModsGamepadItem();
        else
            ClearGamepadFocus();
    }

    private void CloseModsOverlay()
    {
        if (_isModDetailsOpen)
            CloseModDetails();

        _isModsOverlayOpen = false;
        _modsGame = null;
        _modsCatalog = [];
        _modsKnownPackages.Clear();
        _modsOrphanEnrichAttempted.Clear();
        _modsAllItems = [];
        _modsBrowseSession = null;
        _modsUsesPagedBrowse = false;
        _modsSearchDebounceCts?.Cancel();
        _modsSearchDebounceCts = null;
        _modsBackgroundLoadCts?.Cancel();
        _modsBackgroundLoadCts = null;
        ModListRows.Clear();
        ClearModsGamepadFocus();
        UpdateMainViewUi();

        if (_gamepadNavigation.ActiveZone is GamepadNavigationZone.ModsOverlayToolbar
            or GamepadNavigationZone.ModsOverlayFilters
            or GamepadNavigationZone.ModsOverlaySourceFilters
            or GamepadNavigationZone.ModsOverlayList
            or GamepadNavigationZone.ModsOverlayRowActions)
        {
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
            if (IsGamepadFocusActive)
                SelectInitialLibraryGamepadItem();
        }

        OnPropertyChanged(nameof(GamepadHintsVisible));
    }

    private void ModsClose_Click(object? sender, RoutedEventArgs e) => CloseModsOverlay();

    private async void ModsRefresh_Click(object? sender, RoutedEventArgs e) =>
        await RefreshModsCatalogAsync(forceRefresh: true);

    private async void ModsUpdateAll_Click(object? sender, RoutedEventArgs e) =>
        await UpdateAllVisibleModsAsync();

    private void ModsOpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_modsGame == null)
            return;

        try
        {
            var installRoot = _modsGame.GetInstallPath(_gameManager.GamesFolder);
            var modsPath = GameModsConfig.NormalizePath(_modsGame.ModsPath);
            if (string.IsNullOrWhiteSpace(installRoot) || modsPath.Length == 0)
            {
                _ = ShowMessageBoxAsync("Mods folder is not configured for this app.", "Mods");
                return;
            }

            var modsDir = ModInstaller.GetModsDirectory(installRoot, modsPath);
            Directory.CreateDirectory(modsDir);
            OpenUrl(modsDir);
        }
        catch (Exception ex)
        {
            _ = ShowMessageBoxAsync($"Failed to open mod folder: {ex.Message}", "Mods");
        }
    }

    private void ModsTabFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tab)
            return;

        _modsTab = tab;
        UpdateModsTabButtons();
        // Orphan install inclusion depends on Browse+search vs Installed — rebuild rows.
        SyncModListItemsFromCatalog();
        ApplyModsFiltersToUi();
    }

    private void ModsSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _modsSearchText = ModsSearchTextBox?.Text?.Trim() ?? string.Empty;
        _ = DebouncedModsSearchAsync();
    }

    private async Task DebouncedModsSearchAsync()
    {
        _modsSearchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _modsSearchDebounceCts = cts;

        try
        {
            await Task.Delay(300, cts.Token).ConfigureAwait(true);
            if (_modsGame == null || cts.IsCancellationRequested)
                return;

            if (string.IsNullOrWhiteSpace(_modsSearchText))
            {
                if (_modsBrowseSession?.IsSearch == true)
                    await RefreshModsCatalogAsync(forceRefresh: false).ConfigureAwait(true);
                else
                    ApplyModsFiltersToUi();
                return;
            }

            if (string.Equals(_modsTab, "Installed", StringComparison.OrdinalIgnoreCase) ||
                !ModCatalog.HasRemoteSearchSources(_modsGame.ModsSources))
            {
                ApplyModsFiltersToUi();
                return;
            }

            await RunRemoteModsSearchAsync(_modsSearchText, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Newer search keystroke or overlay closed.
        }
    }

    private async Task RunRemoteModsSearchAsync(string query, CancellationToken cancellationToken)
    {
        if (_modsGame == null)
            return;

        SetModsStatus("Searching…");
        try
        {
            var options = CurrentModsListOptions();
            _modsBrowseSession = await ModCatalog
                .LoadSearchSessionAsync(
                    _modsGame.ModsSources,
                    _modsSourceFilterKey,
                    query,
                    ModCatalogLoader.DefaultPageSize,
                    options,
                    cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
                return;

            _modsUsesPagedBrowse = true;
            SetModsCatalog(_modsBrowseSession.Packages);
            SyncModListItemsFromCatalog();
            ApplyModsFiltersToUi();
            SetModsStatus(FormatModsLoadedStatus());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetModsStatus($"Search failed: {ex.Message}");
        }
    }

    private ModListOptions CurrentModsListOptions() =>
        new()
        {
            IncludeNsfw = _modsIncludeNsfw,
            SortMode = _modsSortBy,
        };

    private async void ModsSortByComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModsSortByComboBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string sortMode)
            return;

        var normalized = ModListSorter.Normalize(sortMode);
        if (string.Equals(_modsSortBy, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _modsSortBy = normalized;
        _settings.ModsSortBy = normalized;
        AppSettings.Save(_settings);

        if (ModListSorter.IsRemoteSort(normalized))
            await RefreshModsCatalogAsync(forceRefresh: false).ConfigureAwait(true);
        else
            ApplyModsFiltersToUi();
    }

    private void ApplyModsSortSelection(string? sortBy)
    {
        _modsSortBy = ModListSorter.Normalize(sortBy);
        _settings.ModsSortBy = _modsSortBy;

        if (ModsSortByComboBox == null)
            return;

        foreach (var entry in ModsSortByComboBox.Items)
        {
            if (entry is ComboBoxItem item &&
                item.Tag is string tag &&
                string.Equals(tag, _modsSortBy, StringComparison.OrdinalIgnoreCase))
            {
                ModsSortByComboBox.SelectedItem = item;
                return;
            }
        }

        if (ModsSortByComboBox.Items.Count > 0)
            ModsSortByComboBox.SelectedIndex = 0;
    }

    private async void ModsSourceFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var tag = button.Tag as string;
        if (tag is "nsfw")
        {
            _modsIncludeNsfw = !_modsIncludeNsfw;
            _settings.ModsIncludeNsfw = _modsIncludeNsfw;
            AppSettings.Save(_settings);
            UpdateModsSourceFilterButtons();
            await RefreshModsCatalogAsync(forceRefresh: false).ConfigureAwait(true);
            return;
        }

        _modsSourceFilterKey = tag; // null tag = All
        if (tag is { Length: 0 })
            _modsSourceFilterKey = null;

        UpdateModsSourceFilterButtons();
        // Source filter changes reset GameBanana paging.
        await RefreshModsCatalogAsync(forceRefresh: false).ConfigureAwait(true);
    }

    private async void ModsListScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!CanStartLoadMoreMods() || !IsModsListNearBottomForPrefetch())
            return;

        await LoadMoreModsAsync().ConfigureAwait(true);
    }

    private bool CanStartLoadMoreMods() =>
        _modsGame != null &&
        _modsUsesPagedBrowse &&
        _modsBrowseSession?.CanLoadMore == true &&
        !_modsIsLoadingMore;

    /// <summary>
    /// True when the mods list is scrolled near the bottom, or content does not overflow the
    /// viewport (so mouse users can still page when the last incomplete row fills the view).
    /// </summary>
    private bool IsModsListNearBottomForPrefetch()
    {
        if (ModsListScrollViewer == null)
            return true;

        var extent = ModsListScrollViewer.Extent.Height;
        var viewport = ModsListScrollViewer.Viewport.Height;
        var offset = ModsListScrollViewer.Offset.Y;

        if (extent <= viewport)
            return true;

        return offset + viewport >= extent - 120;
    }

    private bool ShouldPrefetchMoreModsForGamepad(int focusedIndex) =>
        CanStartLoadMoreMods() &&
        ModListRows.Count > 0 &&
        (focusedIndex >= Math.Max(0, ModListRows.Count - 6) || IsModsListNearBottomForPrefetch());

    private async void ModRowInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (GetModListItem(sender) is not { } item)
            return;
        await InstallModAsync(item, updateInstalledFilesOnly: false);
    }

    private async void ModRowUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (GetModListItem(sender) is not { } item)
            return;
        await InstallModAsync(item, updateInstalledFilesOnly: true);
    }

    private async void ModRowUninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (GetModListItem(sender) is not { } item)
            return;
        await UninstallModAsync(item);
    }

    private void ModRowOpenPage_Click(object? sender, RoutedEventArgs e)
    {
        if (GetModListItem(sender) is not { } item)
            return;

        var url = item.Package.PackagePageUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowMessageBoxAsync($"Could not open page: {ex.Message}", "Mods");
        }
    }

    private static ModListItem? GetModListItem(object? sender)
    {
        if (sender is Button button && button.CommandParameter is ModListItem fromCommand)
            return fromCommand;
        if (sender is MenuItem menuItem && menuItem.CommandParameter is ModListItem fromMenu)
            return fromMenu;
        if (sender is Control control && control.DataContext is ModListItem fromContext)
            return fromContext;
        return null;
    }

    private async Task RefreshModsCatalogAsync(bool forceRefresh)
    {
        if (_modsGame == null)
            return;

        try
        {
            _modsBackgroundLoadCts?.Cancel();
            _modsBackgroundLoadCts = null;
            _modsOrphanEnrichAttempted.Clear();
            SetModsStatus(forceRefresh ? "Refreshing…" : "Loading mods…");

            var options = CurrentModsListOptions();
            _modsUsesPagedBrowse = ModCatalog.HasPagedSources(_modsGame.ModsSources);

            // If the user has an active search query, re-run remote search instead of browse.
            if (!string.IsNullOrWhiteSpace(_modsSearchText) &&
                !string.Equals(_modsTab, "Installed", StringComparison.OrdinalIgnoreCase) &&
                ModCatalog.HasRemoteSearchSources(_modsGame.ModsSources))
            {
                await RunRemoteModsSearchAsync(_modsSearchText, CancellationToken.None).ConfigureAwait(true);
                await RefreshModUpdateFlagsForGameAsync(_modsGame).ConfigureAwait(true);
                return;
            }

            if (_modsUsesPagedBrowse)
            {
                _modsBrowseSession = await ModCatalog
                    .LoadBrowseSessionAsync(
                        _modsGame.ModsSources,
                        _modsSourceFilterKey,
                        forceRefresh,
                        ModCatalogLoader.DefaultPageSize,
                        options)
                    .ConfigureAwait(true);
                SetModsCatalog(_modsBrowseSession.Packages);
            }
            else
            {
                _modsBrowseSession = null;
                SetModsCatalog(await ModCatalog
                    .LoadAllPackagesAsync(_modsGame.ModsSources, forceRefresh, options)
                    .ConfigureAwait(true));
            }

            SyncModListItemsFromCatalog();
            ApplyModsFiltersToUi();
            await RefreshModUpdateFlagsForGameAsync(_modsGame).ConfigureAwait(true);
            SetModsStatus(FormatModsLoadedStatus());
        }
        catch (Exception ex)
        {
            SetModsStatus($"Failed to load mods: {ex.Message}");
        }
    }

    private async Task LoadMoreModsAsync()
    {
        if (!CanStartLoadMoreMods())
            return;

        await LoadMoreModsPageAsync().ConfigureAwait(true);

        // After each page, keep fetching while still parked at the end (or content still fits).
        // ScrollChanged during _modsIsLoadingMore is ignored, so without this recheck the last
        // incomplete row can stall with CanLoadMore still true.
        while (CanStartLoadMoreMods() && IsModsListNearBottomForPrefetch())
            await LoadMoreModsPageAsync().ConfigureAwait(true);
    }

    private async Task LoadMoreModsPageAsync()
    {
        if (!CanStartLoadMoreMods() || _modsBrowseSession == null || _modsGame == null)
            return;

        _modsIsLoadingMore = true;
        SetModsStatus("Loading more…");
        try
        {
            var options = CurrentModsListOptions();
            _modsBrowseSession = _modsBrowseSession.IsSearch
                ? await ModCatalog
                    .LoadMoreSearchSessionAsync(_modsBrowseSession, ModCatalogLoader.DefaultPageSize, options)
                    .ConfigureAwait(true)
                : await ModCatalog
                    .LoadMoreBrowseSessionAsync(_modsBrowseSession, ModCatalogLoader.DefaultPageSize, options)
                    .ConfigureAwait(true);
            SetModsCatalog(_modsBrowseSession.Packages);
            SyncModListItemsFromCatalog();
            ApplyModsFiltersToUi();
            SetModsStatus(FormatModsLoadedStatus());
        }
        catch (Exception ex)
        {
            SetModsStatus($"Failed to load more mods: {ex.Message}");
        }
        finally
        {
            _modsIsLoadingMore = false;
        }
    }

    private string FormatModsLoadedStatus() =>
        FormatModsLoadedStatus(
            _modsCatalog.Count,
            _modsBrowseSession?.IsSearch == true,
            _modsBrowseSession?.CanLoadMore == true,
            _modsBrowseSession?.TotalCountHint);

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

    private void SyncModListItemsFromCatalog()
    {
        if (_modsGame == null)
            return;

        var installRoot = _modsGame.GetInstallPath(_gameManager.GamesFolder);
        var installedDoc = string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)
            ? new InstalledModsDocument()
            : ModInstaller.LoadInstalled(installRoot);

        // Remote search: API hits only (no sidecar stubs). Browse / Installed: include orphans,
        // enriched from packages seen earlier in this overlay session.
        var orphanMode = _modsBrowseSession?.IsSearch == true &&
                         !string.Equals(_modsTab, "Installed", StringComparison.OrdinalIgnoreCase)
            ? ModOrphanInstallMode.Exclude
            : ModOrphanInstallMode.Include;

        _modsAllItems = ModCatalogListBuilder
            .BuildItems(
                _modsCatalog,
                installedDoc,
                out var idsMigrated,
                orphanMode,
                _modsKnownPackages)
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
    private void ScheduleBareOrphanEnrichment()
    {
        if (!_isModsOverlayOpen || _modsGame == null)
            return;

        var bare = _modsAllItems
            .Where(i => i.Status is ModInstallStatus.Installed or ModInstallStatus.UpdateAvailable)
            .Where(i => string.IsNullOrWhiteSpace(i.Package.IconUrl) &&
                        string.IsNullOrWhiteSpace(i.Package.Description))
            .Where(i => !string.IsNullOrWhiteSpace(i.Package.Owner) &&
                        !string.IsNullOrWhiteSpace(i.Package.Name))
            .Select(i => i.Package)
            .Where(p =>
            {
                var key = ModCatalogListBuilder.PackageIdentityKey(p);
                return key != null && _modsOrphanEnrichAttempted.Add(key);
            })
            .ToList();

        if (bare.Count == 0)
            return;

        // Do not cancel in-flight enrich on load-more sync — only refresh/close reset the CTS.
        if (_modsBackgroundLoadCts == null || _modsBackgroundLoadCts.IsCancellationRequested)
            _modsBackgroundLoadCts = new CancellationTokenSource();

        _ = EnrichBareOrphansAsync(bare, _modsBackgroundLoadCts.Token);
    }

    private async Task EnrichBareOrphansAsync(
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
                if (cancellationToken.IsCancellationRequested || !_isModsOverlayOpen)
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

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_isModsOverlayOpen || cancellationToken.IsCancellationRequested)
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

        if (enrichedCount == 0 || cancellationToken.IsCancellationRequested || !_isModsOverlayOpen)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isModsOverlayOpen || cancellationToken.IsCancellationRequested)
                return;

            SyncModListItemsFromCatalogWithoutEnrichSchedule();
            ApplyModsFiltersToUi();
        });
    }

    /// <summary>Rebuilds list items without re-queuing orphan enrichment (avoids a loop after enrich).</summary>
    private void SyncModListItemsFromCatalogWithoutEnrichSchedule()
    {
        if (_modsGame == null)
            return;

        var installRoot = _modsGame.GetInstallPath(_gameManager.GamesFolder);
        var installedDoc = string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)
            ? new InstalledModsDocument()
            : ModInstaller.LoadInstalled(installRoot);

        var orphanMode = _modsBrowseSession?.IsSearch == true &&
                         !string.Equals(_modsTab, "Installed", StringComparison.OrdinalIgnoreCase)
            ? ModOrphanInstallMode.Exclude
            : ModOrphanInstallMode.Include;

        _modsAllItems = ModCatalogListBuilder
            .BuildItems(
                _modsCatalog,
                installedDoc,
                out var idsMigrated,
                orphanMode,
                _modsKnownPackages)
            .ToList();

        if (idsMigrated && !string.IsNullOrWhiteSpace(installRoot) && Directory.Exists(installRoot))
            new InstalledModsStore().Save(installRoot, installedDoc);
    }

    private void RememberModsCatalogPackages(IEnumerable<ModPackage> packages)
    {
        ModCatalogListBuilder.RememberPackages(_modsKnownPackages, packages);
    }

    private void SetModsCatalog(IReadOnlyList<ModPackage> packages)
    {
        _modsCatalog = packages.ToList();
        RememberModsCatalogPackages(_modsCatalog);
    }

    private static IReadOnlyList<InstalledModRecord> FindInstalledRecords(
        InstalledModsDocument doc,
        ModPackage package) =>
        ModCatalogListBuilder.FindMatchingRecords(doc, package);

    private void ApplyModsFiltersToUi()
    {
        var restoreFocus = IsGamepadFocusActive &&
            _gamepadNavigation.ActiveZone is GamepadNavigationZone.ModsOverlayList
                or GamepadNavigationZone.ModsOverlayRowActions;
        ModPackage? focusedPackage = null;
        var fallbackIndex = _modsGamepadListIndex;
        if (restoreFocus)
        {
            var focusedRow = ModListRows.FirstOrDefault(r => r.IsGamepadFocused);
            if (focusedRow != null)
                focusedPackage = focusedRow.Package;
            else if (fallbackIndex >= 0 && fallbackIndex < ModListRows.Count)
                focusedPackage = ModListRows[fallbackIndex].Package;
        }

        IEnumerable<ModListItem> query = _modsAllItems;

        // Remote search already applied NSFW via the API where supported; still hide NSFW client-side
        // for GameBanana and for any residual listing rows.
        if (!_modsIncludeNsfw)
            query = query.Where(i => !i.Package.HasContentRating);

        if (!string.IsNullOrEmpty(_modsSourceFilterKey) && _modsBrowseSession?.IsSearch != true)
            query = query.Where(i =>
                string.Equals($"{i.ProviderId}|{i.SourceKey}", _modsSourceFilterKey, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(_modsTab, "Installed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(i => i.Status is ModInstallStatus.Installed or ModInstallStatus.UpdateAvailable);

        // Local text filter only when not showing a remote search result set (or on Installed).
        var useLocalSearchFilter = !string.IsNullOrWhiteSpace(_modsSearchText) &&
                                   (_modsBrowseSession?.IsSearch != true ||
                                    string.Equals(_modsTab, "Installed", StringComparison.OrdinalIgnoreCase));
        if (useLocalSearchFilter)
        {
            var term = _modsSearchText;
            query = query.Where(i =>
                i.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Owner.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Package.FullName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var rows = ModListSorter.Sort(query, _modsSortBy);
        ModListRows.Clear();
        foreach (var row in rows)
            ModListRows.Add(row);

        if (ModsEmptyText != null)
            ModsEmptyText.IsVisible = rows.Count == 0;

        if (ModsUpdateAllButton != null)
            ModsUpdateAllButton.IsEnabled = rows.Any(r => r.CanUpdate);

        if (!restoreFocus || ModListRows.Count == 0)
            return;

        var index = ModCatalogListBuilder.FindListIndexByPackage(ModListRows, focusedPackage, fallbackIndex);
        if (index >= 0)
            ApplyModsListSelection(index);
    }

    /// <summary>
    /// Updates install status on existing list rows without rebuilding ModListItem instances
    /// (avoids focus loss and AdvancedImage rebinds after install/uninstall).
    /// </summary>
    private void ApplyInstalledStateToMatchingItems(ModPackage package)
    {
        if (_modsGame == null)
            return;

        var installRoot = _modsGame.GetInstallPath(_gameManager.GamesFolder);
        var installedDoc = string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)
            ? new InstalledModsDocument()
            : ModInstaller.LoadInstalled(installRoot);
        var records = FindInstalledRecords(installedDoc, package);

        foreach (var existing in _modsAllItems)
        {
            if (ModCatalogListBuilder.PackagesMatch(existing.Package, package))
                existing.ApplyInstalled(records);
        }
    }

    private void BuildModsSourceFilterButtons()
    {
        if (ModsSourceFilterPanel == null || _modsGame == null)
            return;

        ModsSourceFilterPanel.Children.Clear();

        var allButton = CreateModsFilterButton("All", tag: "");
        allButton.Classes.Set("selected", _modsSourceFilterKey == null);
        ModsSourceFilterPanel.Children.Add(allButton);

        var resolved = ModCatalog.ResolveSources(_modsGame.ModsSources);
        foreach (var (_, parsed, _) in resolved)
        {
            var tag = $"{parsed.ProviderId}|{parsed.SourceKey}";
            var button = CreateModsFilterButton(parsed.DisplayLabel, tag);
            button.Classes.Set("selected",
                string.Equals(_modsSourceFilterKey, tag, StringComparison.OrdinalIgnoreCase));
            ModsSourceFilterPanel.Children.Add(button);
        }

        var nsfwButton = CreateModsFilterButton("Include NSFW", tag: "nsfw");
        nsfwButton.Classes.Set("selected", _modsIncludeNsfw);
        ModsSourceFilterPanel.Children.Add(nsfwButton);
    }

    private Button CreateModsFilterButton(string content, string tag)
    {
        var button = new Button
        {
            Classes = { "catalog-filter" },
            Content = content,
            Tag = tag,
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 6),
        };
        button.Click += ModsSourceFilter_Click;
        return button;
    }

    private void UpdateModsSourceFilterButtons()
    {
        if (ModsSourceFilterPanel == null)
            return;

        foreach (var child in ModsSourceFilterPanel.Children.OfType<Button>())
        {
            var tag = child.Tag as string ?? "";
            bool selected;
            if (tag == "nsfw")
                selected = _modsIncludeNsfw;
            else if (string.IsNullOrEmpty(tag))
                selected = _modsSourceFilterKey == null;
            else
                selected = string.Equals(_modsSourceFilterKey, tag, StringComparison.OrdinalIgnoreCase);
            child.Classes.Set("selected", selected);
        }
    }

    private void UpdateModsTabButtons()
    {
        ModsTabBrowseButton?.Classes.Set("selected", _modsTab == "Browse");
        ModsTabInstalledButton?.Classes.Set("selected", _modsTab == "Installed");
    }

    private void SetModsStatus(string text)
    {
        if (ModsStatusText != null)
            ModsStatusText.Text = text;
    }

    private async Task InstallModAsync(ModListItem item, bool updateInstalledFilesOnly = false)
    {
        if (_modsGame == null)
            return;

        if (!_modsGame.IsInstalled)
        {
            await ShowMessageBoxAsync("Install the app before installing mods.", "Mods");
            return;
        }

        var installRoot = _modsGame.GetInstallPath(_gameManager.GamesFolder);
        var modsPath = GameModsConfig.NormalizePath(_modsGame.ModsPath);
        if (string.IsNullOrWhiteSpace(installRoot) || modsPath.Length == 0)
            return;

        if (!_gameManager.ModProviderRegistry.TryGet(item.ProviderId, out var provider))
        {
            await ShowMessageBoxAsync($"Unknown mod provider '{item.ProviderId}'.", "Mods");
            return;
        }

        item.IsBusy = true;
        var isUpdate = updateInstalledFilesOnly;
        var actionLabel = isUpdate ? "Updating" : "Installing";
        SetModsStatus($"{actionLabel} {item.DisplayName}…");
        try
        {
            var package = item.Package;
            IReadOnlyList<ModDownloadFile>? selectedFiles = null;

            if (provider is GameBananaModProvider gameBanana)
            {
                package = await gameBanana.EnrichWithFilesAsync(package).ConfigureAwait(true);
                ReplaceCatalogPackage(package);

                if (package.DownloadFiles.Count == 0)
                    throw new InvalidOperationException("This mod has no downloadable files.");

                var installedRecords = FindInstalledRecords(ModInstaller.LoadInstalled(installRoot), package);

                if (updateInstalledFilesOnly)
                {
                    selectedFiles = ModDownloadFileSelection.ResolveFilesToUpdate(package, installedRecords);
                    if (selectedFiles.Count == 0)
                    {
                        SetModsStatus("Nothing to update");
                        return;
                    }
                }
                else
                {
                    var remaining = ModDownloadFileSelection.GetUninstalledFiles(
                        package.DownloadFiles,
                        installedRecords);
                    if (remaining.Count == 0)
                    {
                        SetModsStatus("All files already installed");
                        return;
                    }

                    if (package.DownloadFiles.Count == 1)
                    {
                        selectedFiles = remaining;
                    }
                    else
                    {
                        selectedFiles = await ShowModDownloadFilePickerAsync(
                            $"Choose files — {package.Name}",
                            "This mod has multiple download files. Select one or more to install:",
                            "Install",
                            remaining,
                            preselectedFileIds: null).ConfigureAwait(true);
                        if (selectedFiles.Count == 0)
                        {
                            SetModsStatus("Install cancelled");
                            return;
                        }
                    }
                }
            }
            else if (provider is ThunderstoreModProvider thunderstore)
            {
                package = await thunderstore.EnrichForInstallAsync(package).ConfigureAwait(true);
                ReplaceCatalogPackage(package);
                if (string.IsNullOrWhiteSpace(package.LatestVersion?.DownloadUrl))
                    throw new InvalidOperationException("Could not resolve a download URL for this mod.");
            }

            var progress = new Progress<double>(p => item.DownloadProgress = p * 100.0);

            await ModInstaller.InstallSelectedFilesAsync(
                installRoot,
                modsPath,
                package,
                _modsCatalog,
                provider,
                selectedFiles,
                progress,
                modsLayout: _modsGame.ModsLayout).ConfigureAwait(true);

            ApplyInstalledStateToMatchingItems(package);
            ApplyModsFiltersToUi();
            await RefreshModUpdateFlagsForGameAsync(_modsGame).ConfigureAwait(true);
            SetModsStatus(isUpdate ? $"Updated {item.DisplayName}" : $"Installed {item.DisplayName}");
        }
        catch (Exception ex)
        {
            SetModsStatus($"{actionLabel} failed: {ex.Message}");
            await ShowMessageBoxAsync($"Failed to install mod:\n{ex.Message}", "Mods");
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private void ReplaceCatalogPackage(ModPackage package)
    {
        for (var i = 0; i < _modsCatalog.Count; i++)
        {
            if (!ModCatalogListBuilder.PackagesMatch(_modsCatalog[i], package))
                continue;

            _modsCatalog[i] = package;
            RememberModsCatalogPackages([package]);
            foreach (var item in _modsAllItems)
            {
                if (!ModCatalogListBuilder.PackagesMatch(item.Package, package))
                    continue;
                item.SetKnownDownloadFiles(package.DownloadFiles);
            }
            return;
        }

        _modsCatalog.Add(package);
        RememberModsCatalogPackages([package]);
        foreach (var item in _modsAllItems)
        {
            if (!ModCatalogListBuilder.PackagesMatch(item.Package, package))
                continue;
            item.SetKnownDownloadFiles(package.DownloadFiles);
        }
    }

    private async Task<IReadOnlyList<ModDownloadFile>> ShowModDownloadFilePickerAsync(
        string title,
        string prompt,
        string confirmLabel,
        IReadOnlyList<ModDownloadFile> files,
        IReadOnlyCollection<string>? preselectedFileIds)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow == null)
                return (IReadOnlyList<ModDownloadFile>)[];

            IReadOnlyList<ModDownloadFile> chosen = [];
            var listBox = new ListBox
            {
                MinHeight = 200,
                MaxHeight = 360,
                Focusable = true,
                SelectionMode = SelectionMode.Multiple,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };

            var checkBoxes = new List<CheckBox>();
            foreach (var file in files)
            {
                var sizeMb = file.FileSize > 0 ? $"{file.FileSize / (1024d * 1024d):0.##} MB" : "Unknown size";
                var desc = string.IsNullOrWhiteSpace(file.Description) ? "" : $" — {file.Description}";
                var isPreselected = preselectedFileIds != null &&
                    preselectedFileIds.Contains(file.Id, StringComparer.OrdinalIgnoreCase);
                var checkBox = new CheckBox
                {
                    Content = new TextBlock
                    {
                        Text = $"{file.FileName} ({sizeMb}){desc}",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    IsChecked = isPreselected,
                    Tag = file,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };
                checkBoxes.Add(checkBox);

                var item = new ListBoxItem
                {
                    Content = checkBox,
                    Tag = file,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };
                listBox.Items.Add(item);
                if (isPreselected)
                    listBox.SelectedItems?.Add(item);
            }

            if (listBox.SelectedItem == null && listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;

            var confirmButton = new Button
            {
                Content = confirmLabel,
                MinWidth = 100,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
            };
            var cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 100,
                IsCancel = true,
            };

            void RefreshConfirmEnabled() =>
                confirmButton.IsEnabled = checkBoxes.Any(c => c.IsChecked == true);

            foreach (var checkBox in checkBoxes)
                checkBox.IsCheckedChanged += (_, _) => RefreshConfirmEnabled();
            RefreshConfirmEnabled();

            var dialog = new Window
            {
                Title = title,
                Width = 720,
                MinWidth = 560,
                Height = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = prompt,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        listBox,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children = { confirmButton, cancelButton },
                        },
                    },
                },
            };

            confirmButton.Click += (_, _) =>
            {
                chosen = checkBoxes
                    .Where(c => c.IsChecked == true && c.Tag is ModDownloadFile)
                    .Select(c => (ModDownloadFile)c.Tag!)
                    .ToList();
                dialog.Close();
            };
            cancelButton.Click += (_, _) => dialog.Close();

            GamepadModalDialogNavigation.Attach(dialog);
            await dialog.ShowDialog(desktop.MainWindow);
            return chosen;
        });
    }

    private async Task UninstallModAsync(ModListItem item)
    {
        if (_modsGame == null || !_modsGame.IsInstalled)
            return;

        var installRoot = _modsGame.GetInstallPath(_gameManager.GamesFolder);
        var modsPath = GameModsConfig.NormalizePath(_modsGame.ModsPath);
        if (string.IsNullOrWhiteSpace(installRoot) || modsPath.Length == 0)
            return;

        var package = item.Package;
        var installedRecords = FindInstalledRecords(ModInstaller.LoadInstalled(installRoot), package);
        if (installedRecords.Count == 0)
            return;

        IReadOnlyList<InstalledModRecord> toRemove = installedRecords;
        if (package.DownloadFiles.Count == 0 &&
            _gameManager.ModProviderRegistry.TryGet(item.ProviderId, out var provider) &&
            provider is GameBananaModProvider gameBanana)
        {
            package = await gameBanana.EnrichWithFilesAsync(package).ConfigureAwait(true);
            ReplaceCatalogPackage(package);
            installedRecords = FindInstalledRecords(ModInstaller.LoadInstalled(installRoot), package);
            toRemove = installedRecords;
        }

        if (package.DownloadFiles.Count > 1)
        {
            var pickerFiles = ModDownloadFileSelection.ResolveInstalledFilesForPicker(package, installedRecords);
            var selected = await ShowModDownloadFilePickerAsync(
                $"Uninstall files — {package.Name}",
                "This mod has multiple download files. Select one or more to uninstall:",
                "Uninstall",
                pickerFiles,
                preselectedFileIds: null).ConfigureAwait(true);
            if (selected.Count == 0)
            {
                SetModsStatus("Uninstall cancelled");
                return;
            }

            var selectedIds = selected.Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            toRemove = installedRecords.Where(r =>
                selectedIds.Contains(r.DownloadFileId ?? string.Empty) ||
                selected.Any(f =>
                    !string.IsNullOrWhiteSpace(r.DownloadFileName) &&
                    string.Equals(f.FileName, r.DownloadFileName, StringComparison.OrdinalIgnoreCase))).ToList();
            if (toRemove.Count == 0)
            {
                SetModsStatus("Uninstall cancelled");
                return;
            }
        }

        item.IsBusy = true;
        try
        {
            foreach (var record in toRemove)
                ModInstaller.UninstallMatchingFile(installRoot, modsPath, package, record.DownloadFileId);

            ApplyInstalledStateToMatchingItems(package);
            ApplyModsFiltersToUi();
            _ = RefreshModUpdateFlagsForGameAsync(_modsGame);
            SetModsStatus($"Uninstalled {item.DisplayName}");
        }
        catch (Exception ex)
        {
            SetModsStatus($"Uninstall failed: {ex.Message}");
            _ = ShowMessageBoxAsync($"Failed to uninstall mod:\n{ex.Message}", "Mods");
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task UpdateAllVisibleModsAsync()
    {
        var toUpdate = ModListRows.Where(r => r.CanUpdate).ToList();
        foreach (var item in toUpdate)
            await InstallModAsync(item, updateInstalledFilesOnly: true);
    }

    private async Task RefreshModUpdateFlagsForGameAsync(GameInfo game)
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
            IReadOnlyList<ModPackage> packages = ReferenceEquals(game, _modsGame) && _modsCatalog.Count > 0
                ? _modsCatalog
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
                        package = await thunderstore.EnrichForInstallAsync(package).ConfigureAwait(true);
                    else if (provider is GameBananaModProvider gameBanana)
                        package = await gameBanana.EnrichWithFilesAsync(package).ConfigureAwait(true);
                }

                if (ModDownloadFileSelection.IsRecordUpdateAvailable(record, package))
                {
                    hasUpdates = true;
                    break;
                }
            }

            game.HasModUpdates = hasUpdates;
        }
        catch
        {
            // Non-fatal.
        }
    }

    public async Task RefreshAllModUpdateBadgesAsync()
    {
        foreach (var game in Games.Where(g => g.CanOpenMods && g.IsInstalled))
            await RefreshModUpdateFlagsForGameAsync(game).ConfigureAwait(true);
    }

    private void SelectInitialModsGamepadItem()
    {
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayToolbar;
        ApplyModsToolbarSelection(0);
    }

    private bool HandleModsGamepadNavigation(NavigationDirection direction)
    {
        return _gamepadNavigation.ActiveZone switch
        {
            GamepadNavigationZone.ModsOverlayToolbar => HandleModsToolbarNavigation(direction),
            GamepadNavigationZone.ModsOverlayFilters => HandleModsFiltersNavigation(direction),
            GamepadNavigationZone.ModsOverlaySourceFilters => HandleModsSourceFiltersNavigation(direction),
            GamepadNavigationZone.ModsOverlayList => HandleModsListNavigation(direction),
            GamepadNavigationZone.ModsOverlayRowActions => HandleModsRowActionsNavigation(direction),
            _ => false,
        };
    }

    private bool HandleModsToolbarNavigation(NavigationDirection direction)
    {
        var controls = CollectModsToolbarControls();
        if (controls.Count == 0)
            return false;

        var currentIndex = _modsGamepadToolbarIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
            direction,
            GamepadNavigationZone.ModsOverlayToolbar,
            GetMainContentGamepadZone(),
            isListLayout: true,
            positions: null,
            currentIndex,
            controls.Count);

        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);

        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;

        if (direction == NavigationDirection.Left && currentIndex <= 0)
            return TryApplyGamepadZoneTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));

        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyModsToolbarSelection(nextIndex);
        return true;
    }

    private bool HandleModsFiltersNavigation(NavigationDirection direction)
    {
        var controls = CollectModsFilterControls();
        if (controls.Count == 0)
            return false;

        var currentIndex = _modsGamepadFilterIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
            direction,
            GamepadNavigationZone.ModsOverlayFilters,
            GetMainContentGamepadZone(),
            isListLayout: true,
            positions: null,
            currentIndex,
            controls.Count);

        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);

        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;

        if (direction == NavigationDirection.Left && currentIndex <= 0)
            return TryApplyGamepadZoneTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));

        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyModsFiltersSelection(nextIndex);
        return true;
    }

    private bool HandleModsSourceFiltersNavigation(NavigationDirection direction)
    {
        var controls = CollectModsSourceFilterControls();
        if (controls.Count == 0)
        {
            return TryApplyGamepadZoneTransition(
                direction == NavigationDirection.Up
                    ? new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayFilters, null)
                    : new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayList, 0));
        }

        var currentIndex = _modsGamepadSourceFilterIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
            direction,
            GamepadNavigationZone.ModsOverlaySourceFilters,
            GetMainContentGamepadZone(),
            isListLayout: true,
            positions: null,
            currentIndex,
            ModListRows.Count);

        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);

        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;

        if (direction == NavigationDirection.Left && currentIndex <= 0)
            return TryApplyGamepadZoneTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));

        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyModsSourceFiltersSelection(nextIndex);
        return true;
    }

    private bool HandleModsListNavigation(NavigationDirection direction)
    {
        var currentIndex = _modsGamepadListIndex;
        var positions = ModListRows.Count > 0 ? CollectModCardPositions() : null;

        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
            direction,
            GamepadNavigationZone.ModsOverlayList,
            GetMainContentGamepadZone(),
            isListLayout: false,
            positions,
            currentIndex,
            ModListRows.Count);

        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);

        if (ModListRows.Count == 0)
            return true;

        var nextIndex = _gamepadNavigation.MoveLibraryIndex(
            currentIndex,
            direction,
            ModListRows.Count,
            isListLayout: false,
            positions);

        // Rightmost card: Right enters the card action buttons.
        if (direction == NavigationDirection.Right && nextIndex == currentIndex)
        {
            ApplyModsRowActionSelection(0);
            return true;
        }

        if (nextIndex == currentIndex &&
            direction is NavigationDirection.Left or NavigationDirection.Up)
        {
            var blockedTransition = _gamepadNavigation.TryGetBlockedMoveZoneTransition(
                direction,
                GamepadNavigationZone.ModsOverlayList,
                GetMainContentGamepadZone(),
                isListLayout: false,
                currentIndex,
                ModListRows.Count);

            if (blockedTransition.HasValue)
                return TryApplyGamepadZoneTransition(blockedTransition.Value);
        }

        ApplyModsListSelection(nextIndex);

        if (direction == NavigationDirection.Down && ShouldPrefetchMoreModsForGamepad(nextIndex))
            _ = LoadMoreModsAsync();

        return true;
    }

    private List<(double X, double Y)> CollectModCardPositions()
    {
        var positions = new List<(double X, double Y)>();
        foreach (var item in ModListRows)
        {
            var card = FindModListRowBorder(item);
            positions.Add(GetControlCenter(card) ?? (0, positions.Count * 220));
        }

        return positions;
    }

    private bool HandleModsRowActionsNavigation(NavigationDirection direction)
    {
        if (ModListRows.Count == 0)
            return false;

        var rowIndex = _gamepadNavigation.ClampIndex(_modsGamepadListIndex, ModListRows.Count);
        if (rowIndex < 0 || rowIndex >= ModListRows.Count)
            return false;

        var actions = CollectModsRowActionControls(rowIndex);
        if (actions.Count == 0)
            return false;

        var currentIndex = _gamepadNavigation.ClampIndex(_modsGamepadRowActionIndex, actions.Count);

        if (direction is NavigationDirection.Up or NavigationDirection.Down)
        {
            ClearModsRowActionGamepadFocus();
            var positions = CollectModCardPositions();
            var nextRow = _gamepadNavigation.MoveLibraryIndex(
                rowIndex,
                direction,
                ModListRows.Count,
                isListLayout: false,
                positions);
            ApplyModsListSelection(nextRow);

            if (direction == NavigationDirection.Down && ShouldPrefetchMoreModsForGamepad(nextRow))
                _ = LoadMoreModsAsync();

            return true;
        }

        // Left from the first action returns to the card (do not wrap to the far-right button).
        if (direction == NavigationDirection.Left && currentIndex <= 0)
        {
            ClearModsRowActionGamepadFocus();
            ApplyModsListSelection(rowIndex);
            return true;
        }

        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return false;

        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, actions.Count);
        ApplyModsRowActionSelection(nextIndex);
        return true;
    }

    private bool HandleModsGamepadConfirm()
    {
        switch (_gamepadNavigation.ActiveZone)
        {
            case GamepadNavigationZone.ModsOverlayToolbar:
                ActivateFocusedControl(CollectModsToolbarControls(), _modsGamepadToolbarIndex);
                return true;
            case GamepadNavigationZone.ModsOverlayFilters:
                ActivateFocusedControl(CollectModsFilterControls(), _modsGamepadFilterIndex);
                return true;
            case GamepadNavigationZone.ModsOverlaySourceFilters:
                ActivateFocusedControl(CollectModsSourceFilterControls(), _modsGamepadSourceFilterIndex);
                return true;
            case GamepadNavigationZone.ModsOverlayList:
            {
                var index = _gamepadNavigation.ClampIndex(_modsGamepadListIndex, ModListRows.Count);
                if (index < 0 || index >= ModListRows.Count)
                    return true;

                var actions = CollectModsRowActionControls(index);
                if (actions.Count == 0)
                    return true;

                // Enter the action strip; do not fire Install/Update/etc until Confirm again.
                ApplyModsRowActionSelection(0);
                return true;
            }
            case GamepadNavigationZone.ModsOverlayRowActions:
                ActivateFocusedControl(CollectModsRowActionControls(_modsGamepadListIndex), _modsGamepadRowActionIndex);
                return true;
            default:
                return false;
        }
    }

    private bool HandleModsGamepadCancel()
    {
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.ModsOverlayRowActions)
        {
            ClearModsRowActionGamepadFocus();
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayList;
            ApplyModsListSelection(_modsGamepadListIndex);
            return true;
        }

        CloseModsOverlay();
        return true;
    }

    private static void ActivateFocusedControl(IReadOnlyList<Control> controls, int index)
    {
        if (index < 0 || index >= controls.Count)
            return;

        var control = controls[index];
        if (control is Button button)
            GamepadControlActivation.ActivateButton(button);
        else if (control is ComboBox comboBox)
            GamepadComboBoxNavigation.Open(comboBox);
        else if (control is TextBox textBox)
            GamepadControlActivation.ActivateTextBox(textBox);
        else
            control.Focus();
    }

    private List<Control> CollectModsToolbarControls()
    {
        var list = new List<Control>();
        void Add(Control? c)
        {
            if (c != null && c.IsVisible && c.IsEnabled)
                list.Add(c);
        }

        Add(ModsRefreshButton);
        Add(ModsUpdateAllButton);
        Add(ModsOpenFolderButton);
        Add(ModsCloseButton);
        return list;
    }

    private List<Control> CollectModsFilterControls()
    {
        var list = new List<Control>();
        void Add(Control? c)
        {
            if (c != null && c.IsVisible && c.IsEnabled)
                list.Add(c);
        }

        Add(ModsTabBrowseButton);
        Add(ModsTabInstalledButton);
        Add(ModsSearchTextBox);
        Add(ModsSortByComboBox);
        return list;
    }

    private List<Control> CollectModsSourceFilterControls()
    {
        var list = new List<Control>();
        if (ModsSourceFilterPanel == null)
            return list;

        foreach (var button in ModsSourceFilterPanel.Children.OfType<Button>())
        {
            if (button.IsVisible && button.IsEnabled)
                list.Add(button);
        }

        return list;
    }

    private List<Control> CollectModsRowActionControls(int rowIndex)
    {
        var list = new List<Control>();
        if (rowIndex < 0 || rowIndex >= ModListRows.Count || ModsItemsControl == null)
            return list;

        var container = ModsItemsControl.ContainerFromIndex(rowIndex);
        if (container == null)
            return list;

        foreach (var button in container.GetVisualDescendants().OfType<Button>()
                     .Where(b => b.IsVisible && b.IsEnabled && b.Classes.Contains("options")))
        {
            list.Add(button);
        }

        return list;
    }

    private Border? FindModListRowBorder(ModListItem item)
    {
        return ModsItemsControl?.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => ReferenceEquals(b.DataContext, item) && b.Classes.Contains("catalog-focus-card"));
    }

    private void ApplyModsToolbarSelection(int index)
    {
        var controls = CollectModsToolbarControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _modsGamepadToolbarIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayToolbar;

        ClearGamepadFocus();
        ClearModsListGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;

        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    private void ApplyModsFiltersSelection(int index)
    {
        var controls = CollectModsFilterControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _modsGamepadFilterIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayFilters;

        ClearGamepadFocus();
        ClearModsListGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;

        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    private void ApplyModsSourceFiltersSelection(int index)
    {
        var controls = CollectModsSourceFilterControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _modsGamepadSourceFilterIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlaySourceFilters;

        ClearGamepadFocus();
        ClearModsListGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;

        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    private void ApplyModsListSelection(int index)
    {
        index = _gamepadNavigation.ClampIndex(index, ModListRows.Count);
        _modsGamepadListIndex = index;
        _modsGamepadRowActionIndex = -1;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayList;

        ClearModsToolbarGamepadFocus();
        ClearModsFiltersGamepadFocus();
        ClearModsSourceFiltersGamepadFocus();
        ClearModsRowActionGamepadFocus();
        ClearModsListGamepadFocus();
        ClearGamepadFocus();
        if (index < 0 || index >= ModListRows.Count)
            return;

        var row = ModListRows[index];
        row.IsGamepadFocused = true;
        Dispatcher.UIThread.Post(
            () => FindModListRowBorder(row)?.BringIntoView(),
            DispatcherPriority.Loaded);
    }

    private void ApplyModsRowActionSelection(int index)
    {
        var listIndex = _gamepadNavigation.ClampIndex(_modsGamepadListIndex, ModListRows.Count);
        if (listIndex < 0 || listIndex >= ModListRows.Count)
            return;

        var actions = CollectModsRowActionControls(listIndex);
        index = _gamepadNavigation.ClampIndex(index, actions.Count);
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayRowActions;

        // Assign action index after clearing focus classes so Left/Right use a stable index.
        ClearModsToolbarGamepadFocus();
        ClearModsFiltersGamepadFocus();
        ClearModsSourceFiltersGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(actions);
        ClearFocusIfOnControls(actions);
        _modsGamepadListIndex = listIndex;
        _modsGamepadRowActionIndex = index;
        if (index < 0 || index >= actions.Count)
            return;

        // Keep the card highlighted while moving Left/Right across its action buttons.
        ClearModsListGamepadFocus();
        ModListRows[listIndex].IsGamepadFocused = true;

        if (actions[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(actions[index]);
        Dispatcher.UIThread.Post(() => actions[index].BringIntoView(), DispatcherPriority.Loaded);
    }

    private void ClearModsToolbarGamepadFocus()
    {
        var controls = CollectModsToolbarControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    private void ClearModsFiltersGamepadFocus()
    {
        var controls = CollectModsFilterControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    private void ClearModsSourceFiltersGamepadFocus()
    {
        var controls = CollectModsSourceFilterControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    private void ClearModsListGamepadFocus()
    {
        foreach (var row in ModListRows)
            row.IsGamepadFocused = false;
    }

    private void ClearModsRowActionGamepadFocus()
    {
        if (_modsGamepadListIndex < 0)
        {
            _modsGamepadRowActionIndex = -1;
            return;
        }

        var controls = CollectModsRowActionControls(_modsGamepadListIndex);
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
        _modsGamepadRowActionIndex = -1;
    }

    private void ClearModsGamepadFocus()
    {
        ClearModsToolbarGamepadFocus();
        ClearModsFiltersGamepadFocus();
        ClearModsSourceFiltersGamepadFocus();
        ClearModsListGamepadFocus();
        ClearModsRowActionGamepadFocus();
    }

    private async void ModRowDetails_Click(object? sender, RoutedEventArgs e)
    {
        if (GetModListItem(sender) is not { } item)
            return;
        await OpenModDetailsAsync(item);
    }

    private void CloseModDetails_Click(object? sender, RoutedEventArgs e) => CloseModDetails();

    private void ModDetailsOpenPage_Click(object? sender, RoutedEventArgs e)
    {
        var url = _modDetailsItem?.Package.PackagePageUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowMessageBoxAsync($"Could not open page: {ex.Message}", "Mods");
        }
    }

    private async void ModDetailsTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tab)
            return;

        if (string.Equals(_modDetailsTab, tab, StringComparison.OrdinalIgnoreCase))
            return;

        _modDetailsTab = tab;
        UpdateModDetailsTabButtons();
        await LoadActiveModDetailsTabAsync();
    }

    private async Task OpenModDetailsAsync(ModListItem item)
    {
        _modDetailsItem = item;
        _isModDetailsOpen = true;
        _modDetailsTab = "Details";
        _modDetailsReadmeMarkdown = null;
        _modDetailsChangelogMarkdown = null;
        _modDetailsReadmeLoaded = false;
        _modDetailsChangelogLoaded = false;

        if (ModDetailsPanel != null)
            ModDetailsPanel.IsVisible = true;

        if (ModDetailsTitle != null)
        {
            var version = item.LatestVersion;
            ModDetailsTitle.Text = string.IsNullOrWhiteSpace(version)
                ? item.DisplayName
                : $"{item.DisplayName} · v{version}";
        }

        if (ModDetailsOpenPageButton != null)
            ModDetailsOpenPageButton.IsVisible = !string.IsNullOrWhiteSpace(item.Package.PackagePageUrl);

        UpdateModDetailsTabButtons();
        SetModDetailsLoading("Loading details…");

        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsDetailsOverlay;
        OnPropertyChanged(nameof(GamepadHintsVisible));

        if (IsGamepadFocusActive)
            Dispatcher.UIThread.Post(() => ApplyModDetailsGamepadSelection(0), DispatcherPriority.Loaded);

        await LoadActiveModDetailsTabAsync();

        if (!_isModDetailsOpen)
            return;

        if (IsGamepadFocusActive)
            ApplyModDetailsGamepadSelection(0);
        else
            ClearModDetailsGamepadFocus();
    }

    private void CloseModDetails()
    {
        ClearModDetailsGamepadFocus();
        _modDetailsGamepadFocusIndex = -1;
        _isModDetailsOpen = false;
        _modDetailsItem = null;
        _modDetailsReadmeMarkdown = null;
        _modDetailsChangelogMarkdown = null;
        _modDetailsReadmeLoaded = false;
        _modDetailsChangelogLoaded = false;

        if (ModDetailsPanel != null)
            ModDetailsPanel.IsVisible = false;

        if (ModDetailsContent != null)
            ModDetailsContent.ItemsSource = null;

        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.ModsDetailsOverlay)
        {
            if (_isModsOverlayOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayList;
                if (IsGamepadFocusActive)
                    ApplyModsListSelection(Math.Max(0, _modsGamepadListIndex));
            }
            else
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                if (IsGamepadFocusActive)
                    SelectInitialGamepadItemForCurrentView();
            }
        }

        OnPropertyChanged(nameof(GamepadHintsVisible));
    }

    private void UpdateModDetailsTabButtons()
    {
        ModDetailsTabDetailsButton?.Classes.Set("selected",
            string.Equals(_modDetailsTab, "Details", StringComparison.OrdinalIgnoreCase));
        ModDetailsTabChangelogButton?.Classes.Set("selected",
            string.Equals(_modDetailsTab, "Changelog", StringComparison.OrdinalIgnoreCase));
    }

    private void SetModDetailsLoading(string message)
    {
        if (ModDetailsContent == null)
            return;

        var loadingPanel = new StackPanel();
        loadingPanel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
            FontSize = 14,
        });
        ModDetailsContent.ItemsSource = new[] { loadingPanel };
    }

    private void SetModDetailsMarkdown(string? markdown, string emptyMessage)
    {
        if (ModDetailsContent == null)
            return;

        if (string.IsNullOrWhiteSpace(markdown))
        {
            var emptyPanel = new StackPanel();
            emptyPanel.Children.Add(new TextBlock
            {
                Text = emptyMessage,
                Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                FontSize = 14,
            });
            ModDetailsContent.ItemsSource = new[] { emptyPanel };
            return;
        }

        ModDetailsContent.ItemsSource = ParseMarkdown(markdown);
    }

    private async Task LoadActiveModDetailsTabAsync()
    {
        if (_modDetailsItem == null)
            return;

        var isChangelog = string.Equals(_modDetailsTab, "Changelog", StringComparison.OrdinalIgnoreCase);
        SetModDetailsLoading(isChangelog ? "Loading changelog…" : "Loading details…");

        if (!_gameManager.ModProviderRegistry.TryGet(_modDetailsItem.ProviderId, out var provider))
        {
            SetModDetailsMarkdown(null, "This mod provider does not support documentation.");
            return;
        }

        try
        {
            if (isChangelog)
            {
                if (!_modDetailsChangelogLoaded)
                {
                    _modDetailsChangelogMarkdown = await provider
                        .GetChangelogAsync(_modDetailsItem.Package)
                        .ConfigureAwait(true);
                    _modDetailsChangelogLoaded = true;
                }

                if (!_isModDetailsOpen ||
                    !string.Equals(_modDetailsTab, "Changelog", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetModDetailsMarkdown(_modDetailsChangelogMarkdown, "No changelog available.");
            }
            else
            {
                if (!_modDetailsReadmeLoaded)
                {
                    _modDetailsReadmeMarkdown = await provider
                        .GetReadmeAsync(_modDetailsItem.Package)
                        .ConfigureAwait(true);
                    _modDetailsReadmeLoaded = true;
                }

                if (!_isModDetailsOpen ||
                    !string.Equals(_modDetailsTab, "Details", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetModDetailsMarkdown(_modDetailsReadmeMarkdown, "No readme available.");
            }
        }
        catch (Exception ex)
        {
            if (!_isModDetailsOpen)
                return;
            SetModDetailsMarkdown(null, $"Failed to load documentation: {ex.Message}");
        }

        if (ModDetailsScrollViewer != null)
            ModDetailsScrollViewer.Offset = new Vector(0, 0);
    }

    private bool HandleModDetailsGamepadNavigation(NavigationDirection direction)
    {
        var controls = CollectModDetailsFocusableControls();
        if (controls.Count == 0)
            return false;

        // When Close/Open are focused, Left/Right move between header buttons.
        // Up/Down always scroll the body; Left/Right on tabs also move selection.
        var scrollViewer = ModDetailsScrollViewer;
        var onTabs = _modDetailsGamepadFocusIndex >= 0 &&
                     _modDetailsGamepadFocusIndex < controls.Count &&
                     (ReferenceEquals(controls[_modDetailsGamepadFocusIndex], ModDetailsTabDetailsButton) ||
                      ReferenceEquals(controls[_modDetailsGamepadFocusIndex], ModDetailsTabChangelogButton));

        if (direction is NavigationDirection.Up or NavigationDirection.Down)
        {
            if (scrollViewer != null)
            {
                const double step = 96;
                var delta = direction == NavigationDirection.Down ? step : -step;
                scrollViewer.Offset = new Vector(
                    scrollViewer.Offset.X,
                    Math.Max(0, scrollViewer.Offset.Y + delta));
            }

            return true;
        }

        if (direction is NavigationDirection.Left or NavigationDirection.Right)
        {
            var delta = direction == NavigationDirection.Right ? 1 : -1;
            ApplyModDetailsGamepadSelection(_modDetailsGamepadFocusIndex + delta);
            return true;
        }

        // Keep selection painted.
        if (onTabs)
            ApplyModDetailsGamepadSelection(_modDetailsGamepadFocusIndex);
        return true;
    }

    private void HandleModDetailsGamepadConfirm()
    {
        var controls = CollectModDetailsFocusableControls();
        var index = _gamepadNavigation.ClampIndex(_modDetailsGamepadFocusIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        ActivateFocusedControl(controls, index);
    }

    private List<Control> CollectModDetailsFocusableControls()
    {
        var list = new List<Control>();
        void Add(Control? c)
        {
            if (c != null && c.IsVisible && c.IsEnabled)
                list.Add(c);
        }

        Add(ModDetailsTabDetailsButton);
        Add(ModDetailsTabChangelogButton);
        Add(ModDetailsOpenPageButton);
        Add(CloseModDetailsButton);
        return list;
    }

    private void ApplyModDetailsGamepadSelection(int index)
    {
        var controls = CollectModDetailsFocusableControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _modDetailsGamepadFocusIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsDetailsOverlay;

        ClearModDetailsGamepadFocus();
        if (index < 0 || index >= controls.Count)
            return;

        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    private void ClearModDetailsGamepadFocus() =>
        ClearStyledControlsGamepadFocusClasses(CollectModDetailsFocusableControls());
}
