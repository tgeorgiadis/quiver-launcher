using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>
/// An app's page over the App Catalog, laid out like Quiver Launcher 4's: the app's header over its artwork, then
/// Overview (the README) and Player feedback tabs, with its project details beside them. Feedback is read here and
/// written on quiverlauncher.com.
/// </summary>
public partial class BrowseDetailsView : UserControl, IFeatureNavigationHandler
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private MarkdownRenderer _renderer = null!;
    private Func<GameInfo, GameInfo?> _findInLibrary = _ => null;
    private GamepadNavigationService Nav => _host.Navigation;
    private GameInfo? _libraryApp;
    private bool _feedbackTab;
    // The first action was asked for before the actions arrived (they need the app's page): take it when they do.
    private bool _awaitingAction;
    internal int FocusIndex = -1;
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
        BrowseDetailsViewModel model, Func<GameInfo, GameInfo?> findInLibrary)
    {
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

    public void Open(BrowseItem item)
    {
        BrowseDetailsScrollViewer.Offset = default;
        BodyFocused = false;
        _feedbackTab = false;
        _ = _session.RunAsync(() => Model.OpenAsync(item, _session.Token));
        Refresh();
        if (_host.IsFocusActive)
            SelectFirstAction();
    }

    public void Close()
    {
        ClearHighlights();
        Model.Close();
        FocusIndex = -1;
        BodyFocused = false;
        BrowseDetailsReadme.ItemsSource = null;
    }

    /// <summary>Shows the app, and its library state again after it was added or removed.</summary>
    public void Refresh()
    {
        if (_session.IsClosed) return;
        var item = Model.Item;
        var app = item?.App;
        var entry = Model.Entry;
        _libraryApp = entry == null ? null : _findInLibrary(entry);

        var hero = FirstText(app?.LibraryArt?.Hero, app?.LibraryArt?.Header);
        AsyncImageLoader.ImageLoader.SetSource(BrowseDetailsHero, hero);
        BrowseDetailsHero.IsVisible = hero != null;
        BrowseDetailsBackdrop.MinHeight = hero != null && !PlatformCapabilities.IsMobile ? 360 : 0;
        AsyncImageLoader.ImageLoader.SetSource(BrowseDetailsArt, app == null ? entry?.GameIconUrl
            : FirstText(app.ArtworkFromGame ? null : app.Artwork, app.LibraryArt?.Logo, app.LibraryArt?.Capsule, app.Artwork, app.LibraryArt?.Header));
        BrowseDetailsKind.Text = item?.Kind.Replace(" · ", " / ").ToUpperInvariant();
        BrowseDetailsTitle.Text = item?.Title;
        BrowseDetailsTagline.Text = app?.Description;
        BrowseDetailsTagline.IsVisible = !string.IsNullOrWhiteSpace(app?.Description);
        BrowseDetailsGames.ItemsSource = item?.BasedOn;
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

        // An app from the player's own list has no page on the site: no feedback, only its README.
        var feedback = _feedbackTab && app != null;
        BrowseDetailsTabsBar.IsVisible = app != null;
        BrowseDetailsOverviewTab.Classes.Set("selected", !feedback);
        BrowseDetailsFeedbackTab.Classes.Set("selected", feedback);
        BrowseDetailsFeedbackCount.Text = said.ToString();
        BrowseDetailsOverview.IsVisible = !feedback;
        BrowseDetailsFeedback.IsVisible = feedback;
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
            else ApplySelection(FocusIndex, bringIntoView: false);
        }
    }

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

    private void Close_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void Tab_Click(object? sender, RoutedEventArgs e)
    {
        var feedback = ReferenceEquals(sender, BrowseDetailsFeedbackTab);
        if (_feedbackTab == feedback) return;
        _feedbackTab = feedback;
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
    private List<List<Control>> Rows() => new List<List<Control>>
    {
        Visible([BrowseDetailsCloseButton]),
        Visible(BrowseDetailsActions.Children),
        BrowseDetailsTabsBar.IsVisible ? Visible([BrowseDetailsOverviewTab, BrowseDetailsFeedbackTab]) : [],
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

    private int FirstActionIndex() => Rows() is { Count: > 1 } rows && rows[1].Any(c => BrowseDetailsActions.Children.Contains(c)) ? rows[0].Count : 0;

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
        var (row, column) = Locate(rows, FocusIndex);
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
        BodyFocused = false;
        // Entering the tabs lands on the open one.
        var column2 = nav.Column;
        if (nav.Row != row && rows[nav.Row].Contains(BrowseDetailsFeedbackTab))
            column2 = Math.Max(0, rows[nav.Row].IndexOf(_feedbackTab ? BrowseDetailsFeedbackTab : BrowseDetailsOverviewTab));
        ApplySelection(rows.Take(nav.Row).Sum(r => r.Count) + column2);
        return true;
    }

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
        var index = Nav.ClampIndex(FocusIndex, controls.Count);
        if (!BodyFocused && index >= 0 && controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
        return true;
    }

    public bool Cancel()
    {
        CloseRequested?.Invoke();
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
        Nav.ActiveZone = GamepadNavigationZone.BrowseDetailsOverlay;
        _host.ClearFocus();
        ClearHighlights();
        if (index < 0) return;
        controls[index].Classes.Set("gamepad-focused", true);
        controls[index].Focus();
        if (bringIntoView) controls[index].BringIntoView();
    }

    internal void ClearHighlights()
    {
        Control[] controls = [BrowseDetailsCloseButton, .. BrowseDetailsActions.Children, BrowseDetailsOverviewTab, BrowseDetailsFeedbackTab,
            BrowseDetailsReviewButton];
        foreach (var control in controls)
            control.Classes.Set("gamepad-focused", false);
    }

    public bool SynchronizePointer(object? source) =>
        GamepadPointerFocusSync.Hit(Nav, Controls(), GamepadNavigationZone.BrowseDetailsOverlay, FocusIndex, ApplySelection, source);

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
