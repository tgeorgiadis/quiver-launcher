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

/// <summary>The App Catalog page: search, sort and filter the catalog, then open an app's details.</summary>
public partial class BrowseView : UserControl
{
    private LauncherSession? _session;
    private CancellationTokenSource? _searchDelay;
    private bool _updatingControls;
    private int _consoleChoices = -1;
    // On a phone the four filters sit two by two under the search; on a wider screen (a handheld held sideways) on one line.
    private Grid? _filterGrid;
    private const double FiltersOnOneLineWidth = 640;
    private CancellationToken Token => _session?.Token ?? CancellationToken.None;
    public BrowseViewModel Model { get; private set; } = null!;
    /// <summary>Laid out for a phone, like the website's narrow layout.</summary>
    internal bool IsMobileLayout { get; private set; }
    public BrowseNavigation Navigation { get; private set; } = null!;
    public event Action<BrowseItem>? DetailsRequested;
    /// <summary>A matched game was opened: its page compares the apps that play it.</summary>
    public event Action<BrowseGame>? GameRequested;
    /// <summary>The player chose an AI filter; the shell keeps it for next time, as the website does.</summary>
    public event Action<string?>? AiFilterChosen;

    public BrowseView()
    {
        InitializeComponent();
        Fill(BrowseSortComboBox, BrowseText.Sorts);
        Fill(BrowseTypeComboBox, BrowseText.ProjectTypes);
        Fill(BrowsePlatformComboBox, BrowseText.Platforms.Select(p =>
            (p.Id, p.Id != null && p.Id == BrowseText.CurrentPlatform ? $"{p.Name} (this device)" : p.Name)));
        Fill(BrowseAiComboBox, BrowseText.AiFilters);
        foreach (var combo in new[] { BrowseSortComboBox, BrowseTypeComboBox, BrowsePlatformComboBox, BrowseConsoleComboBox, BrowseAiComboBox })
            GamepadComboBoxNavigation.Attach(combo);
    }

    private const string Heading = "#heading";

    private static void Fill(ComboBox combo, IEnumerable<(string? Id, string Name)> choices)
    {
        foreach (var (id, name) in choices)
            combo.Items.Add(new ComboBoxItem { Content = name, Tag = id });
    }

    /// <summary>The console list, grouped by maker like the website: a heading, "All Nintendo", then each console.</summary>
    private void FillConsoles()
    {
        BrowseConsoleComboBox.Items.Clear();
        BrowseConsoleComboBox.Items.Add(new ComboBoxItem { Content = "All consoles", Tag = null });
        foreach (var brand in Model.Consoles.GroupBy(c => c.Brand))
        {
            BrowseConsoleComboBox.Items.Add(new ComboBoxItem
            {
                Content = BrowseViewModel.BrandName(brand.Key), Tag = Heading, IsEnabled = false,
                FontWeight = FontWeight.SemiBold, FontSize = 11,
            });
            BrowseConsoleComboBox.Items.Add(new ComboBoxItem { Content = BrowseViewModel.AllOfBrand(brand.Key), Tag = "maker:" + brand.Key, Padding = new Thickness(22, 6, 12, 6) });
            foreach (var console in brand)
                BrowseConsoleComboBox.Items.Add(new ComboBoxItem { Content = console.Name, Tag = console.Id, Padding = new Thickness(22, 6, 12, 6) });
        }
        _consoleChoices = Model.Consoles.Count;
    }

