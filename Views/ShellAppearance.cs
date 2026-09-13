using Avalonia.Controls;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
/// <summary>Projects shell navigation state onto shared headers and feature surfaces.</summary>
public sealed class ShellAppearance
{
    private readonly UserControl _root;
    private readonly ShellViewModel Shell;
    private readonly MobileShellLayout _mobileLayout;
    private readonly CatalogSyncViewModel _catalogSyncViewModel;
    private readonly Func<AppCatalogSource?> _source;
    private readonly Action _refreshHeaderLayout;
    private AppCatalogSource? _activeCatalogSyncSource => _source();

    private readonly CatalogSourcesView CatalogSourcesPanel;
    private readonly LibraryFiltersView LibraryFiltersPanel;
    private readonly TextBlock CatalogReviewCompactSummary;
    private readonly Button MobileAddButton;
    private readonly TextBlock HeaderTitleText;
    private readonly Button MobileSearchToggleButton;
    private readonly CatalogReviewView CatalogReviewPanel;
    private readonly AppUpdateReviewView AppUpdatesReviewPanel;
    private readonly HoverScrollText CatalogReviewHeaderTitleScroll;
    private readonly Button MobileCatalogBackButton;
    private readonly Button LibraryNavButton;
    private readonly ModsView ModsPanel;
    private readonly Button MobileSortButton;
    private readonly Button AppCatalogNavButton;
    private readonly LibraryToolbarView LibraryToolbar;
    private readonly LibraryView LibraryPanel;
    private readonly Button MobileCatalogSortButton;
    public ShellAppearance(UserControl root, ShellViewModel shell, MobileShellLayout mobile, CatalogSyncViewModel review, Func<AppCatalogSource?> source, Action refreshHeaderLayout)
    {
        _root = root;
        Shell = shell;
        _mobileLayout = mobile;
        _catalogSyncViewModel = review;
        _source = source;
        _refreshHeaderLayout = refreshHeaderLayout;
        CatalogSourcesPanel = root.FindControl<CatalogSourcesView>("CatalogSourcesPanel")!;
        LibraryFiltersPanel = root.FindControl<LibraryFiltersView>("LibraryFiltersPanel")!;
        CatalogReviewCompactSummary = root.FindControl<TextBlock>("CatalogReviewCompactSummary")!;
        MobileAddButton = root.FindControl<Button>("MobileAddButton")!;
        HeaderTitleText = root.FindControl<TextBlock>("HeaderTitleText")!;
        MobileSearchToggleButton = root.FindControl<Button>("MobileSearchToggleButton")!;
        CatalogReviewPanel = root.FindControl<CatalogReviewView>("CatalogReviewPanel")!;
        AppUpdatesReviewPanel = root.FindControl<AppUpdateReviewView>("AppUpdatesReviewPanel")!;
        CatalogReviewHeaderTitleScroll = root.FindControl<HoverScrollText>("CatalogReviewHeaderTitleScroll")!;
        MobileCatalogBackButton = root.FindControl<Button>("MobileCatalogBackButton")!;
        LibraryNavButton = root.FindControl<Button>("LibraryNavButton")!;
        ModsPanel = root.FindControl<ModsView>("ModsPanel")!;
        MobileSortButton = root.FindControl<Button>("MobileSortButton")!;
        AppCatalogNavButton = root.FindControl<Button>("AppCatalogNavButton")!;
        LibraryToolbar = root.FindControl<LibraryToolbarView>("LibraryToolbar")!;
        LibraryPanel = root.FindControl<LibraryView>("LibraryPanel")!;
        MobileCatalogSortButton = root.FindControl<Button>("MobileCatalogSortButton")!;
    }

