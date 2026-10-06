using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>An app's details over Browse, with Add/Remove and links to its reviews and repository.</summary>
public partial class BrowseDetailsView : UserControl, IFeatureNavigationHandler
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private MarkdownRenderer _renderer = null!;
    private Func<GameInfo, GameInfo?> _findInLibrary = _ => null;
    private GamepadNavigationService Nav => _host.Navigation;
    private GameInfo? _libraryApp;
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
            BrowseDetailsArtHost.Width = BrowseDetailsArtHost.Height = 88;
            BrowseDetailsTitle.FontSize = 18;
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
        _ = _session.RunAsync(() => Model.OpenAsync(item, _session.Token));
        Refresh();
        if (_host.IsFocusActive)
            ApplySelection(FirstActionIndex());
    }

    public void Close()
    {
        ClearHighlights();
        Model.Close();
        FocusIndex = -1;
        BodyFocused = false;
        BrowseDetailsReadme.ItemsSource = null;
    }

    /// <summary>Shows the app's library state again, after it was added or removed.</summary>
    public void Refresh()
    {
        if (_session.IsClosed) return;
        var item = Model.Item;
        var app = item?.App;
        var entry = Model.Entry;
        _libraryApp = entry == null ? null : _findInLibrary(entry);
        AsyncImageLoader.ImageLoader.SetSource(BrowseDetailsArt, app == null ? entry?.GameIconUrl
            : FirstText(app.ArtworkFromGame ? null : app.Artwork, app.LibraryArt?.Capsule, app.LibraryArt?.Header, app.Artwork));
        BrowseDetailsKind.Text = item?.Kind;
        BrowseDetailsTitle.Text = item?.Title;
        BrowseDetailsSubtitle.Text = item?.Subtitle;
        BrowseDetailsFacts.Text = app == null ? (entry == null ? "" : string.Join(", ", entry.Tags))
            : string.Join(" · ", new[] { item!.ScoreText, BrowseText.PlatformNames(app.SupportedOS),
                app.Verified?.Version is { Length: > 0 } v ? $"Latest: {v}" : item.ReleaseText }.Where(t => t.Length > 0));
        BrowseDetailsNote.Text = Model.Error.Length > 0 ? Model.Error
            : _libraryApp != null ? "In your library."
            : app != null && entry == null ? "Loading…" : "";
        BrowseDetailsNote.IsVisible = BrowseDetailsNote.Text.Length > 0;
        BrowseDetailsDescription.Text = app?.Description;
        BrowseDetailsDescription.IsVisible = !string.IsNullOrWhiteSpace(app?.Description);
        BrowseDetailsReviewsHeading.IsVisible = BrowseDetailsReviewsStatus.IsVisible = app != null;
        BrowseDetailsReviewsStatus.Text = Model.ReviewsStatus;
        BrowseDetailsReviewsStatus.IsVisible = app != null && Model.ReviewsStatus.Length > 0;
        BrowseDetailsReviews.ItemsSource = Model.Reviews;
        BrowseDetailsAddButton.IsVisible = entry != null && _libraryApp == null;
        BrowseDetailsRemoveButton.IsVisible = _libraryApp != null;
        BrowseDetailsReviewButton.IsVisible = Model.ReviewUrl != null;
        BrowseDetailsRepositoryButton.IsVisible = !string.IsNullOrWhiteSpace(entry?.Repository);
        // Buttons appear as the details arrive; keep the highlight on a button while it is in this panel.
        if (_host.IsFocusActive && !BodyFocused && FocusIndex >= 0 && Nav.ActiveZone == GamepadNavigationZone.BrowseDetailsOverlay)
            ApplySelection(FocusIndex);
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

    // Controller and keyboard: Close on the first row, the actions on the second, then the
    // body scrolls (reviews and README are read-only).
    private List<Control> HeaderControls() => Visible([BrowseDetailsCloseButton]);
    private List<Control> Controls() => Visible([BrowseDetailsCloseButton, .. BrowseDetailsActions.Children.OfType<Button>()]);
    private static List<Control> Visible(IEnumerable<Control> controls) => controls.Where(c => c is { IsVisible: true, IsEnabled: true }).ToList();
    private int FirstActionIndex() => Controls().Count > HeaderControls().Count ? HeaderControls().Count : 0;

    public bool Navigate(NavigationDirection direction)
    {
        var controls = Controls();
        var nav = DetailsGamepadLayout.Move(direction, Nav.ClampIndex(FocusIndex, controls.Count), HeaderControls().Count,
            controls.Count, BodyFocused, BrowseDetailsScrollViewer.Offset.Y > 1);
        if (nav.LeaveTopBar || nav.LeaveSidebar)
        {
            BodyFocused = false;
            return _host.ApplyTransition(new GamepadZoneTransition(nav.LeaveTopBar ? GamepadNavigationZone.TopBar : GamepadNavigationZone.Sidebar, null));
        }
        if (nav.ScrollDown || nav.ScrollUp)
        {
            BodyFocused = true;
            ClearHighlights();
            var viewer = BrowseDetailsScrollViewer;
            viewer.Offset = new Vector(viewer.Offset.X, Math.Max(0, viewer.Offset.Y + (nav.ScrollDown ? 96 : -96)));
            return true;
        }
        BodyFocused = nav.Region == DetailsRegion.Body;
        if (!BodyFocused) ApplySelection(nav.Index);
        return true;
    }

    public bool Confirm()
    {
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
    public void RestoreFocus() => ApplySelection(Math.Max(0, FocusIndex));

    private void ApplySelection(int index)
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
    }

    internal void ClearHighlights()
    {
        var controls = Visible([BrowseDetailsCloseButton, .. BrowseDetailsActions.Children.OfType<Button>()]);
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
