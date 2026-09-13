using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Services;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;
using System.Collections.ObjectModel;

namespace QuiverLauncher.Views;
public partial class CatalogReviewView : UserControl
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private Func<bool> _isActive = () => false;
    private GamepadNavigationService _gamepadNavigation => _host.Navigation;
    private CatalogSyncViewModel _catalogSyncViewModel => Model;
    private CatalogReviewWorkspace _catalogReview => Workspace;
    private AppSettings _settings => SettingsModel.Current;
    private AppCatalogSource? _activeCatalogSyncSource => Workspace.ActiveSource;
    private ResettableObservableCollection<CatalogSyncRowItem> CatalogSyncRows => Model.Rows;
    private ObservableCollection<TagChipListItem> CatalogTagChips => Model.TagChips;
    private bool HasCatalogTagChips => Model.HasTagChips;
    public CatalogSyncViewModel Model { get; private set; } = new();
    public SettingsViewModel SettingsModel { get; private set; } = new();
    public CatalogReviewWorkspace Workspace { get; private set; } = null!;
    public CatalogReviewNavigation Navigation { get; private set; } = null!;
    public CatalogDetailsView Details { get; private set; } = null!;
    public bool IsActive => !_session.IsClosed && _isActive();
    public Action<string> Log { get; private set; } = _ =>
    {
    };

    public event Action? HeaderChanged;
    public event Action? LibraryRequested;
    public event Action? SourcesRequested;
    public event Action? GitHubTokenSettingsRequested;
    public event Action? GitLabTokenSettingsRequested;
    public event Action? PlatformRetryRequested;
    private void CatalogPlatformRetry_Click(object? sender, RoutedEventArgs e) => PlatformRetryRequested?.Invoke();
    private void CatalogShowAllPending_Click(object? sender, RoutedEventArgs e)
    {
        Navigation.ClearCatalogReviewFilterGamepadFocus();
        Model.RevealAllPendingReviews();
        ResetSearch();
        RefreshCatalogReviewFilterButtons("NeedsReview");
        UpdateCatalogReviewPlatformButton();
        ApplyCatalogSyncFilter();
        Dispatcher.UIThread.Post(() =>
        {
            if (IsActive && Model.Rows.Count > 0)
                Navigation.ApplyCatalogReviewRowSelection(0);
        }, DispatcherPriority.Loaded);
    }
    private void CatalogPlatformTokenSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.PlatformProvider == "gitlab") GitLabTokenSettingsRequested?.Invoke();
        else GitHubTokenSettingsRequested?.Invoke();
    }
    private void CatalogPlatformTokenSettings_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (Navigation == null) return;
        var index = Navigation.CollectCatalogReviewFilterControls().IndexOf(sender as Control ?? CatalogPlatformTokenSettingsButton);
        if (index < 0) return;
        _gamepadNavigation.CatalogReviewFilterIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewFilters;
    }
    internal void ReturnToSources() => SourcesRequested?.Invoke();
    public event Action<CatalogSyncRowItem>? DetailsRequested;
    public event Action? DetailsRefreshRequested;
    public CatalogReviewView()
    {
        InitializeComponent();
        GamepadMenuFlyoutNavigation.Attach(CatalogPlatformDetailsButton.Flyout as MenuFlyout);
        DataContext = Model;
        AddHandler(InputElement.GotFocusEvent, CatalogAdd_GotFocus);
    }

    private void CatalogAdd_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button) return;
        if (button.GetVisualAncestors().OfType<MobileActionRow>().Any()) return;
        var isAdd = button.DataContext is CatalogReviewGridCardActions.ChromeItem
            { Kind: CatalogReviewGridCardActions.ChromeKind.Add } || button.Classes.Contains("catalog-review-add");
        if (!isAdd) return;
        var card = button.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(c => c.DataContext is CatalogSyncRowItem);
        if (card?.DataContext is not CatalogSyncRowItem row) return;
        // Use the outer row container so list, desktop grid and mobile templates work.
        card = card.GetVisualAncestors().OfType<Control>()
            .TakeWhile(c => ReferenceEquals(c.DataContext, row)).LastOrDefault() ?? card;
        void Changed(object? s, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(CatalogSyncRowItem.IsAddPending) || row.ShowAddButton || !button.IsFocused) return;
            card.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
                b.IsEffectivelyVisible && (b.DataContext is CatalogReviewGridCardActions.ChromeItem
                    { Kind: CatalogReviewGridCardActions.ChromeKind.Details } || b.Content?.ToString() == "Details"))?.Focus();
        }
        void Lost(object? s, RoutedEventArgs args)
        {
            row.PropertyChanged -= Changed;
            button.LostFocus -= Lost;
        }
        row.PropertyChanged += Changed;
        button.LostFocus += Lost;
    }

    public void Configure(CatalogSyncViewModel model, CatalogReviewWorkspace workspace, SettingsViewModel settings, LauncherSession session, IFeatureNavigationHost host, CatalogDetailsView details, Func<bool> isActive, Action<string> log)
    {
        Model = model;
        DataContext = model;
        Workspace = workspace;
        SettingsModel = settings;
        _session = session;
        _host = host;
        Details = details;
        _isActive = isActive;
        Log = log;
        Model.SettingsModel = settings;
        settings.PropertyChanged += SettingsChanged;
        session.OnShutdown(() => settings.PropertyChanged -= SettingsChanged);
        Navigation = new(this, host);
        GamepadComboBoxNavigation.Attach(CatalogReviewSortByComboBox);
        CatalogReviewGridItemsControl.SizeChanged += (_, _) => FitMobileCatalogReviewGrid();
        CatalogReviewItemsHost.SizeChanged += (_, _) => FitMobileCatalogReviewGrid();
    }

    private void SettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Model.PublishPresentation();
    public void ResetSearch()
    {
        _catalogReviewFiltersExpanded = false;
        CatalogSearchTextBox.Text = "";
    }

    public void SortFlyoutOpening(MenuFlyout? flyout)
    {
        GamepadMenuFlyoutNavigation.Attach(flyout);
        if (flyout == null)
            return;
        foreach (var entry in flyout.Items.OfType<MenuItem>())
            entry.FontWeight = entry.Tag as string == Model.SortBy ? FontWeight.Bold : FontWeight.Normal;
    }

    public void SelectSort(string? tag)
    {
        if (tag == null)
            return;
        foreach (var item in CatalogReviewSortByComboBox.Items.OfType<ComboBoxItem>())
            if (item.Tag as string == tag)
            {
                CatalogReviewSortByComboBox.SelectedItem = item;
                break;
            }
    }

    private CatalogSyncRowItem? FindCatalogSyncRow(string key) => Workspace.FindRow(key);
    private void MoveControlTo(Control? control, Grid target, int column = 0)
    {
        if (control == null)
            return;
        if (control.Parent is Panel panel)
            panel.Children.Remove(control);
        else if (control.Parent is Decorator decorator)
            decorator.Child = null;
        else if (control.Parent is ContentControl content)
            content.Content = null;
        Grid.SetRow(control, 0);
        Grid.SetColumn(control, column);
        target.Children.Add(control);
    }

    internal string _currentCatalogReviewSortBy = "Name";
    /// <summary>
    /// Catalog-review grid card width. Desktop is fixed. Mobile cards stretch to fill
    /// virtualizing-grid cells, so width is Auto (NaN).
    /// </summary>
    public double CatalogReviewGridCardPixelSize => PlatformCapabilities.IsMobile ? double.NaN : 196;

    internal bool _mobileCatalogReviewReparented;
    internal bool _catalogReviewFiltersExpanded;
    internal bool _catalogReviewIgnoreSelection;
    internal bool _clearingCatalogReviewSelection;
    internal void FitMobileCatalogReviewGrid()
    {
        if (!PlatformCapabilities.IsMobile)
            return;
        if (CatalogReviewGridItemsControl != null)
        {
            CatalogReviewGridItemsControl.HorizontalAlignment = HorizontalAlignment.Stretch;
            CatalogReviewGridItemsControl.InvalidateMeasure();
        }

        if (CatalogReviewGridItemsControl?.ItemsPanelRoot is Panel panel)
            panel.InvalidateMeasure();
    }

    internal void ReparentMobileCatalogReviewChrome()
    {
        if (_mobileCatalogReviewReparented)
            return;
        MoveControlTo(CatalogSearchTextBox, CatalogReviewMobileChromeRow);
        if (CatalogSearchTextBox != null)
        {
            CatalogSearchTextBox.Width = double.NaN;
            CatalogSearchTextBox.MinWidth = 0;
            CatalogSearchTextBox.Margin = new Thickness(0);
            CatalogSearchTextBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            CatalogSearchTextBox.VerticalAlignment = VerticalAlignment.Center;
        }

        MoveControlTo(CatalogReviewTagsToggle, CatalogReviewMobileChromeRow, column: 2);
        if (CatalogReviewTagsToggle != null)
        {
            CatalogReviewTagsToggle.Margin = new Thickness(0);
            CatalogReviewTagsToggle.MinHeight = 0;
            CatalogReviewTagsToggle.Padding = new Thickness(10, 4);
            CatalogReviewTagsToggle.VerticalAlignment = VerticalAlignment.Center;
        }

        ReparentMobileCatalogReviewStatusChips();
        _mobileCatalogReviewReparented = true;
        ApplyCatalogReviewChrome();
    }

    internal void ReparentMobileCatalogReviewStatusChips()
    {
        if (CatalogReviewStatusScroller == null || CatalogReviewStatusChips == null)
            return;
        var host = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        foreach (var child in CatalogReviewStatusChips.Children.ToList())
        {
            CatalogReviewStatusChips.Children.Remove(child);
            if (child is Control control)
                control.Margin = new Thickness(0, 0, 6, 0);
            host.Children.Add(child);
        }

        CatalogReviewStatusScroller.Content = host;
        CatalogReviewStatusScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        CatalogReviewStatusScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        CatalogReviewStatusScroller.IsScrollInertiaEnabled = true;
    }

    internal void MobileCatalogMoreFlyout_Opening(object? sender, EventArgs e)
    {
        GamepadMenuFlyoutNavigation.Attach(sender as MenuFlyout);
        MarkCurrentCatalogViewFlyoutItem(sender as MenuFlyout);
        MarkCurrentCatalogPlatformFlyoutItem(sender as MenuFlyout);
    }

    internal void MobileCatalogBulkFlyout_Opening(object? sender, EventArgs e)
    {
        GamepadMenuFlyoutNavigation.Attach(sender as MenuFlyout);
        ApplyMobileCatalogBulkFlyoutItems();
    }

    internal void MarkCurrentCatalogViewFlyoutItem(MenuFlyout? flyout)
    {
        if (flyout == null)
            return;
        var selected = _settings.CatalogReviewUseGridView ? "view:grid" : "view:list";
        foreach (var entry in flyout.Items)
        {
            if (entry is MenuItem item && item.Tag is string tag && tag.StartsWith("view:", StringComparison.Ordinal))
                item.FontWeight = tag == selected ? FontWeight.Bold : FontWeight.Normal;
        }
    }

    internal static readonly (string Tag, CatalogReviewFilter Filter)[] CatalogReviewFilters = [("All", CatalogReviewFilter.All), ("NeedsReview", CatalogReviewFilter.NeedsReview), ("NotInLibrary", CatalogReviewFilter.NotInLibrary), ("Changed", CatalogReviewFilter.Changed), ("UpToDate", CatalogReviewFilter.UpToDate), ("Hidden", CatalogReviewFilter.Hidden), ];
    internal void CatalogTagChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;
        _catalogSyncViewModel.CycleTagChip(tag);
        ApplyCatalogSyncFilter();
    }

    internal void CatalogSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _catalogSyncViewModel.SearchText = CatalogSearchTextBox?.Text ?? "";
        if (_activeCatalogSyncSource != null)
            ApplyCatalogSyncFilter();
    }

    internal void CatalogReviewFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;
        var filter = CatalogReviewFilters.FirstOrDefault(f => f.Tag == tag).Filter;
        _catalogSyncViewModel.ReviewFilter = filter;
        RefreshCatalogReviewFilterButtons(tag);
        ApplyCatalogSyncFilter();
    }

    internal void RefreshCatalogReviewFilterButtons(string selectedTag)
    {
        foreach (var(tag, _)in CatalogReviewFilters)
        {
            var buttonName = tag switch
            {
                "All" => "CatalogFilterAllButton",
                "NeedsReview" => "CatalogFilterNeedsReviewButton",
                "NotInLibrary" => "CatalogFilterNotInLibraryButton",
                "Changed" => "CatalogFilterChangedButton",
                "UpToDate" => "CatalogFilterUpToDateButton",
                "Hidden" => "CatalogFilterHiddenButton",
                _ => null,
            };
            if (buttonName != null && this.FindControl<Button>(buttonName)is Button button)
                button.Classes.Set("selected", tag == selectedTag);
        }

        UpdateCatalogReviewFilterChipLabels();
    }

    internal void UpdateCatalogReviewFilterChipLabels()
    {
        if (this.FindControl<Button>("CatalogFilterNeedsReviewButton")is Button needsReviewButton)
        {
            var count = _catalogSyncViewModel.NeedsReviewCount;
            needsReviewButton.Content = count > 0 ? $"Needs review ({count})" : "Needs review";
        }

        if (this.FindControl<Button>("CatalogFilterNotInLibraryButton")is Button notInLibraryButton)
        {
            var count = _catalogSyncViewModel.NotInLibraryCount;
            notInLibraryButton.Content = count > 0 ? $"Not in library ({count})" : "Not in library";
        }

        if (this.FindControl<Button>("CatalogFilterChangedButton")is Button changedButton)
        {
            var count = _catalogSyncViewModel.ChangedCount;
            changedButton.Content = count > 0 ? $"Changed ({count})" : "Changed";
        }

        if (this.FindControl<Button>("CatalogFilterHiddenButton")is Button hiddenButton)
        {
            var count = _catalogSyncViewModel.HiddenCount;
            hiddenButton.Content = count > 0 ? $"Hidden ({count})" : "Hidden";
        }
    }

    internal void CatalogSyncAcknowledge_Click(object? sender, RoutedEventArgs e) => _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Acknowledge, null, _session.Token));
    internal void CatalogSyncRowRemoveFromLibrary_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetCatalogRowTag(sender, out var key))
            _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Remove, key, _session.Token));
    }

    internal void CatalogSyncRowHide_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetCatalogRowTag(sender, out var key))
            _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Hide, key, _session.Token));
    }

    internal void CatalogSyncRowAdd_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetCatalogRowTag(sender, out var key))
            _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Add, key, _session.Token));
    }

    internal void CatalogSyncRowMerge_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetCatalogRowTag(sender, out var key))
            _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Merge, key, _session.Token));
    }

    internal void CatalogSyncRowIgnore_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetCatalogRowTag(sender, out var key))
            _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Ignore, key, _session.Token));
    }

    internal void CatalogSyncRowReplace_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetCatalogRowTag(sender, out var key))
            _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Replace, key, _session.Token));
    }

    internal void CatalogSyncReplaceAll_Click(object? sender, RoutedEventArgs e) => _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.MergeAll, null, _session.Token));
    internal void CatalogSyncRowUnhide_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetCatalogRowTag(sender, out var key))
            _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.Unhide, key, _session.Token));
    }

    internal void CatalogSyncAddAll_Click(object? sender, RoutedEventArgs e) => _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(CatalogReviewAction.AddAll, null, _session.Token));
    internal void ApplyCatalogSyncFilter()
    {
        using var timing = CatalogPerformance.Measure("filter", Model.AllRows.Count);
        Model.AcceptPlatformDiscoveries();
        RefreshPresentedCatalog();
    }
    internal void StagePlatformDiscoveries()
    {
        Model.DeferPlatformDiscoveries();
        UpdateCatalogReviewFilterChipLabels();
        UpdateCatalogSyncBulkButtons();
    }
    internal void RefreshPresentedCatalog()
    {
        using var timing = CatalogPerformance.Measure("presentation", Model.AllRows.Count);
        Model.BeginPresentation();
        _catalogSyncViewModel.IgnoreArticlesWhenSorting = _settings.IgnoreArticlesWhenSorting;
        var isHiddenFilter = _catalogSyncViewModel.ReviewFilter == CatalogReviewFilter.Hidden;
        var rows = _catalogSyncViewModel.GetFilteredRows().ToList();
        foreach (var row in rows)
        {
            CatalogCompareService.ApplyReviewActionButtons(row, _activeCatalogSyncSource, _catalogSyncViewModel.ReviewFilter);
        }

        ReplaceCatalogSyncRows(rows);
        DetailsRefreshRequested?.Invoke();
        Model.PublishPresentation();
        if (this.FindControl<TextBlock>("CatalogReviewVersionText")is TextBlock versionText)
        {
            var summary = _catalogSyncViewModel.VersionBannerText;
            versionText.Text = summary;
            versionText.Classes.Set("catalog-review-version-unreviewed", _catalogSyncViewModel.ShowVersionBannerEmphasis);
        }

        HeaderChanged?.Invoke();
        ApplyCatalogReviewChrome();
        if (this.FindControl<TextBlock>("CatalogSyncEmptyText")is TextBlock emptyText)
        {
            var showNeedsReviewComplete = _catalogSyncViewModel.ShowNeedsReviewCompleteState;
            emptyText.Text = isHiddenFilter ? "No hidden apps for this source." : Model.PlatformEmptyText;
            emptyText.IsVisible = CatalogSyncRows.Count == 0 && !showNeedsReviewComplete && !Model.ShowHiddenPendingReviews;
        }

        if (this.FindControl<StackPanel>("CatalogSyncNeedsReviewEmptyPanel")is StackPanel needsReviewEmptyPanel)
            needsReviewEmptyPanel.IsVisible = _catalogSyncViewModel.ShowNeedsReviewCompleteState;
        UpdateCatalogReviewFilterChipLabels();
        UpdateCatalogSyncBulkButtons();
        if (_host.IsFocusActive && IsActive && (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewList || _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions))
        {
            Navigation.SyncCatalogReviewGamepadSelection();
        }
    }

    internal void CatalogReviewFiltersToggle_Click(object? sender, RoutedEventArgs e)
    {
        _catalogReviewFiltersExpanded = !_catalogReviewFiltersExpanded;
        ApplyCatalogReviewChrome();
        if (_host.IsFocusActive && _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewFilters)
        {
            var ranges = Navigation.GetCatalogReviewFilterRanges();
            if (ranges.Total <= 0)
                return;
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewFilterIndex, ranges.Total);
            Navigation.ApplyCatalogReviewFilterSelection(index < 0 ? 0 : index);
        }
    }

    internal void ApplyCatalogReviewChrome()
    {
        var mobile = PlatformCapabilities.IsMobile;
        var extraOpen = _catalogReviewFiltersExpanded;
        var compactText = _catalogSyncViewModel.VersionBannerCompactText;
        var hasTags = HasCatalogTagChips;
        var isReview = IsActive && IsActive;
        if (CatalogReviewDesktopToolsRow != null)
            CatalogReviewDesktopToolsRow.IsVisible = !mobile;
        if (CatalogReviewVersionBanner != null)
            CatalogReviewVersionBanner.IsVisible = false;
        if (CatalogReviewMobileChromeRow != null)
            CatalogReviewMobileChromeRow.IsVisible = mobile;
        if (CatalogReviewFiltersExtra != null)
            CatalogReviewFiltersExtra.IsVisible = extraOpen && hasTags;
        if (CatalogReviewFiltersToggle != null)
            CatalogReviewFiltersToggle.Content = "More";
        if (CatalogReviewTagsToggle != null)
        {
            var tagCount = _catalogSyncViewModel.ActiveTagChipCount;
            CatalogReviewTagsToggle.IsVisible = hasTags;
            CatalogReviewTagsToggle.Content = tagCount > 0 ? $"Tags ({tagCount})" : "Tags";
            CatalogReviewTagsToggle.Classes.Set("selected", extraOpen && hasTags);
        }

        HeaderChanged?.Invoke();
    }

    internal void ReplaceCatalogSyncRows(IEnumerable<CatalogSyncRowItem> rows)
    {
        var next = rows.ToList();
        if (!CatalogSyncRows.SequenceEqual(next))
        {
            // Strip synthetic focus before virtualization reassigns a card to another app.
            foreach (var button in this.GetVisualDescendants().OfType<Button>())
                button.Classes.Set("gamepad-focused", false);
            foreach (var row in Model.AllRows)
                row.IsGamepadFocused = false;
        }
        _catalogReviewIgnoreSelection = true;
        try
        {
            CatalogSyncRows.UpdateWith(next);
        }
        finally
        {
            _catalogReviewIgnoreSelection = false;
        }

        ClearCatalogReviewPointerSelection();
    }

    internal void CatalogReviewList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_catalogReviewIgnoreSelection || _clearingCatalogReviewSelection)
            return;
        ClearCatalogReviewPointerSelection();
    }

    internal void ClearCatalogReviewPointerSelection()
    {
        if (_clearingCatalogReviewSelection)
            return;
        if (CatalogSyncRowsItemsControl is not { SelectedIndex: >= 0 } list)
            return;
        if (IsCatalogReviewSelectionDrivenByGamepad())
            return;
        _clearingCatalogReviewSelection = true;
        try
        {
            list.SelectedIndex = -1;
        }
        finally
        {
            _clearingCatalogReviewSelection = false;
        }
    }

    internal bool IsCatalogReviewSelectionDrivenByGamepad() => _host.IsFocusActive && _gamepadNavigation.ActiveZone is GamepadNavigationZone.CatalogReviewList or GamepadNavigationZone.CatalogReviewRowActions;
    internal void CatalogSyncBackToLibrary_Click(object? sender, RoutedEventArgs e) => LibraryRequested?.Invoke();
    internal void CatalogSyncBackToSources_Click(object? sender, RoutedEventArgs e) => SourcesRequested?.Invoke();
    internal void UpdateCatalogSyncBulkButtons()
    {
        var addCount = _catalogSyncViewModel.FilteredBulkAddCount;
        var replaceCount = _catalogSyncViewModel.FilteredBulkReplaceCount;
        if (this.FindControl<Button>("CatalogSyncAddAllButton")is Button addAllButton)
        {
            addAllButton.Content = $"Add all new ({addCount})";
            addAllButton.IsEnabled = addCount > 0;
            addAllButton.IsVisible = !PlatformCapabilities.IsMobile && addCount > 0;
        }

        if (this.FindControl<Button>("CatalogReviewBulkButton")is Button bulkButton)
        {
            var showSkip = _catalogSyncViewModel.ShowSkipReviewButton;
            bulkButton.IsVisible = PlatformCapabilities.IsMobile && (addCount > 0 || replaceCount > 0 || showSkip);
        }

        ApplyMobileCatalogBulkFlyoutItems();
        if (this.FindControl<Button>("CatalogSyncReplaceAllButton")is Button replaceAllButton)
        {
            replaceAllButton.Content = $"Merge all changed ({replaceCount})";
            replaceAllButton.IsEnabled = replaceCount > 0;
            replaceAllButton.IsVisible = replaceCount > 0;
        }

        if (this.FindControl<Button>("CatalogSyncAcknowledgeButton")is Button skipReviewButton)
            skipReviewButton.IsVisible = _catalogSyncViewModel.ShowSkipReviewButton;
        if (CatalogReviewBulkPanel != null)
        {
            CatalogReviewBulkPanel.IsVisible = !PlatformCapabilities.IsMobile && (addCount > 0 || replaceCount > 0 || _catalogSyncViewModel.ShowSkipReviewButton);
        }
    }

    internal void ApplyMobileCatalogBulkFlyoutItems()
    {
        var addCount = _catalogSyncViewModel.FilteredBulkAddCount;
        var replaceCount = _catalogSyncViewModel.FilteredBulkReplaceCount;
        var showMerge = replaceCount > 0;
        var showSkip = _catalogSyncViewModel.ShowSkipReviewButton;
        if (CatalogReviewBulkAddItem != null)
        {
            CatalogReviewBulkAddItem.Header = addCount > 0 ? $"Add all ({addCount})" : "Add all";
            CatalogReviewBulkAddItem.IsEnabled = addCount > 0;
        }

        if (CatalogReviewBulkAddSeparator != null)
            CatalogReviewBulkAddSeparator.IsVisible = showMerge || showSkip;
        if (CatalogReviewBulkMergeItem != null)
        {
            CatalogReviewBulkMergeItem.Header = $"Merge all changed ({replaceCount})";
            CatalogReviewBulkMergeItem.IsVisible = showMerge;
            CatalogReviewBulkMergeItem.IsEnabled = showMerge;
        }

        if (CatalogReviewBulkSkipSeparator != null)
            CatalogReviewBulkSkipSeparator.IsVisible = showMerge && showSkip;
        if (CatalogReviewBulkSkipItem != null)
            CatalogReviewBulkSkipItem.IsVisible = showSkip;
    }

    internal void CatalogReviewGridCard_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: CatalogSyncRowItem row })
            row.IsHovered = true;
    }

    internal void CatalogReviewGridCard_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: CatalogSyncRowItem row })
            row.IsHovered = false;
    }

    internal void CatalogReviewCard_Tapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any()))
            return;
        if (sender is Control { DataContext: CatalogSyncRowItem row })
            DetailsRequested?.Invoke(row);
    }

    internal void CatalogReviewDetails_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetCatalogRowTag(sender, out var key))
            return;
        var row = FindCatalogSyncRow(key);
        if (row != null)
            DetailsRequested?.Invoke(row);
    }

    internal void CatalogReviewGridChrome_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CatalogReviewGridCardActions.ChromeItem item } button)
            return;
        if (item.IsMore)
            return;
        button.Tag = item.IdentityKey;
        switch (item.Kind)
        {
            case CatalogReviewGridCardActions.ChromeKind.Add:
                CatalogSyncRowAdd_Click(button, e);
                break;
            case CatalogReviewGridCardActions.ChromeKind.Merge:
                CatalogSyncRowMerge_Click(button, e);
                break;
            case CatalogReviewGridCardActions.ChromeKind.Details:
                CatalogReviewDetails_Click(button, e);
                break;
            case CatalogReviewGridCardActions.ChromeKind.Hide:
                CatalogSyncRowHide_Click(button, e);
                break;
            case CatalogReviewGridCardActions.ChromeKind.Unhide:
                CatalogSyncRowUnhide_Click(button, e);
                break;
            case CatalogReviewGridCardActions.ChromeKind.Remove:
                CatalogSyncRowRemoveFromLibrary_Click(button, e);
                break;
        }
    }

    internal static bool TryGetCatalogRowTag(object? sender, out string key)
    {
        key = sender switch
        {
            Button { Tag: string buttonKey } => buttonKey,
            MenuItem { Tag: string menuKey } => menuKey,
            _ => "",
        };
        return key.Length > 0;
    }

    internal void CatalogReviewSortByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;
        if (CatalogReviewSortByComboBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string sortMode)
            return;
        _currentCatalogReviewSortBy = sortMode;
        _catalogSyncViewModel.SortBy = sortMode;
        _catalogSyncViewModel.IgnoreArticlesWhenSorting = _settings.IgnoreArticlesWhenSorting;
        _settings.CatalogReviewSortBy = sortMode;
        SettingsModel.SaveCurrent();
        ApplyCatalogSyncFilter();
    }

    internal void CatalogReviewPlatformFlyout_Opening(object? sender, EventArgs e)
    {
        GamepadMenuFlyoutNavigation.Attach(sender as MenuFlyout);
        MarkCurrentCatalogPlatformFlyoutItem(sender as MenuFlyout);
    }

    internal void MarkCurrentCatalogPlatformFlyoutItem(MenuFlyout? flyout)
    {
        if (flyout == null)
            return;
        foreach (var entry in flyout.Items)
        {
            if (entry is not MenuItem item || item.Tag is not string tag)
                continue;
            if (CatalogPlatformSupport.Canonical(tag) == null && !tag.Equals("All", StringComparison.OrdinalIgnoreCase) && !tag.StartsWith("platform:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            item.FontWeight = CatalogPlatformSupport.IsSelected(Model.EffectivePlatformFilters, tag) ? FontWeight.Bold : FontWeight.Normal;
        }
    }

    internal void CatalogReviewPlatformItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag)
            return;
        if (CatalogPlatformSupport.Canonical(tag) == null && !tag.Equals("All", StringComparison.OrdinalIgnoreCase) && !tag.StartsWith("platform:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CatalogPlatformFilterSettings.SetFilters(_settings, Model.EffectivePlatformFilters);
        Model.RevealPendingPlatforms = false;
        if (tag.Equals("All", StringComparison.OrdinalIgnoreCase) || tag.Equals("platform:All", StringComparison.OrdinalIgnoreCase))
        {
            CatalogPlatformFilterSettings.SetAll(_settings);
        }
        else
        {
            CatalogPlatformFilterSettings.Toggle(_settings, tag);
        }

        _catalogSyncViewModel.PlatformFilters = _settings.CatalogPlatformFilters;
        UpdateCatalogReviewPlatformButton();
        SettingsModel.SaveCurrent();
        ApplyCatalogSyncFilter();
    }

    internal void EnsureCatalogPlatformFilterDefault()
    {
        Model.RevealPendingPlatforms = false;
        if (CatalogPlatformFilterSettings.EnsureDefault(_settings))
            SettingsModel.SaveCurrent();
        _catalogSyncViewModel.PlatformFilters = _settings.CatalogPlatformFilters;
        UpdateCatalogReviewPlatformButton();
    }

    internal void UpdateCatalogReviewPlatformButton()
    {
        if (CatalogReviewPlatformButton == null)
            return;
        CatalogReviewPlatformButton.Content = CatalogPlatformSupport.FormatLabel(Model.EffectivePlatformFilters);
    }

    internal void ApplyCatalogReviewSortSelection(string sortMode)
    {
        _currentCatalogReviewSortBy = sortMode;
        _catalogSyncViewModel.SortBy = sortMode;
        _catalogSyncViewModel.IgnoreArticlesWhenSorting = _settings.IgnoreArticlesWhenSorting;
        if (CatalogReviewSortByComboBox == null)
            return;
        foreach (var entry in CatalogReviewSortByComboBox.Items)
        {
            if (entry is ComboBoxItem item && item.Tag as string == sortMode)
            {
                CatalogReviewSortByComboBox.SelectedItem = item;
                break;
            }
        }
    }

    internal static bool ShouldShowCatalogReviewHelpLines(bool useGridView) => false;
    internal static bool ShouldShowCatalogReviewGrid(bool useGridView) => useGridView;
    internal static bool ShouldShowCatalogReviewList(bool useGridView) => !useGridView;
    internal void CatalogReviewLayout_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
            return;
        SetCatalogReviewUseGridView(string.Equals(tag, "grid", StringComparison.OrdinalIgnoreCase));
    }

    internal void MobileCatalogViewItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag)
            return;
        SetCatalogReviewUseGridView(string.Equals(tag, "view:grid", StringComparison.OrdinalIgnoreCase));
    }

    internal void SetCatalogReviewUseGridView(bool useGrid)
    {
        if (_settings.CatalogReviewUseGridView != useGrid)
        {
            _settings.CatalogReviewUseGridView = useGrid;
            SettingsModel.SaveCurrent();
        }

        UpdateCatalogReviewLayoutVisibility();
        if (_host.IsFocusActive && IsActive && IsActive && CatalogReviewFilterGamepadLayout.ShouldKeepFilterFocusAfterLayoutChange(_gamepadNavigation.ActiveZone))
        {
            var index = _gamepadNavigation.CatalogReviewFilterIndex;
            Navigation.ApplyCatalogReviewFilterSelection(index < 0 ? 0 : index);
        }
    }

    internal void UpdateCatalogReviewLayoutVisibility()
    {
        var useGrid = _settings.CatalogReviewUseGridView;
        if (CatalogSyncRowsItemsControl != null)
            CatalogSyncRowsItemsControl.IsVisible = ShouldShowCatalogReviewList(useGrid);
        if (CatalogReviewGridItemsControl != null)
            CatalogReviewGridItemsControl.IsVisible = ShouldShowCatalogReviewGrid(useGrid);
        CatalogReviewListViewButton?.Classes.Set("selected", ShouldShowCatalogReviewList(useGrid));
        CatalogReviewGridViewButton?.Classes.Set("selected", ShouldShowCatalogReviewGrid(useGrid));
        if (PlatformCapabilities.IsMobile && useGrid)
            FitMobileCatalogReviewGrid();
        var showHelp = ShouldShowCatalogReviewHelpLines(useGrid);
        if (CatalogReviewHelpStatusText != null)
            CatalogReviewHelpStatusText.IsVisible = showHelp;
        if (CatalogReviewHelpTagColorsText != null)
            CatalogReviewHelpTagColorsText.IsVisible = showHelp;
    }
}