    public void Refresh()
    {
        var isLibrary = Shell.Mode == MainViewMode.Library;
        var isCatalog = Shell.Mode == MainViewMode.AppCatalog;
        var isReview = isCatalog && Shell.CatalogSubView == AppCatalogSubView.Review;
        var isAppUpdatesReview = isLibrary && Shell.AppUpdatesOpen;
        var isModsOverlay = isLibrary && Shell.ModsOpen && !Shell.AppUpdatesOpen;
        if (LibraryPanel is { } libraryView)
            libraryView.IsVisible = isLibrary;
        if (_root.FindControl<Grid>("CatalogContentPanel")is Grid catalogPanel)
            catalogPanel.IsVisible = isCatalog;
        if (CatalogSourcesPanel is { } sourcesPanel)
            sourcesPanel.IsVisible = isCatalog && !isReview;
        if (CatalogReviewPanel is { } reviewPanel)
            reviewPanel.IsVisible = isReview;
        if (AppUpdatesReviewPanel is { } appUpdatesPanel)
            appUpdatesPanel.IsVisible = isAppUpdatesReview;
        if (ModsPanel is { } modsPanel)
            modsPanel.IsVisible = isModsOverlay;
        var showLibraryTools = isLibrary && !isAppUpdatesReview && !isModsOverlay;
        if (_root.FindControl<LibraryToolbarView>("LibraryToolbar")is { } libraryTopBar)
            libraryTopBar.IsVisible = showLibraryTools;
        if (_root.FindControl<Panel>("CatalogReviewTopBarPanel")is Panel catalogReviewTopBar)
            catalogReviewTopBar.IsVisible = false;
        if (isReview)
            CatalogReviewPanel.UpdateCatalogReviewLayoutVisibility();
        if (_root.FindControl<Button>("CatalogReviewBackButton")is Button backButton)
            backButton.IsVisible = isReview;
        if (MobileSearchToggleButton != null)
            MobileSearchToggleButton.IsVisible = showLibraryTools;
        if (MobileAddButton != null)
            MobileAddButton.IsVisible = showLibraryTools;
        if (MobileSortButton != null)
            MobileSortButton.IsVisible = showLibraryTools;
        if (MobileCatalogBackButton != null)
            MobileCatalogBackButton.IsVisible = isReview;
        if (MobileCatalogSortButton != null)
            MobileCatalogSortButton.IsVisible = isReview;
        if (PlatformCapabilities.IsMobile)
        {
            if (!showLibraryTools)
                _mobileLayout.IsSearchOpen = false;
            else if (LibraryToolbar.HasQuery)
                _mobileLayout.IsSearchOpen = true;
        }

        if (LibraryFiltersPanel is { } libraryFilters)
        {
            libraryFilters.IsVisible = PlatformCapabilities.IsMobile ? !isAppUpdatesReview && !isModsOverlay : isLibrary && !isAppUpdatesReview && !isModsOverlay;
        }

        LibraryNavButton?.Classes.Set("selected", isLibrary);
        AppCatalogNavButton?.Classes.Set("selected", isCatalog);
        if (_root.FindControl<TextBlock>("HeaderTitleText")is TextBlock headerTitle)
        {
            headerTitle.Text = isModsOverlay ? "Mods" : isAppUpdatesReview ? "App Updates" : isLibrary ? "Library" : isReview && _activeCatalogSyncSource != null ? $"Review: {_activeCatalogSyncSource.Name}" : "App Catalog";
        }

        if (!isReview && CatalogReviewCompactSummary != null)
            CatalogReviewCompactSummary.IsVisible = false;
        ApplyHeader();
        _mobileLayout.ApplyMobileSearchChrome();
    }

    public void ApplyHeader()
    {
        _refreshHeaderLayout();
        var isReview = Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review && _activeCatalogSyncSource != null;
        var summary = _catalogSyncViewModel.VersionBannerCompactText;
        CatalogReviewCompactSummary.Text = summary;
        CatalogReviewCompactSummary.IsVisible = !PlatformCapabilities.IsMobile && isReview && !string.IsNullOrWhiteSpace(summary);
        CatalogReviewCompactSummary.Classes.Set("catalog-review-version-unreviewed", _catalogSyncViewModel.ShowVersionBannerEmphasis);
        ToolTip.SetTip(CatalogReviewCompactSummary, string.IsNullOrWhiteSpace(_catalogSyncViewModel.VersionBannerTooltip) ? summary : _catalogSyncViewModel.VersionBannerTooltip);
        var mobileReview = PlatformCapabilities.IsMobile && isReview;
        var searchOpen = PlatformCapabilities.IsMobile && _mobileLayout.IsSearchOpen;
        var compact = _catalogSyncViewModel.VersionBannerCompactText;
        var name = _activeCatalogSyncSource?.Name ?? "";
        var landscape = _mobileLayout.IsMobileLandscape;
        if (HeaderTitleText != null)
        {
            if (PlatformCapabilities.IsMobile)
                HeaderTitleText.FontSize = landscape ? 14 : 18;
            HeaderTitleText.IsVisible = !searchOpen && !mobileReview;
        }

        if (CatalogReviewHeaderTitleScroll != null)
        {
            if (PlatformCapabilities.IsMobile)
                CatalogReviewHeaderTitleScroll.FontSize = landscape ? 14 : 18;
            var showScroll = mobileReview && !searchOpen;
            CatalogReviewHeaderTitleScroll.IsVisible = showScroll;
            if (showScroll)
            {
                CatalogReviewHeaderTitleScroll.Text = string.IsNullOrWhiteSpace(compact) ? $"Review: {name}" : $"Review: {name} · {compact}";
                CatalogReviewHeaderTitleScroll.IsActive = true;
                var tip = _catalogSyncViewModel.VersionBannerTooltip;
                ToolTip.SetTip(CatalogReviewHeaderTitleScroll, string.IsNullOrWhiteSpace(tip) ? null : tip);
            }
            else
            {
                CatalogReviewHeaderTitleScroll.IsActive = false;
            }
        }
    }
}
