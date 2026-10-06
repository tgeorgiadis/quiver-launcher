using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>The Browse page: search, sort and filter the catalog, then open an app's details.</summary>
public partial class BrowseView : UserControl
{
    private LauncherSession? _session;
    private CancellationTokenSource? _searchDelay;
    private bool _updatingControls;
    private CancellationToken Token => _session?.Token ?? CancellationToken.None;
    public BrowseViewModel Model { get; private set; } = null!;
    public BrowseNavigation Navigation { get; private set; } = null!;
    public event Action<BrowseItem>? DetailsRequested;

    public BrowseView()
    {
        InitializeComponent();
        GamepadComboBoxNavigation.Attach(BrowseSortComboBox);
        foreach (var (id, name) in BrowseText.Sorts)
            BrowseSortComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = id });
    }

    public void Configure(BrowseViewModel model, LauncherSession session, IFeatureNavigationHost host, Func<bool> isActive)
    {
        Model = model;
        DataContext = model;
        _session = session;
        Navigation = new(this, host, () => !session.IsClosed && isActive());
        model.PropertyChanged += ModelChanged;
        model.Items.CollectionChanged += ItemsChanged;
        session.OnShutdown(() =>
        {
            model.PropertyChanged -= ModelChanged;
            model.Items.CollectionChanged -= ItemsChanged;
            _searchDelay?.Cancel();
        });
        UpdateControls();
    }

    /// <summary>Loads the catalog the first time Browse opens, or again after a failed load.</summary>
    public void EnsureLoaded()
    {
        UpdateControls();
        if (Model.Items.Count == 0 && !Model.IsLoading)
            Reload();
    }

    public void Reload()
    {
        UpdateControls();
        Run(() => Model.ReloadAsync(Token));
    }

    public void ApplyMobileLayout()
    {
        BrowseItemsControl.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Spacing = 12 });
        BrowseContentStack.Margin = new Thickness(0);
        BrowseToolbarPanel.Margin = new Thickness(0, 0, 0, 8);
        BrowseFiltersPanel.Margin = new Thickness(0, 0, 0, 8);
        BrowseSearchTextBox.Width = double.NaN;
        BrowseSearchTextBox.MinWidth = 200;
    }

    internal Task<bool> LoadMoreAsync() => _session?.RunAsync(() => Model.LoadMoreAsync(Token)) ?? Task.FromResult(false);

    private void Run(Func<Task> operation)
    {
        if (_session != null)
            _ = _session.RunAsync(operation);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session?.IsClosed != false) return;
        if (e.PropertyName is nameof(BrowseViewModel.Status) or nameof(BrowseViewModel.IsLoading))
            UpdateControls();
    }

    private void ItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_session?.IsClosed != false) return;
        if (Model.Items.Count == 0) BrowseScrollViewer.Offset = default;
        Navigation.SyncSelection();
    }

    /// <summary>Shows the current filters on their buttons and hides what doesn't apply.</summary>
    internal void UpdateControls()
    {
        if (Model == null) return;
        _updatingControls = true;
        try
        {
            var custom = Model.ShowingCustomList;
            BrowseCatalogTabButton.IsVisible = BrowseCustomListTabButton.IsVisible = Model.HasCustomList;
            BrowseCatalogTabButton.Classes.Set("selected", !custom);
            BrowseCustomListTabButton.Classes.Set("selected", custom);
            BrowsePlatformButton.IsVisible = BrowseConsoleButton.IsVisible = BrowseTypeButton.IsVisible = !custom;
            BrowseSortComboBox.IsVisible = !custom;
            BrowseClearFiltersButton.IsVisible = !custom && Model.HasFilters;
            BrowsePlatformButton.Content = BrowseText.Platforms.FirstOrDefault(p => p.Id == Model.Platform).Name ?? Model.Platform;
            BrowseConsoleButton.Content = Model.ConsoleName(Model.Console);
            BrowseTypeButton.Content = Model.ProjectType == null ? "All types" : BrowseText.ProjectTypeName(Model.ProjectType);
            BrowseSortComboBox.SelectedItem = BrowseSortComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == Model.Sort);
            // A search is ordered by relevance, so the sort would only mislead.
            BrowseSortComboBox.IsEnabled = string.IsNullOrWhiteSpace(Model.Search);
            BrowseStatusText.IsVisible = !string.IsNullOrEmpty(Model.Status);
            BrowseRetryButton.IsVisible = !Model.IsLoading && Model.Items.Count == 0 && !string.IsNullOrEmpty(Model.Status);
        }
        finally { _updatingControls = false; }
    }

    private void BrowseSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (Model == null || _updatingControls) return;
        Model.Search = BrowseSearchTextBox.Text ?? "";
        _searchDelay?.Cancel();
        var delay = _searchDelay = new CancellationTokenSource();
        Run(async () =>
        {
            try { await Task.Delay(300, delay.Token); }
            catch (OperationCanceledException) { return; }
            Reload();
        });
    }

    private void BrowseSort_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Model == null || _updatingControls || BrowseSortComboBox.SelectedItem is not ComboBoxItem { Tag: string sort } || sort == Model.Sort)
            return;
        Model.Sort = sort;
        Reload();
    }

    private void BrowseRetry_Click(object? sender, RoutedEventArgs e) => Reload();

    private void BrowseSourceTab_Click(object? sender, RoutedEventArgs e)
    {
        var custom = ReferenceEquals(sender, BrowseCustomListTabButton);
        if (Model.ShowingCustomList == custom) return;
        Model.ShowingCustomList = custom;
        Reload();
    }

    private void BrowseClearFilters_Click(object? sender, RoutedEventArgs e)
    {
        Model.ClearFilters();
        Navigation.KeepFilterFocusAfterClear();
        Reload();
    }

    /// <summary>Fills a filter's menu with its choices, the current one in bold.</summary>
    private void BrowseFilterFlyout_Opening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout) return;
        flyout.Items.Clear();
        void Add(string label, string? value, string? current, Action<string?> apply)
        {
            var item = new MenuItem { Header = label, FontWeight = value == current ? FontWeight.Bold : FontWeight.Normal };
            item.Click += (_, _) =>
            {
                apply(value);
                Reload();
            };
            flyout.Items.Add(item);
        }
        if (ReferenceEquals(flyout, BrowsePlatformButton.Flyout))
        {
            foreach (var (id, name) in BrowseText.Platforms)
                Add(id == BrowseText.CurrentPlatform ? $"{name} (this device)" : name, id, Model.Platform, v => Model.Platform = v);
        }
        else if (ReferenceEquals(flyout, BrowseConsoleButton.Flyout))
        {
            Add("All consoles", null, Model.Console, v => Model.Console = v);
            foreach (var brand in Model.Consoles.GroupBy(c => c.Brand))
            {
                Add($"All {BrowseViewModel.BrandName(brand.Key)}", "maker:" + brand.Key, Model.Console, v => Model.Console = v);
                foreach (var console in brand)
                    Add("    " + console.Name, console.Id, Model.Console, v => Model.Console = v);
            }
        }
        else
        {
            foreach (var (id, name) in BrowseText.ProjectTypes)
                Add(name, id, Model.ProjectType, v => Model.ProjectType = v);
        }
        GamepadMenuFlyoutNavigation.Attach(flyout);
    }

    private void BrowseScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Load the next page before the player reaches the end, like the Mods browser.
        var viewer = BrowseScrollViewer;
        if (Model?.CanLoadMore == true && viewer.Extent.Height > 0 &&
            viewer.Offset.Y + viewer.Viewport.Height >= viewer.Extent.Height - 400)
            _ = LoadMoreAsync();
    }

    private void BrowseCard_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: BrowseItem item }) return;
        Navigation.TrackPointerCard(Model.Items.IndexOf(item));
        DetailsRequested?.Invoke(item);
    }

    internal void OpenDetails(BrowseItem item) => DetailsRequested?.Invoke(item);
}
