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
    // The filters sit beside the search when both fit; otherwise under it, four across, or two by two when narrower still.
    private enum FilterLayout { BesideSearch, FourAcross, TwoByTwo }
    private FilterLayout? _filterLayout;
    private const double FiltersBesideSearchWidth = 1000;
    private const double FiltersFourAcrossWidth = 640;
    private readonly double[] _filterMinWidths;
    // Cards narrower than this are drawn like the website's narrow card.
    private const double NarrowCardWidth = 250;
    private CancellationToken Token => _session?.Token ?? CancellationToken.None;
    public BrowseViewModel Model { get; private set; } = null!;
    /// <summary>Laid out for a phone, like the website's narrow layout.</summary>
    internal bool IsMobileLayout { get; private set; }
    public BrowseNavigation Navigation { get; private set; } = null!;
    public event Action<BrowseItem>? DetailsRequested;
    /// <summary>A matched game was opened: its page compares the apps that play it.</summary>
    public event Action<BrowseGame>? GameRequested;
    /// <summary>A card's Add button, or Y on a highlighted card: add its app without opening its page.</summary>
    public event Action<BrowseItem>? AddRequested;
    /// <summary>The player turned hiding library apps on or off; the shell keeps it for next time.</summary>
    public event Action<bool>? HideLibraryChosen;
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
        _filterMinWidths = [.. FilterCombos.Select(c => c.MinWidth)];
        BrowseFiltersPanel.SizeChanged += (_, e) => ArrangeFilters(e.NewSize.Width);
        BrowseItemsControl.SizeChanged += (_, e) => ArrangeCards(e.NewSize.Width);
        AddHandler(BrowseCard.AddRequestedEvent, (_, e) =>
        {
            if (e.Source is Control { DataContext: BrowseItem item }) RequestAdd(item);
        });
    }

    internal void RequestAdd(BrowseItem item)
    {
        if (item.CanAdd && !item.IsAdding) AddRequested?.Invoke(item);
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

        BrowseFilterGrid.ColumnSpacing = 12;
        // A phone's filters always go under the search, as on the website.
        _filterLayout = null;
        ArrangeFilters(BrowseFiltersPanel.Bounds.Width);

        BrowseGamesControl.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        BrowseItemsControl.ItemsPanel = new FuncTemplate<Panel?>(() => new CardColumnsPanel());
        BrowseItemsControl.Classes.Add("narrow-cards");
        UpdateControls();
    }

    private ComboBox[] FilterCombos => [BrowseTypeComboBox, BrowsePlatformComboBox, BrowseConsoleComboBox, BrowseAiComboBox];

    /// <summary>
    /// Lays the search and filters out for the width there is: the filters beside the search on a wide window; on a narrower
    /// one (or a phone) the search on its own line with the filters under it, four across or two by two, filling the width.
    /// </summary>
    internal void ArrangeFilters(double width)
    {
        var layout = width >= FiltersBesideSearchWidth && !IsMobileLayout ? FilterLayout.BesideSearch
            : width >= FiltersFourAcrossWidth ? FilterLayout.FourAcross
            : FilterLayout.TwoByTwo;
        if (layout == _filterLayout) return;
        _filterLayout = layout;
        var beside = layout == FilterLayout.BesideSearch;
        var columns = layout == FilterLayout.TwoByTwo ? 2 : 4;
        BrowseFilterGrid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat(beside ? "Auto" : "*", columns)));
        BrowseFilterGrid.RowDefinitions = new RowDefinitions(columns == 4 ? "Auto" : "Auto,Auto");
        var combos = FilterCombos;
        for (var i = 0; i < combos.Length; i++)
        {
            Grid.SetRow(combos[i], i / columns);
            Grid.SetColumn(combos[i], i % columns);
            combos[i].MinWidth = beside ? _filterMinWidths[i] : 0;
            combos[i].HorizontalAlignment = beside ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        BrowseFiltersIcon.IsVisible = beside;
        BrowseSearchTextBox.Margin = beside ? new Thickness(0, 0, 18, 0) : new Thickness(0, 0, 0, 10);
        Grid.SetColumnSpan(BrowseSearchTextBox, beside ? 1 : 3);
        Grid.SetRow(BrowseFilterGrid, beside ? 0 : 1);
        Grid.SetColumn(BrowseFilterGrid, beside ? 2 : 0);
        Grid.SetColumnSpan(BrowseFilterGrid, beside ? 1 : 3);
    }

    /// <summary>Cards that come out narrow (a small window) are drawn like the website's narrow card.</summary>
    private void ArrangeCards(double width)
    {
        if (IsMobileLayout || BrowseItemsControl.ItemsPanelRoot is not CardColumnsPanel panel || width <= 0) return;
        BrowseItemsControl.Classes.Set("narrow-cards", panel.ColumnWidthFor(width) < NarrowCardWidth);
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
        if (e.PropertyName is nameof(BrowseViewModel.Status) or nameof(BrowseViewModel.IsLoading) or nameof(BrowseViewModel.Total)
            or nameof(BrowseViewModel.HiddenInLibrary))
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
            BrowseHideLibraryCheckBox.IsChecked = Model.HideLibraryApps;
            // A search says what it shows, as on the website, and hidden library apps are counted so none seems to be missing.
            var hidden = Model.HiddenInLibrary == 0 ? ""
                : Model.HiddenInLibrary == 1 ? "1 app in your library is hidden" : $"{Model.HiddenInLibrary} apps in your library are hidden";
            var results = searching && !custom ? $"Results for “{Model.Search.Trim()}”" : "";
            BrowseResultsText.Text = results.Length > 0 && hidden.Length > 0 ? $"{results} · {hidden}" : results + hidden;
            BrowseResultsText.IsVisible = BrowseResultsText.Text.Length > 0;
            BrowseFilterGrid.IsVisible = !custom;
            BrowseFiltersIcon.IsVisible = !custom && _filterLayout == FilterLayout.BesideSearch;
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

    private void BrowseHideLibrary_Changed(object? sender, RoutedEventArgs e)
    {
        if (Model == null || _updatingControls) return;
        var hide = BrowseHideLibraryCheckBox.IsChecked == true;
        if (hide == Model.HideLibraryApps) return;
        Model.HideLibraryApps = hide;
        HideLibraryChosen?.Invoke(hide);
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
        if (sender is not Control { DataContext: BrowseItem item } || BrowseCard.IsOnAddButton(e)) return;
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