    private static void Select(ComboBox combo, string? id)
    {
        var item = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == id);
        // A console the facets don't list (they failed to load) still shows as chosen.
        if (item == null && id != null)
        {
            item = new ComboBoxItem { Content = id, Tag = id };
            combo.Items.Add(item);
        }
        combo.SelectedItem = item;
    }

    public void Configure(BrowseViewModel model, LauncherSession session, IFeatureNavigationHost host, Func<bool> isActive)
    {
        Model = model;
        DataContext = model;
        _session = session;
        Navigation = new(this, host, () => !session.IsClosed && isActive());
        model.PropertyChanged += ModelChanged;
        model.Items.CollectionChanged += ItemsChanged;
        model.Games.CollectionChanged += ItemsChanged;
        session.OnShutdown(() =>
        {
            model.PropertyChanged -= ModelChanged;
            model.Items.CollectionChanged -= ItemsChanged;
            model.Games.CollectionChanged -= ItemsChanged;
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

    /// <summary>
    /// The website's phone layout: the title and count with the sort beside them, the search across the width, the
    /// four filters two by two, then the cards two to a row filling the width.
    /// </summary>
    public void ApplyMobileLayout()
    {
        if (IsMobileLayout) return;
        IsMobileLayout = true;
        Classes.Add("mobile");
        BrowseContentStack.Margin = new Thickness(0);
        BrowseToolbarPanel.Margin = new Thickness(0, 0, 0, 12);
        BrowseFiltersPanel.Margin = new Thickness(0, 0, 0, 14);

        BrowseToolbarPanel.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
        BrowseTitleText.FontSize = 16;
        // The list tabs, when there is a list, go on their own line under the title.
        Grid.SetRow(BrowseSourceTabs, 1);
        Grid.SetColumn(BrowseSourceTabs, 0);
        Grid.SetColumnSpan(BrowseSourceTabs, 3);
        BrowseSourceTabs.Margin = new Thickness(0, 10, 0, 0);
        BrowseSortComboBox.MinWidth = 120;

        BrowseSearchTextBox.Margin = new Thickness(0, 0, 0, 10);
        BrowseSearchTextBox.MinWidth = 0;
        Grid.SetColumnSpan(BrowseSearchTextBox, 2);
        _filterGrid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        foreach (var combo in new[] { BrowseTypeComboBox, BrowsePlatformComboBox, BrowseConsoleComboBox, BrowseAiComboBox })
        {
            BrowseFilterSelects.Children.Remove(combo);
            combo.MinWidth = 0;
            combo.Margin = new Thickness(0);
            combo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            _filterGrid.Children.Add(combo);
        }
        ArrangeFilters(0);
        BrowseFiltersPanel.SizeChanged += (_, e) => ArrangeFilters(e.NewSize.Width);
        Grid.SetRow(_filterGrid, 1);
        Grid.SetColumnSpan(_filterGrid, 2);
        BrowseFiltersPanel.Children.Add(_filterGrid);
        // "Try again" and "Clear filters" follow on a line of their own when they show.
        BrowseFiltersPanel.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
        foreach (var icon in BrowseFilterSelects.Children.OfType<Avalonia.Controls.Shapes.Path>())
            icon.IsVisible = false;
        Grid.SetRow(BrowseFilterSelects, 2);
        Grid.SetColumn(BrowseFilterSelects, 0);
        Grid.SetColumnSpan(BrowseFilterSelects, 2);
        BrowseFilterSelects.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        BrowseRetryButton.Margin = BrowseClearFiltersButton.Margin = new Thickness(0, 10, 10, 0);

        BrowseGamesControl.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        BrowseItemsControl.ItemsPanel = new FuncTemplate<Panel?>(() => new CardColumnsPanel());
        BrowseItemsControl.Classes.Add("narrow-cards");
        UpdateControls();
    }

    private void ArrangeFilters(double width)
    {
        if (_filterGrid == null) return;
        var columns = width >= FiltersOnOneLineWidth ? 4 : 2;
        if (_filterGrid.ColumnDefinitions.Count == columns) return;
        _filterGrid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns)));
        _filterGrid.RowDefinitions = new RowDefinitions(columns == 4 ? "Auto" : "Auto,Auto");
        for (var i = 0; i < _filterGrid.Children.Count; i++)
        {
            Grid.SetRow(_filterGrid.Children[i], i / columns);
            Grid.SetColumn(_filterGrid.Children[i], i % columns);
        }
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
        if (e.PropertyName is nameof(BrowseViewModel.Status) or nameof(BrowseViewModel.IsLoading) or nameof(BrowseViewModel.Total))
            UpdateControls();
    }

    private void ItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_session?.IsClosed != false) return;
        if (Model.Items.Count == 0 && Model.Games.Count == 0) BrowseScrollViewer.Offset = default;
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
            var searching = !string.IsNullOrWhiteSpace(Model.Search);
            if (_consoleChoices != Model.Consoles.Count) FillConsoles();
            BrowseTitleText.Text = custom ? "My app list" : "Explore the catalog";
            BrowseCountText.Text = custom || Model.Total > 0 ? Model.Total.ToString() : "—";
            BrowseSourceTabs.IsVisible = BrowseCatalogTabButton.IsVisible = BrowseCustomListTabButton.IsVisible = Model.HasCustomList;
            BrowseCatalogTabButton.Classes.Set("selected", !custom);
            BrowseCustomListTabButton.Classes.Set("selected", custom);
            foreach (var combo in new[] { BrowseTypeComboBox, BrowsePlatformComboBox, BrowseConsoleComboBox, BrowseAiComboBox })
                combo.IsVisible = !custom;
            // A search is ordered by relevance, so the website shows that instead of the sort.
            BrowseSortComboBox.IsVisible = !custom && !searching;
            // A phone has no room for the label; the website leaves it out there too.
            BrowseSortLabel.IsVisible = !custom && !searching && !IsMobileLayout;
            BrowseRelevantText.IsVisible = !custom && searching;
            BrowseResultsText.IsVisible = !custom && searching;
            BrowseResultsText.Text = searching ? $"Results for “{Model.Search.Trim()}”" : "";
            if (_filterGrid != null) _filterGrid.IsVisible = !custom;
            Select(BrowseSortComboBox, Model.Sort);
            Select(BrowseTypeComboBox, Model.ProjectType);
            Select(BrowsePlatformComboBox, Model.Platform);
            Select(BrowseConsoleComboBox, Model.Console);
            Select(BrowseAiComboBox, Model.Ai);
            var empty = !Model.IsLoading && Model.Items.Count == 0;
            var failed = empty && Model.Status.StartsWith("Couldn't", StringComparison.Ordinal);
            BrowseClearFiltersButton.IsVisible = !custom && empty && !failed && (searching || Model.HasFilters);
            BrowseStatusText.IsVisible = !string.IsNullOrEmpty(Model.Status);
            BrowseRetryButton.IsVisible = failed;
        }
        finally { _updatingControls = false; }
    }

    private void BrowseSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (Model == null || _updatingControls) return;
        Model.Search = BrowseSearchTextBox.Text ?? "";
        UpdateControls();
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

    private void BrowseFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Model == null || _updatingControls || sender is not ComboBox { SelectedItem: ComboBoxItem item } combo) return;
        var id = (string?)item.Tag;
        if (id == Heading) return;
        if (ReferenceEquals(combo, BrowseTypeComboBox)) { if (id == Model.ProjectType) return; Model.ProjectType = id; }
        else if (ReferenceEquals(combo, BrowsePlatformComboBox)) { if (id == Model.Platform) return; Model.Platform = id; }
        else if (ReferenceEquals(combo, BrowseConsoleComboBox)) { if (id == Model.Console) return; Model.Console = id; }
        else if (ReferenceEquals(combo, BrowseAiComboBox))
        {
            if (id == Model.Ai) return;
            Model.Ai = id;
            AiFilterChosen?.Invoke(id);
        }
        Reload();
    }

    private void BrowseRetry_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.ShowingCustomList) Model.ForgetCustomList();
        Navigation.AfterRetry();
        Reload();
    }

    private void BrowseSourceTab_Click(object? sender, RoutedEventArgs e)
    {
        var custom = ReferenceEquals(sender, BrowseCustomListTabButton);
        if (Model.ShowingCustomList == custom) return;
        Model.ShowingCustomList = custom;
        // The list may have been edited since it was last read.
        if (custom) Model.ForgetCustomList();
        Reload();
    }

    private void BrowseClearFilters_Click(object? sender, RoutedEventArgs e)
    {
        Model.ClearFilters();
        _updatingControls = true;
        try { BrowseSearchTextBox.Text = ""; }
        finally { _updatingControls = false; }
        Navigation.KeepFilterFocusAfterClear();
        Reload();
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
        Navigation.TrackPointerCard(Navigation.CardIndexOf(item));
        DetailsRequested?.Invoke(item);
    }

    internal void OpenDetails(BrowseItem item) => DetailsRequested?.Invoke(item);
    internal void OpenGame(BrowseGame game) => GameRequested?.Invoke(game);

    private void BrowseGame_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: BrowseGame game }) return;
        Navigation.TrackPointerCard(Navigation.CardIndexOf(game));
        GameRequested?.Invoke(game);
    }
}
