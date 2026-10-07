using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>
/// An app's page over the App Catalog, laid out like Quiver Launcher 4's: the app's header over its artwork, then
/// Overview (the README), Releases (notes and what Quiver says about each) and Player feedback tabs, with its project
/// details beside them. Feedback is read here and written on quiverlauncher.com.
/// It also shows an original game's page: the game, then every app that plays it. Pages stack, so Back returns from an
/// app to the game it was opened from.
/// </summary>
public partial class BrowseDetailsView : UserControl, IFeatureNavigationHandler
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private MarkdownRenderer _renderer = null!;
    private Func<GameInfo, GameInfo?> _findInLibrary = _ => null;
    private GamepadNavigationService Nav => _host.Navigation;
    private GameInfo? _libraryApp;
    private BrowseViewModel _browse = null!;
    // An app (Item) or a game (Slug, Title); the last is shown, and Back returns to the one before.
    private sealed record Page(BrowseItem? Item, string? Slug, string? Title);
    private readonly List<Page> _pages = [];
    private Page? Current => _pages.Count > 0 ? _pages[^1] : null;
    private QuiverCatalogGameDetail? _game;
    private List<BrowseItem> _ways = [];
    private string _gameStatus = "";
    private int _gameGeneration;
    private enum Tab { Overview, Releases, Feedback }
    private Tab _tab;
    private IReadOnlyList<QuiverCatalogRelease>? _shownReleases;
    // The first action was asked for before the actions arrived (they need the app's page): take it when they do.
    private bool _awaitingAction;
    internal int FocusIndex = -1;
    // The highlighted control itself: rows appear as the page loads (Based on, actions), which moves its index.
    private Control? _focused;
    internal bool BodyFocused;
    public BrowseDetailsViewModel Model { get; private set; } = null!;

    public event Action? CloseRequested;
    public event Action<GameInfo>? AddRequested;
    public event Action<GameInfo>? RemoveRequested;
    public event Action<string>? OpenUrlRequested;

    public BrowseDetailsView()
    {
        InitializeComponent();
        if (PlatformCapabilities.IsMobile)
        {
            BrowseDetailsArtHost.Width = BrowseDetailsArtHost.Height = 72;
            BrowseDetailsArtHost.Margin = new Thickness(0, 0, 14, 0);
            BrowseDetailsTitle.FontSize = 22;
            // One column: the project details follow the open section.
            BrowseDetailsColumns.ColumnDefinitions = new ColumnDefinitions("*");
            BrowseDetailsOverview.Margin = BrowseDetailsFeedback.Margin = new Thickness(0);
            Grid.SetColumn(BrowseDetailsAbout, 0);
            Grid.SetRow(BrowseDetailsAbout, 1);
            BrowseDetailsAbout.Margin = new Thickness(0, 20, 0, 0);
        }
    }

    public void Configure(LauncherSession session, IFeatureNavigationHost host, MarkdownRenderer renderer,
        BrowseDetailsViewModel model, Func<GameInfo, GameInfo?> findInLibrary, BrowseViewModel browse)
    {
        _browse = browse;
        _session = session;
        _host = host;
        _renderer = renderer;
        _findInLibrary = findInLibrary;
        Model = model;
        model.PropertyChanged += ModelChanged;
        model.Readme.PropertyChanged += ReadmeChanged;
        session.OnShutdown(() =>
        {
            model.PropertyChanged -= ModelChanged;
            model.Readme.PropertyChanged -= ReadmeChanged;
            model.Dispose();
        });
    }

    /// <summary>Opens an app's page from the App Catalog.</summary>
    public void Open(BrowseItem item)
    {
        _pages.Clear();
        Show(new Page(item, null, null));
    }

    /// <summary>Opens an original game's page from the App Catalog.</summary>
    public void OpenGame(string slug, string title)
    {
        _pages.Clear();
        Show(new Page(null, slug, title));
    }

    private void Show(Page page)
    {
        ClearHighlights();
        _pages.Add(page);
        ShowCurrent();
    }

    /// <summary>Back: to the page this one was opened from, or out to the App Catalog.</summary>
    public void Back()
    {
        if (_pages.Count <= 1)
        {
            CloseRequested?.Invoke();
            return;
        }
        ClearHighlights();
        _pages.RemoveAt(_pages.Count - 1);
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        BrowseDetailsScrollViewer.Offset = default;
        BodyFocused = false;
        FocusIndex = -1;
        _tab = Tab.Overview;
        var page = Current!;
        if (page.Item is { } item)
        {
            _ = _session.RunAsync(() => Model.OpenAsync(item, _session.Token));
            Refresh();
            if (_host.IsFocusActive)
                SelectFirstAction();
            return;
        }
        Model.Close();
        BrowseDetailsReadme.ItemsSource = null;
        _game = null;
        _ways = [];
        _gameStatus = "Loading…";
        var generation = ++_gameGeneration;
        Refresh();
        if (_host.IsFocusActive)
            ApplySelection(0);
        _ = _session.RunAsync(async () =>
        {
            QuiverCatalogGameDetail? game = null;
            string status;
            try
            {
                game = await _browse.GetGameAsync(page.Slug!, _session.Token);
                status = game == null ? "This game is no longer in the catalog." : game.Entries.Count == 0 ? "No apps play this game yet." : "";
            }
            catch (Exception ex) when (!_session.Token.IsCancellationRequested)
            {
                status = $"Couldn't reach quiverlauncher.com. {ex.Message}";
            }
            if (generation != _gameGeneration || _session.IsClosed) return;
            _game = game;
            _ways = game == null ? [] : _browse.CardsFor(game);
            _gameStatus = status;
            Refresh();
        });
    }

    public void Close()
    {
        ClearHighlights();
        Model.Close();
        _pages.Clear();
        _game = null;
        _ways = [];
        ++_gameGeneration;
        FocusIndex = -1;
        BodyFocused = false;
        BrowseDetailsReadme.ItemsSource = null;
        BrowseDetailsWays.ItemsSource = null;
    }

    /// <summary>Shows the page, and its library state again after an app was added or removed.</summary>
    public void Refresh()
    {
        if (_session.IsClosed) return;
        // Back names the page it returns to.
        BrowseDetailsBackText.Text = _pages.Count > 1 ? _pages[^2].Item?.Title ?? _pages[^2].Title ?? "Back" : "App Catalog";
        var gamePage = Current is { Slug: not null };
        BrowseDetailsGamePanel.IsVisible = gamePage;
        BrowseDetailsSystems.IsVisible = gamePage;
        BrowseDetailsFacts.IsVisible = BrowseDetailsActions.IsVisible = !gamePage;
        if (gamePage)
        {
            RefreshGame();
            return;
        }
        SetArtShape(boxArt: false);
        var item = Model.Item;
        var app = item?.App;
        var entry = Model.Entry;
        _libraryApp = entry == null ? null : _findInLibrary(entry);

        var hero = FirstText(app?.LibraryArt?.Hero, app?.LibraryArt?.Header);
        AsyncImageLoader.ImageLoader.SetSource(BrowseDetailsHero, hero);
        BrowseDetailsHero.IsVisible = hero != null;
        AsyncImageLoader.ImageLoader.SetSource(BrowseDetailsArt, app == null ? entry?.GameIconUrl
            : FirstText(app.ArtworkFromGame ? null : app.Artwork, app.LibraryArt?.Logo, app.LibraryArt?.Capsule, app.Artwork, app.LibraryArt?.Header));
        BrowseDetailsKind.Text = item?.Kind.Replace(" · ", " / ").ToUpperInvariant();
        BrowseDetailsTitle.Text = item?.Title;
        BrowseDetailsTagline.Text = app?.Description;
        BrowseDetailsTagline.IsVisible = !string.IsNullOrWhiteSpace(app?.Description);
        // Each opens the game's page, as on the website.
        BrowseDetailsGames.ItemsSource = app?.Games.Where(g => !string.IsNullOrWhiteSpace(g.Title)).ToList()
            ?? item?.BasedOn.Select(title => new QuiverCatalogGame { Title = title }).ToList();
        BrowseDetailsBasedOn.IsVisible = item?.HasBasedOn == true;

        BrowseDetailsPlatforms.IsVisible = app?.SupportedOS.Count > 0;
        BrowseDetailsWindows.IsVisible = item?.RunsOnWindows == true;
        BrowseDetailsMacOS.IsVisible = item?.RunsOnMacOS == true;
        BrowseDetailsLinux.IsVisible = item?.RunsOnLinux == true;
        BrowseDetailsAndroid.IsVisible = item?.RunsOnAndroid == true;
        BrowseDetailsIOS.IsVisible = item?.RunsOnIOS == true;
        ToolTip.SetTip(BrowseDetailsPlatforms, item?.PlatformTip);

        // As on the website: what players said, then how many said it.
        var said = app == null ? 0 : app.Recommended + app.ReportIssues + app.ReportBroken;
        BrowseDetailsScore.IsVisible = app != null;
        BrowseDetailsScoreLabel.Text = item?.ScoreLabel;
        foreach (var tone in new[] { "positive", "caution", "negative" })
            BrowseDetailsScoreLabel.Classes.Set("tone-" + tone, item?.ScoreTone == tone);
        if (item?.ScoreMuted == true) BrowseDetailsScoreLabel.Foreground = new SolidColorBrush(Color.Parse("#8f929b"));
        else BrowseDetailsScoreLabel.ClearValue(TextElement.ForegroundProperty);
        BrowseDetailsScorePlayers.Text = said > 0 ? $"  ·  Feedback from {said} {(said == 1 ? "player" : "players")}" : "";
        BrowseDetailsVerified.IsVisible = app?.Verified?.Version is { Length: > 0 };
        BrowseDetailsVerifiedText.Text = $"{app?.Verified?.Version} · verified";

        BrowseDetailsNote.Text = Model.Error.Length > 0 ? Model.Error
            : _libraryApp != null ? "In your library."
            : app != null && entry == null ? "Loading…" : "";
        BrowseDetailsNote.IsVisible = BrowseDetailsNote.Text.Length > 0;
        BrowseDetailsAddButton.IsVisible = entry != null && _libraryApp == null;
        BrowseDetailsRemoveButton.IsVisible = _libraryApp != null;
        BrowseDetailsRepositoryButton.IsVisible = !string.IsNullOrWhiteSpace(entry?.Repository);

        // An app from the player's own list has no page on the site: no releases or feedback, only its README.
        var tab = app == null ? Tab.Overview : _tab;
        BrowseDetailsTabsBar.IsVisible = app != null;
        BrowseDetailsColumns.IsVisible = true;
        BrowseDetailsOverviewTab.Classes.Set("selected", tab == Tab.Overview);
        BrowseDetailsReleasesTab.Classes.Set("selected", tab == Tab.Releases);
        BrowseDetailsFeedbackTab.Classes.Set("selected", tab == Tab.Feedback);
        BrowseDetailsFeedbackCount.Text = said.ToString();
        BrowseDetailsOverview.IsVisible = tab == Tab.Overview;
        BrowseDetailsReleases.IsVisible = tab == Tab.Releases;
        BrowseDetailsFeedback.IsVisible = tab == Tab.Feedback;
        BrowseDetailsReleasesCountPill.IsVisible = Model.Releases != null;
        BrowseDetailsReleasesCount.Text = $"{Model.Releases?.Count}{(Model.ReleasesMore ? "+" : "")}";
        BrowseDetailsReleasesStatus.Text = Model.ReleasesStatus;
        BrowseDetailsReleasesStatus.IsVisible = Model.ReleasesStatus.Length > 0;
        if (!ReferenceEquals(_shownReleases, Model.Releases)) ShowReleases(Model.Releases);
        BrowseDetailsShareTitle.Text = said == 0 ? "Nobody has shared how it runs yet" : "Your experience helps the next person";
        BrowseDetailsReviewButton.IsVisible = Model.ReviewUrl != null;
        BrowseDetailsReviews.ItemsSource = Model.Reviews;
        BrowseDetailsReviewsStatus.Text = Model.ReviewsStatus;
        BrowseDetailsReviewsStatus.IsVisible = Model.ReviewsStatus.Length > 0;

        BrowseDetailsAbout.IsVisible = app != null;
        if (app != null) FillAbout(app);

        // Buttons appear as the details arrive; keep the highlight on a button while it is in this page.
        if (_host.IsFocusActive && !BodyFocused && FocusIndex >= 0 && Nav.ActiveZone == GamepadNavigationZone.BrowseDetailsOverlay)
        {
            if (_awaitingAction && FirstActionIndex() > 0) SelectFirstAction(bringIntoView: false);
            else ApplySelection(CurrentIndex(), bringIntoView: false);
        }
    }

    /// <summary>A game's page: the game over its artwork, then every app that plays it, best first.</summary>
    private void RefreshGame()
    {
        var game = _game?.Game;
        var art = game?.LibraryArt;
        var hero = FirstText(art?.Hero, art?.Header);
        AsyncImageLoader.ImageLoader.SetSource(BrowseDetailsHero, hero);
        BrowseDetailsHero.IsVisible = hero != null;
        SetArtShape(boxArt: !string.IsNullOrWhiteSpace(art?.Capsule));
        AsyncImageLoader.ImageLoader.SetSource(BrowseDetailsArt, game == null ? null : FirstText(art?.Capsule, game.Artwork, art?.Logo));
        BrowseDetailsKind.Text = "THE ORIGINAL GAME";
        BrowseDetailsTitle.Text = game?.Title ?? Current?.Title;
        BrowseDetailsTagline.Text = game?.Description;
        BrowseDetailsTagline.IsVisible = !string.IsNullOrWhiteSpace(game?.Description);
        BrowseDetailsBasedOn.IsVisible = false;
        BrowseDetailsSystems.Children.Clear();
        foreach (var system in (game?.OriginalSystems ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            BrowseDetailsSystems.Children.Add(new Border
            {
                Classes = { "details-tag" },
                Child = new TextBlock { Text = _browse.ConsoleName(system), FontSize = 11, Foreground = Brush("ThemeTextSecondary") },
            });
        BrowseDetailsNote.IsVisible = false;
        BrowseDetailsTabsBar.IsVisible = false;
        BrowseDetailsColumns.IsVisible = false;
        _browse.MarkLibraryState(_ways);
        BrowseDetailsWays.ItemsSource = _ways;
        BrowseDetailsWaysCount.Text = _ways.Count.ToString();
        BrowseDetailsWaysNote.Text = _ways.Count > 1 ? "Best first, by player feedback" : "Community ports and recreations";
        BrowseDetailsGameStatus.Text = _gameStatus;
        BrowseDetailsGameStatus.IsVisible = _gameStatus.Length > 0;
        if (_host.IsFocusActive && !BodyFocused && FocusIndex >= 0 && Nav.ActiveZone == GamepadNavigationZone.BrowseDetailsOverlay)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplySelection(CurrentIndex(), bringIntoView: false), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // A game's box art is tall; an app's icon is square.
    private void SetArtShape(bool boxArt)
    {
        var mobile = PlatformCapabilities.IsMobile;
        BrowseDetailsArtHost.Width = boxArt ? (mobile ? 72 : 120) : (mobile ? 72 : 104);
        BrowseDetailsArtHost.Height = boxArt ? (mobile ? 108 : 180) : (mobile ? 72 : 104);
        BrowseDetailsArtHost.Padding = new Thickness(boxArt ? 0 : 12);
        BrowseDetailsArt.Stretch = boxArt ? Stretch.UniformToFill : Stretch.Uniform;
    }

    /// <summary>Each release as a card, as on the website: version, date and what Quiver says, its reasons, then its notes.</summary>
    private void ShowReleases(IReadOnlyList<QuiverCatalogRelease>? releases)
    {
        _shownReleases = releases;
        BrowseDetailsReleaseList.Children.Clear();
        foreach (var release in releases ?? [])
        {
            var (label, color) = release.State switch
            {
                "verified" => ("Verified", "#8ac3a2"),
                "blocked" => ("Blocked", "#e38c87"),
                _ => ("Unverified", "#d7b56a"),
            };
            var brush = new SolidColorBrush(Color.Parse(color));
            var title = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = release.Version, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brush("ThemeText") });
            if (release.Prerelease)
                title.Children.Add(new Border { Classes = { "release-tag" }, Child = new TextBlock { Text = "Pre-release", FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#d7b56a")) } });
            var heading = new StackPanel();
            heading.Children.Add(title);
            if (release.ReleasedAt is { } at)
                heading.Children.Add(new TextBlock { Text = DateTimeOffset.FromUnixTimeMilliseconds((long)at).LocalDateTime.ToString("d MMM yyyy"), FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#8f929b")), Margin = new Thickness(0, 2, 0, 0) });
            var badge = new Border
            {
                Classes = { "result-badge" }, BorderBrush = new SolidColorBrush(Color.Parse("#66" + color[1..])),
                Background = new SolidColorBrush(Color.Parse("#1a" + color[1..])),
                Child = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = brush },
            };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(heading);
            Grid.SetColumn(badge, 1);
            header.Children.Add(badge);
            var body = new StackPanel { Spacing = 14 };
            body.Children.Add(header);
            if (release.State != "verified")
                foreach (var reason in release.Reasons)
                    body.Children.Add(new TextBlock { Text = reason, FontSize = 12, Foreground = brush, TextWrapping = TextWrapping.Wrap });
            var notes = new StackPanel();
            if (string.IsNullOrWhiteSpace(release.Notes))
                notes.Children.Add(new TextBlock { Text = "No release notes provided.", FontSize = 13, Foreground = Brush("ThemeTextSecondary") });
            else
                foreach (var control in _renderer.Render(release.Notes))
                    notes.Children.Add(control);
            body.Children.Add(notes);
            BrowseDetailsReleaseList.Children.Add(new Border { Classes = { "details-panel" }, Padding = new Thickness(22), Child = body });
        }
        if (Model.ReleasesMore)
            BrowseDetailsReleaseList.Children.Add(new TextBlock
            {
                Text = "Older releases are on the app's page on quiverlauncher.com.", FontSize = 12, Foreground = Brush("ThemeTextSecondary"),
            });
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    /// <summary>Project details, as in Quiver Launcher 4: who made it, where it runs, its latest release and AI use.</summary>
    private void FillAbout(QuiverCatalogApp app)
    {
        var rows = new List<(string Term, string Value)>();
        var maker = FirstText(Model.Project?.Author, app.Developer?.Name);
        if (maker != null) rows.Add(("Made by", maker));
        rows.Add(("Supported platforms", app.SupportedOS.Count == 0 ? "Not confirmed yet"
            : BrowseText.PlatformNames(new[] { "windows", "macos", "linux", "android", "ios" }.Where(app.SupportedOS.Contains))));
        rows.Add(("Latest release", app.LastReleaseAt == null ? "No releases found"
            : string.Join(" · ", new[] { app.LastReleaseVersion ?? "", BrowseText.ReleaseAge(app.LastReleaseAt, DateTimeOffset.UtcNow) }.Where(t => t.Length > 0))));
        rows.Add(("Verified release", app.Verified?.Version is { Length: > 0 } verified ? verified : "None yet"));
        rows.Add(("AI use", app.AiLevel switch { "assisted" => "AI-assisted", "generated" => "Mostly AI-generated", _ => "No AI use found" }));
        BrowseDetailsAboutRows.Children.Clear();
        foreach (var (term, value) in rows)
        {
            BrowseDetailsAboutRows.Children.Add(new TextBlock { Text = term, Classes = { "details-term" } });
            BrowseDetailsAboutRows.Children.Add(new TextBlock { Text = value, Classes = { "details-value" } });
        }
    }

    private static string? FirstText(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_session.IsClosed) Refresh();
    }

    private void ReadmeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session.IsClosed || e.PropertyName != nameof(DocumentViewModel.Content)) return;
        var content = Model.Readme.Content;
        BrowseDetailsReadme.ItemsSource = content.IsMarkdown && content.Text.Length > 0 ? _renderer.Render(content.Text, content.ImageBaseUrl) : new Control[]
        {
            new TextBlock { Text = content.Text, FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")), TextWrapping = TextWrapping.Wrap },
        };
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Back();

    private void GameChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: QuiverCatalogGame { Slug.Length: > 0 } game }) return;
        // The game this app was opened from: go back to it rather than open it again.
        if (_pages.Count > 1 && string.Equals(_pages[^2].Slug, game.Slug, StringComparison.OrdinalIgnoreCase)) Back();
        else Show(new Page(null, game.Slug, game.Title));
    }

    private void WayToPlay_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: BrowseItem item })
            Show(new Page(item, null, null));
    }

    private void Tab_Click(object? sender, RoutedEventArgs e)
    {
        var tab = ReferenceEquals(sender, BrowseDetailsFeedbackTab) ? Tab.Feedback
            : ReferenceEquals(sender, BrowseDetailsReleasesTab) ? Tab.Releases : Tab.Overview;
        if (_tab == tab) return;
        _tab = tab;
        if (tab == Tab.Releases) _ = _session.RunAsync(() => Model.LoadReleasesAsync(_session.Token));
        Refresh();
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.Entry is { } entry) AddRequested?.Invoke(entry);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (_libraryApp is { } app) RemoveRequested?.Invoke(app);
    }

    private void Review_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.ReviewUrl is { } url) OpenUrlRequested?.Invoke(url);
    }

    private void Repository_Click(object? sender, RoutedEventArgs e)
    {
        var url = Model.Entry is { } entry ? RepositorySourceHelper.GetRepositoryPageUrl(entry.EffectiveRepositorySource, entry.Repository) : null;
        if (!string.IsNullOrWhiteSpace(url)) OpenUrlRequested?.Invoke(url);
    }

    // Controller and keyboard: rows from the top (back, the actions, the tabs, the website button on Player feedback),
    // then the page scrolls (the README and feedback are read-only).
    private List<List<Control>> Rows()
    {
        if (Current is { Slug: not null })
            return [Visible([BrowseDetailsCloseButton]), .. WayRows()];
        return AppRows();
    }

    private List<Control> GameChips() => BrowseDetailsBasedOn.IsVisible
        ? BrowseDetailsGames.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is QuiverCatalogGame { Slug.Length: > 0 }).Cast<Control>().ToList()
        : [];

    private List<Control> WayCards() => BrowseDetailsWays.GetVisualDescendants().OfType<BrowseCard>().Cast<Control>().ToList();

    // The cards wrap, so each line of them is a row.
    private List<List<Control>> WayRows() => WayCards()
        .GroupBy(card => Math.Round(card.TranslatePoint(default, BrowseDetailsWays)?.Y ?? 0))
        .OrderBy(row => row.Key)
        .Select(row => row.OrderBy(card => card.TranslatePoint(default, BrowseDetailsWays)?.X ?? 0).ToList())
        .ToList();

    private List<List<Control>> AppRows() => new List<List<Control>>
    {
        Visible([BrowseDetailsCloseButton]),
        GameChips(),
        Visible(BrowseDetailsActions.Children),
        BrowseDetailsTabsBar.IsVisible ? Visible([BrowseDetailsOverviewTab, BrowseDetailsReleasesTab, BrowseDetailsFeedbackTab]) : [],
        BrowseDetailsFeedback.IsVisible ? Visible([BrowseDetailsReviewButton]) : [],
    }.Where(r => r.Count > 0).ToList();

    private List<Control> Controls() => Rows().SelectMany(r => r).ToList();
    private static List<Control> Visible(IEnumerable<Control> controls) => controls.Where(c => c is { IsVisible: true, IsEnabled: true }).ToList();
    private void SelectFirstAction(bool bringIntoView = true)
    {
        var index = FirstActionIndex();
        _awaitingAction = index == 0;
        ApplySelection(index, bringIntoView);
    }

    private int FirstActionIndex()
    {
        var rows = Rows();
        var actions = rows.FindIndex(row => row.Any(c => BrowseDetailsActions.Children.Contains(c)));
        return actions < 0 ? 0 : rows.Take(actions).Sum(row => row.Count);
    }

    private static (int Row, int Column) Locate(List<List<Control>> rows, int index)
    {
        for (var row = 0; row < rows.Count; row++)
        {
            if (index < rows[row].Count) return (row, Math.Max(0, index));
            index -= rows[row].Count;
        }
        return (Math.Max(0, rows.Count - 1), 0);
    }

    public bool Navigate(NavigationDirection direction)
    {
        var rows = Rows();
        var (row, column) = Locate(rows, CurrentIndex());
        // Up from the page returns to the controls once the last row is back in view.
        var lastRow = rows.Count > 0 ? rows[^1][0] : null;
        var rowAbove = lastRow?.TranslatePoint(default, BrowseDetailsScrollViewer) is { } at && at.Y < 0;
        var nav = DetailsGamepadLayout.Move(direction, rows.Select(r => r.Count).ToList(), row, column, BodyFocused, rowAbove);
        _awaitingAction = false;
        if (nav.LeaveTopBar || nav.LeaveSidebar)
        {
            BodyFocused = false;
            return _host.ApplyTransition(new GamepadZoneTransition(nav.LeaveTopBar ? GamepadNavigationZone.TopBar : GamepadNavigationZone.Sidebar, null));
        }
        if (nav.Body)
        {
            // Reading the page: no button may look highlighted while A does nothing.
            BodyFocused = true;
            ClearHighlights();
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
            if (nav.ScrollDown || nav.ScrollUp)
            {
                var viewer = BrowseDetailsScrollViewer;
                viewer.Offset = new Vector(viewer.Offset.X, Math.Max(0, viewer.Offset.Y + (nav.ScrollDown ? 96 : -96)));
            }
            return true;
        }
        var from = BodyFocused || rows.Count == 0 ? null : rows[row][Math.Min(column, rows[row].Count - 1)];
        BodyFocused = false;
        var column2 = nav.Column;
        if (nav.Row != row)
        {
            // Entering the tabs lands on the open one; any other row, on what sits nearest above or below.
            if (rows[nav.Row].Contains(BrowseDetailsFeedbackTab))
                column2 = Math.Max(0, rows[nav.Row].IndexOf(_tab switch
                {
                    Tab.Releases => BrowseDetailsReleasesTab,
                    Tab.Feedback => BrowseDetailsFeedbackTab,
                    _ => BrowseDetailsOverviewTab,
                }));
            else if (from != null && CentreX(from) is { } x)
                column2 = rows[nav.Row].Select((control, i) => (i, distance: Math.Abs((CentreX(control) ?? 0) - x)))
                    .OrderBy(c => c.distance).First().i;
        }
        ApplySelection(rows.Take(nav.Row).Sum(r => r.Count) + column2);
        return true;
    }

    /// <summary>Where the highlighted control is now; its old place if it has gone.</summary>
    private int CurrentIndex() => _focused != null && Controls().IndexOf(_focused) is >= 0 and var index ? index : FocusIndex;

    private double? CentreX(Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, 0), BrowseDetailsScrollViewer)?.X;

    public bool Confirm()
    {
        // Opened with the mouse: the first press only highlights an action.
        if (FocusIndex < 0 || (_awaitingAction && FirstActionIndex() == 0))
        {
            BodyFocused = false;
            SelectFirstAction();
            return true;
        }
        var controls = Controls();
        var index = Nav.ClampIndex(CurrentIndex(), controls.Count);
        if (BodyFocused || index < 0) return true;
        if (controls[index] is BrowseCard { DataContext: BrowseItem item })
            Show(new Page(item, null, null));
        else if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
        return true;
    }

    public bool Cancel()
    {
        Back();
        return true;
    }

    public bool Options() => Cancel();
    public void RestoreFocus()
    {
        BodyFocused = false;
        if (FocusIndex >= 0) ApplySelection(FocusIndex);
        else SelectFirstAction();
    }

    private void ApplySelection(int index) => ApplySelection(index, bringIntoView: true);

    private void ApplySelection(int index, bool bringIntoView)
    {
        var controls = Controls();
        index = Nav.ClampIndex(index, controls.Count);
        FocusIndex = index;
        _focused = index >= 0 ? controls[index] : null;
        Nav.ActiveZone = GamepadNavigationZone.BrowseDetailsOverlay;
        _host.ClearFocus();
        ClearHighlights();
        if (index < 0) return;
        if (controls[index] is BrowseCard { DataContext: BrowseItem card }) card.IsGamepadFocused = true;
        else controls[index].Classes.Set("gamepad-focused", true);
        controls[index].Focus();
        if (bringIntoView) controls[index].BringIntoView();
    }

    internal void ClearHighlights()
    {
        Control[] controls = [BrowseDetailsCloseButton, .. BrowseDetailsActions.Children, BrowseDetailsOverviewTab, BrowseDetailsReleasesTab, BrowseDetailsFeedbackTab,
            BrowseDetailsReviewButton];
        foreach (var control in controls.Concat(GameChips()))
            control.Classes.Set("gamepad-focused", false);
        foreach (var way in _ways)
            way.IsGamepadFocused = false;
    }

    public bool SynchronizePointer(object? source) =>
        GamepadPointerFocusSync.Hit(Nav, Controls(), GamepadNavigationZone.BrowseDetailsOverlay, CurrentIndex(), ApplySelection, source);

    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        if (nextZone != GamepadNavigationZone.BrowseDetailsOverlay)
            ClearHighlights();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        if (transition.Zone != GamepadNavigationZone.BrowseDetailsOverlay) return false;
        BodyFocused = false;
        ApplySelection(transition.SelectedIndex ?? Math.Max(0, FocusIndex));
        return true;
    }
}
