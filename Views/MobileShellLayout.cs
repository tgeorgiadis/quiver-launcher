using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using QuiverLauncher.Services;

namespace QuiverLauncher.Views;
/// <summary>View-side mobile arrangement and temporary attachment ownership.</summary>
public sealed class MobileShellLayout : IDisposable
{
    private readonly UserControl _root;
    private readonly LibraryToolbarView _toolbar;
    private readonly LauncherSession _session;
    private readonly Action _updateHeader;
    private readonly MobileInsetsController _insets;
    private bool _attached, _disposed;
    private int _attachment;
    private readonly SplitView MainSplitView;
    private readonly Grid MainGrid;
    private readonly Border SidebarPanel;
    private readonly Grid MobileNavDrawerHost;
    private readonly Grid MobileNavOverlay;
    private readonly Grid HeaderTitleColumn;
    private readonly TextBlock HeaderTitleText;
    private readonly HoverScrollText CatalogReviewHeaderTitleScroll;
    private readonly Button MobileSearchToggleButton;
    private readonly Button MobileNavButton;
    private readonly StackPanel SidebarNavPanel;
    private readonly ScrollViewer SidebarBodyScroller;
    private readonly StackPanel SidebarBodyHost;
    private readonly Grid CatalogContentPanel;
    private readonly LibraryView LibraryPanel;
    private readonly CatalogSourcesView CatalogSourcesPanel;
    private readonly SettingsView SettingsPanel;
    private readonly CatalogReviewView CatalogReviewPanel;
    private readonly LibraryFiltersView LibraryFiltersPanel;
    private readonly AppEntryEditorView EntryFormOverlay;
    private readonly AppUpdateReviewView AppUpdatesReviewPanel;
    private readonly ModsView ModsPanel;
    private readonly Border _navDimmer;
    public MobileShellLayout(UserControl root, LauncherSession session, Action updateHeader)
    {
        _root = root;
        _toolbar = root.FindControl<LibraryToolbarView>("LibraryToolbar")!;
        _session = session;
        _updateHeader = updateHeader;
        MainSplitView = root.FindControl<SplitView>("MainSplitView")!;
        MainGrid = root.FindControl<Grid>("MainGrid")!;
        SidebarPanel = root.FindControl<Border>("SidebarPanel")!;
        MobileNavDrawerHost = root.FindControl<Grid>("MobileNavDrawerHost")!;
        MobileNavOverlay = root.FindControl<Grid>("MobileNavOverlay")!;
        HeaderTitleColumn = root.FindControl<Grid>("HeaderTitleColumn")!;
        HeaderTitleText = root.FindControl<TextBlock>("HeaderTitleText")!;
        CatalogReviewHeaderTitleScroll = root.FindControl<HoverScrollText>("CatalogReviewHeaderTitleScroll")!;
        MobileSearchToggleButton = root.FindControl<Button>("MobileSearchToggleButton")!;
        MobileNavButton = root.FindControl<Button>("MobileNavButton")!;
        SidebarNavPanel = root.FindControl<StackPanel>("SidebarNavPanel")!;
        SidebarBodyScroller = root.FindControl<ScrollViewer>("SidebarBodyScroller")!;
        SidebarBodyHost = root.FindControl<StackPanel>("SidebarBodyHost")!;
        CatalogContentPanel = root.FindControl<Grid>("CatalogContentPanel")!;
        LibraryPanel = root.FindControl<LibraryView>("LibraryPanel")!;
        CatalogSourcesPanel = root.FindControl<CatalogSourcesView>("CatalogSourcesPanel")!;
        SettingsPanel = root.FindControl<SettingsView>("SettingsPanel")!;
        CatalogReviewPanel = root.FindControl<CatalogReviewView>("CatalogReviewPanel")!;
        LibraryFiltersPanel = root.FindControl<LibraryFiltersView>("LibraryFiltersPanel")!;
        EntryFormOverlay = root.FindControl<AppEntryEditorView>("EntryFormOverlay")!;
        AppUpdatesReviewPanel = root.FindControl<AppUpdateReviewView>("AppUpdatesReviewPanel")!;
        ModsPanel = root.FindControl<ModsView>("ModsPanel")!;
        _insets = new MobileInsetsController(root, () =>
        {
            ApplyMobileHeaderChrome();
            EntryFormOverlay.FitAvailableHeight();
        });
        MobileNavButton.Click += MobileNavButton_Click;
        MobileSearchToggleButton.Click += MobileSearchToggleButton_Click;
        _toolbar.SearchLostFocus += LibrarySearchTextBox_LostFocus;
        _navDimmer = root.FindControl<Border>("MobileNavDimmer")!;
        _navDimmer.PointerPressed += MobileNavDimmer_PointerPressed;
    }

