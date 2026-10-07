using Avalonia.Controls;
using QuiverLauncher;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
/// <summary>Projects shell navigation state onto shared headers and feature surfaces.</summary>
public sealed class ShellAppearance
{
    private readonly UserControl _root;
    private readonly ShellViewModel Shell;
    private readonly MobileShellLayout _mobileLayout;
    private readonly Action _refreshHeaderLayout;

    private readonly BrowseView BrowsePanel;
    private readonly LibraryFiltersView LibraryFiltersPanel;
    private readonly Button MobileAddButton;
    private readonly TextBlock HeaderTitleText;
    private readonly Button MobileSearchToggleButton;
    private readonly AppUpdateReviewView AppUpdatesReviewPanel;
    private readonly Button LibraryNavButton;
    private readonly ModsView ModsPanel;
    private readonly Button MobileSortButton;
    private readonly Button BrowseNavButton;
    private readonly LibraryToolbarView LibraryToolbar;
    private readonly LibraryView LibraryPanel;
    public ShellAppearance(UserControl root, ShellViewModel shell, MobileShellLayout mobile, Action refreshHeaderLayout)
    {
        _root = root;
        Shell = shell;
        _mobileLayout = mobile;
        _refreshHeaderLayout = refreshHeaderLayout;
        BrowsePanel = root.FindControl<BrowseView>("BrowsePanel")!;
        LibraryFiltersPanel = root.FindControl<LibraryFiltersView>("LibraryFiltersPanel")!;
        MobileAddButton = root.FindControl<Button>("MobileAddButton")!;
        HeaderTitleText = root.FindControl<TextBlock>("HeaderTitleText")!;
        MobileSearchToggleButton = root.FindControl<Button>("MobileSearchToggleButton")!;
        AppUpdatesReviewPanel = root.FindControl<AppUpdateReviewView>("AppUpdatesReviewPanel")!;
        LibraryNavButton = root.FindControl<Button>("LibraryNavButton")!;
        ModsPanel = root.FindControl<ModsView>("ModsPanel")!;
        MobileSortButton = root.FindControl<Button>("MobileSortButton")!;
        BrowseNavButton = root.FindControl<Button>("BrowseNavButton")!;
        LibraryToolbar = root.FindControl<LibraryToolbarView>("LibraryToolbar")!;
        LibraryPanel = root.FindControl<LibraryView>("LibraryPanel")!;
    }

    public void Refresh()
    {
        var isLibrary = Shell.Mode == MainViewMode.Library;
        var isBrowse = Shell.Mode == MainViewMode.Browse;
        var isAppUpdatesReview = isLibrary && Shell.AppUpdatesOpen;
        var isModsOverlay = isLibrary && Shell.ModsOpen && !Shell.AppUpdatesOpen;
        if (LibraryPanel is { } libraryView)
            libraryView.IsVisible = isLibrary;
        if (BrowsePanel is { } browsePanel)
            browsePanel.IsVisible = isBrowse;
        if (AppUpdatesReviewPanel is { } appUpdatesPanel)
            appUpdatesPanel.IsVisible = isAppUpdatesReview;
        if (ModsPanel is { } modsPanel)
            modsPanel.IsVisible = isModsOverlay;
        var showLibraryTools = isLibrary && !isAppUpdatesReview && !isModsOverlay;
        if (_root.FindControl<LibraryToolbarView>("LibraryToolbar")is { } libraryTopBar)
            libraryTopBar.IsVisible = showLibraryTools;
        if (MobileSearchToggleButton != null)
            MobileSearchToggleButton.IsVisible = showLibraryTools;
        if (MobileAddButton != null)
            MobileAddButton.IsVisible = showLibraryTools && _root is not MainView { KioskLocked: true };
        if (MobileSortButton != null)
            MobileSortButton.IsVisible = showLibraryTools;
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
        BrowseNavButton?.Classes.Set("selected", isBrowse);
        if (_root.FindControl<TextBlock>("HeaderTitleText")is TextBlock headerTitle)
        {
            headerTitle.Text = isModsOverlay ? "Mods" : isAppUpdatesReview ? "App Updates" : isLibrary ? "Library" : "App Catalog";
        }

        ApplyHeader();
        _mobileLayout.ApplyMobileSearchChrome();
    }

    public void ApplyHeader()
    {
        _refreshHeaderLayout();
        var searchOpen = PlatformCapabilities.IsMobile && _mobileLayout.IsSearchOpen;
        if (HeaderTitleText != null)
        {
            if (PlatformCapabilities.IsMobile)
                HeaderTitleText.FontSize = _mobileLayout.IsMobileLandscape ? 14 : 18;
            HeaderTitleText.IsVisible = !searchOpen;
        }
    }
}