    public void Attach()
    {
        if (_disposed || _session.IsClosed)
            return;
        if (!_attached)
        {
            _root.SizeChanged += SizeChanged;
            _attached = true;
            ++_attachment;
        }

        _insets.Attach();
        if (PlatformCapabilities.IsMobile)
            Post(() =>
            {
                var insets = TopLevel.GetTopLevel(_root)?.InsetsManager;
                if (insets != null)
                    _insets.ApplySafeAreaPadding(insets.SafeAreaPadding);
                LibraryPanel.FitMobileLibraryCardWidth();
                CatalogReviewPanel.FitMobileCatalogReviewGrid();
            }, DispatcherPriority.Loaded);
    }

    public void Detach()
    {
        _attached = false;
        ++_attachment;
        _root.SizeChanged -= SizeChanged;
        _insets.Detach();
    }

    private void SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        EntryFormOverlay.FitAvailableHeight();
        if (!PlatformCapabilities.IsMobile)
            return;
        _insets.Refresh();
        ApplyMobileHeaderChrome();
        LibraryPanel.FitMobileLibraryCardWidth();
        CatalogReviewPanel.FitMobileCatalogReviewGrid();
    }

    private void Post(Action action, DispatcherPriority priority)
    {
        var attachment = _attachment;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !_session.IsClosed && attachment == _attachment)
                action();
        }, priority);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
        _insets.Dispose();
        MobileNavButton.Click -= MobileNavButton_Click;
        MobileSearchToggleButton.Click -= MobileSearchToggleButton_Click;
        _toolbar.SearchLostFocus -= LibrarySearchTextBox_LostFocus;
        _navDimmer.PointerPressed -= MobileNavDimmer_PointerPressed;
    }

    private bool _mobileTopBarReparented;
    private bool _mobileSidebarBodyReparented;
    public bool IsSearchOpen;
    private bool _isMobileNavOpen;
    public bool IsMobileNavOpen
    {
        get => _isMobileNavOpen;
        set
        {
            if (_isMobileNavOpen == value)
                return;
            _isMobileNavOpen = value;
            if (!PlatformCapabilities.IsMobile)
                return;
            if (MainSplitView != null)
                MainSplitView.IsPaneOpen = false;
            if (MobileNavOverlay != null)
                MobileNavOverlay.IsVisible = value;
        }
    }

    public void ApplyMobileShell()
    {
        if (!PlatformCapabilities.IsMobile)
            return;
        _root.Classes.Set("mobile", true);
        if (SidebarPanel != null && MobileNavDrawerHost != null)
            MoveControlTo(SidebarPanel, MobileNavDrawerHost);
        BypassMobileSplitView();
        ApplyMobileContentInsets();
        CatalogSourcesPanel.ApplyMobileLayout();
        if (SettingsPanel != null)
        {
            SettingsPanel.Width = double.NaN;
            SettingsPanel.MaxWidth = 520;
            SettingsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        ReparentMobileTopBar();
        CatalogReviewPanel.ReparentMobileCatalogReviewChrome();
        ReparentMobileSidebarBody();
        LibraryPanel.ApplyMobileLayout();
        CatalogReviewPanel.FitMobileCatalogReviewGrid();
    }

    private void BypassMobileSplitView()
    {
        if (MainSplitView == null || MainGrid == null)
            return;
        if (MainSplitView.Parent is not Panel host)
            return;
        if (ReferenceEquals(MainGrid.Parent, host))
            return;
        MainSplitView.IsPaneOpen = false;
        MainSplitView.Content = null;
        MainSplitView.Pane = null;
        MainSplitView.IsVisible = false;
        host.Children.Insert(0, MainGrid);
    }

    private void ApplyMobileContentInsets()
    {
        var content = new Thickness(8, 8, 8, 8);
        LibraryPanel.ApplyContentInsets(content);
        if (CatalogContentPanel != null)
            CatalogContentPanel.Margin = content;
        CatalogSourcesPanel.ApplyMobileLayout();
        if (AppUpdatesReviewPanel != null)
            AppUpdatesReviewPanel.Margin = content;
        if (ModsPanel != null)
            ModsPanel.Margin = content;
    }

    private void ReparentMobileTopBar()
    {
        if (_mobileTopBarReparented)
            return;
        _toolbar.MoveSearchTo(HeaderTitleColumn);
        if (HeaderTitleColumn != null)
            HeaderTitleColumn.Margin = new Thickness(8, 0, 4, 0);
        if (HeaderTitleText != null)
            HeaderTitleText.Margin = new Thickness(0);
        if (CatalogReviewHeaderTitleScroll != null)
            CatalogReviewHeaderTitleScroll.Margin = new Thickness(0);
        _mobileTopBarReparented = true;
        ApplyMobileSearchChrome();
        ApplyMobileHeaderChrome();
    }

    private static void MoveControlTo(Control? control, Panel? destination, int column = 0, Dock dock = Dock.Left, int insertIndex = -1)
    {
        if (control == null || destination == null || ReferenceEquals(control.Parent, destination))
            return;
        DetachFromParent(control);
        if (insertIndex >= 0 && insertIndex <= destination.Children.Count)
            destination.Children.Insert(insertIndex, control);
        else
            destination.Children.Add(control);
        if (destination is Grid)
        {
            Grid.SetColumn(control, column);
            Grid.SetRow(control, 0);
        }
        else if (destination is DockPanel)
        {
            DockPanel.SetDock(control, dock);
        }
    }

    private void ReparentMobileSidebarBody()
    {
        if (_mobileSidebarBodyReparented)
            return;
        var nav = SidebarNavPanel;
        var filters = LibraryFiltersPanel;
        var scroller = SidebarBodyScroller;
        var host = SidebarBodyHost;
        if (nav == null || filters == null || scroller == null || host == null)
            return;
        MoveControlTo(nav, host);
        MoveControlTo(filters, host);
        filters.ApplyMobileLayout();
        scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        scroller.IsScrollInertiaEnabled = true;
        scroller.IsVisible = true;
        _mobileSidebarBodyReparented = true;
    }

    private static void DetachFromParent(Control control)
    {
        if (control.Parent is SplitView split && ReferenceEquals(split.Pane, control))
        {
            split.Pane = null;
            return;
        }

        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
            case ContentPresenter presenter:
                presenter.Content = null;
                break;
            case ContentControl contentControl:
                contentControl.Content = null;
                break;
        }
    }

    public bool IsMobileLandscape
    {
        get
        {
            if (!PlatformCapabilities.IsMobile)
                return false;
            var size = TopLevel.GetTopLevel(_root)?.ClientSize ?? default;
            if (size.Width > 1 && size.Height > 1)
                return size.Width > size.Height;
            return _root.Bounds.Width > _root.Bounds.Height && _root.Bounds.Width > 1;
        }
    }

    private void ApplyMobileHeaderChrome()
    {
        if (!PlatformCapabilities.IsMobile)
            return;
        var landscape = IsMobileLandscape;
        _root.Classes.Set("mobile-landscape", landscape);
        if (HeaderTitleText != null)
            HeaderTitleText.FontSize = landscape ? 14 : 18;
        if (CatalogReviewHeaderTitleScroll != null)
            CatalogReviewHeaderTitleScroll.FontSize = landscape ? 14 : 18;
        if (HeaderTitleColumn != null)
            HeaderTitleColumn.MinHeight = landscape ? 28 : 44;
        _updateHeader();
        CatalogReviewPanel.FitMobileCatalogReviewGrid();
    }

    public void CloseMobileNav()
    {
        if (PlatformCapabilities.IsMobile)
            IsMobileNavOpen = false;
    }

    private void MobileNavButton_Click(object? sender, RoutedEventArgs e)
    {
        IsMobileNavOpen = !IsMobileNavOpen;
    }

    private void MobileSearchToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsSearchOpen)
        {
            if (!_toolbar.HasQuery)
                CloseMobileSearch();
            return;
        }

        OpenMobileSearch();
    }

    private void OpenMobileSearch()
    {
        IsSearchOpen = true;
        ApplyMobileSearchChrome();
        Post(() => _toolbar.FocusSearch(), DispatcherPriority.Input);
    }

    public void CloseMobileSearch()
    {
        if (!IsSearchOpen)
            return;
        IsSearchOpen = false;
        ApplyMobileSearchChrome();
    }

    public void ApplyMobileSearchChrome()
    {
        if (!PlatformCapabilities.IsMobile)
            return;
        var open = IsSearchOpen;
        _updateHeader();
        _toolbar.SetSearchVisible(open);
        var hasQuery = _toolbar.HasQuery;
        if (MobileSearchToggleButton != null)
            MobileSearchToggleButton.Opacity = hasQuery ? 1 : 0.92;
    }

    private void LibrarySearchTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (!PlatformCapabilities.IsMobile || !IsSearchOpen)
            return;
        Post(() =>
        {
            if (!PlatformCapabilities.IsMobile || !IsSearchOpen)
                return;
            if (_toolbar.SearchContainsFocus)
                return;
            if (!_toolbar.HasQuery)
                CloseMobileSearch();
        }, DispatcherPriority.Input);
    }

    private void MobileNavDimmer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseMobileNav();
        e.Handled = true;
    }
}
