using QuiverLauncher.Core.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class CatalogDetailsView : UserControl, IFeatureNavigationHandler
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private MarkdownRenderer _renderer = null!;
    private Action<string> _openUrl = _ =>
    {
    };
    private Func<CatalogSyncRowItem, CancellationToken, Task<DocumentContent>> _load = null!;
    private GamepadNavigationService _gamepadNavigation => _host.Navigation;

    internal int FocusIndex = -1;
    internal bool BodyFocused;
    public CatalogDetailsViewModel Model { get; } = new();

    public event Action? CloseRequested;
    public event Action<CatalogReviewAction, string>? ActionRequested;
    public CatalogDetailsView()
    {
        InitializeComponent();
    }

    public void Configure(LauncherSession session, IFeatureNavigationHost host, MarkdownRenderer renderer, Func<CatalogSyncRowItem, CancellationToken, Task<DocumentContent>> load, Action<string> openUrl)
    {
        _session = session;
        _host = host;
        _renderer = renderer;
        _load = load;
        _openUrl = openUrl;
        Model.PropertyChanged += ModelChanged;
        Model.Document.PropertyChanged += DocumentChanged;
        session.OnShutdown(() =>
        {
            Model.PropertyChanged -= ModelChanged;
            Model.Document.PropertyChanged -= DocumentChanged;
            Model.Dispose();
        });
    }

    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_session.IsClosed)
            return;
        DataContext = Model.Row;
        var row = Model.Row;
        CatalogReviewDetailsTitle.Text = row?.TitleName;
        CatalogReviewDetailsSubtitle.Text = row?.Subtitle;
        CatalogReviewDetailsOpenRepoButton.IsVisible = ShouldShowCatalogReviewOpenRepo(row?.Repository);
        CatalogReviewDetailsBlockedReason.Text = row?.AddBlockedReason;
        CatalogReviewDetailsBlockedReason.IsVisible = row?.HasAddBlockedReason == true;
        CatalogReviewDetailsDiffs.ItemsSource = row?.DetailsFields;
        CatalogReviewDetailsDiffsHost.IsVisible = row?.HasDetailsFields == true;
        CatalogReviewDetailsReadmeLoading.IsVisible = Model.IsLoading;
        ApplyCatalogReviewDetailsMobileChrome();
    }

    private void DocumentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_session.IsClosed)
            return;
        var content = Model.Document.Content;
        CatalogReviewDetailsReadmeContent.ItemsSource = content.IsMarkdown ? _renderer.Render(content.Text, content.ImageBaseUrl) : new Control[]
        {
            new TextBlock
            {
                Text = content.Text,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    public void Open(CatalogSyncRowItem row)
    {
        Model.Expanded = false;
        Bind(row);
        CatalogReviewDetailsScrollViewer.Offset = new Vector(0, 0);
        if (_host.IsFocusActive)
        {
            BodyFocused = false;
            ApplyCatalogReviewDetailsActionSelection(0);
        }
    }

    public void Bind(CatalogSyncRowItem row) => _ = _session.RunAsync(() => Model.BindAsync(row, _load, _session.Token));
    public void Close()
    {
        Model.Close();
        FocusIndex = -1;
        BodyFocused = false;
        CatalogReviewDetailsReadmeContent.ItemsSource = null;
    }

    private void CloseCatalogReviewDetails_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private void RequestAction(object? sender, CatalogReviewAction action)
    {
        if (sender is Control { Tag: string id })
            ActionRequested?.Invoke(action, id);
    }

    public bool Navigate(Services.NavigationDirection direction) => HandleCatalogReviewDetailsGamepadNavigation(direction);
    public bool Confirm()
    {
        ActivateCatalogReviewDetailsAction();
        return true;
    }

    public bool Cancel()
    {
        CloseRequested?.Invoke();
        return true;
    }

    public bool Options() => Cancel();
    public void RestoreFocus() => ApplyCatalogReviewDetailsActionSelection(Math.Max(0, FocusIndex));
    private static void ClearStyledControlsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
            control.Classes.Remove("gamepad-focused");
    }

    private void ClearFocusIfOnControls(IReadOnlyList<Control> controls)
    {
        var focus = TopLevel.GetTopLevel(this)?.FocusManager;
        if (focus?.GetFocusedElement()is Control control && controls.Contains(control))
            focus.Focus(null);
    }

    private void CatalogSyncRowAdd_Click(object? sender, RoutedEventArgs e) => RequestAction(sender, CatalogReviewAction.Add);
    private void CatalogSyncRowHide_Click(object? sender, RoutedEventArgs e) => RequestAction(sender, CatalogReviewAction.Hide);
    private void CatalogSyncRowIgnore_Click(object? sender, RoutedEventArgs e) => RequestAction(sender, CatalogReviewAction.Ignore);
    private void CatalogSyncRowMerge_Click(object? sender, RoutedEventArgs e) => RequestAction(sender, CatalogReviewAction.Merge);
    private void CatalogSyncRowRemoveFromLibrary_Click(object? sender, RoutedEventArgs e) => RequestAction(sender, CatalogReviewAction.Remove);
    private void CatalogSyncRowReplace_Click(object? sender, RoutedEventArgs e) => RequestAction(sender, CatalogReviewAction.Replace);
    private void CatalogSyncRowUnhide_Click(object? sender, RoutedEventArgs e) => RequestAction(sender, CatalogReviewAction.Unhide);
    internal void CatalogReviewDetailsAbout_Click(object? sender, RoutedEventArgs e)
    {
        Model.Expanded = !Model.Expanded;
        ApplyCatalogReviewDetailsMobileChrome();
        if (_host.IsFocusActive)
            ApplyCatalogReviewDetailsActionSelection(0);
    }

    internal void CatalogReviewDetailsOpenRepo_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.Row is not CatalogSyncRowItem row)
            return;
        var url = RepositorySourceHelper.GetRepositoryPageUrl(row.EffectiveRepositorySource, row.Repository);
        if (!string.IsNullOrWhiteSpace(url))
            _openUrl(url);
    }

    internal static bool ShouldShowCatalogReviewOpenRepo(string? repository) => !string.IsNullOrWhiteSpace(repository);
    internal void ApplyCatalogReviewDetailsMobileChrome()
    {
        if (!PlatformCapabilities.IsMobile)
        {
            if (CatalogReviewDetailsHeroExpanded != null)
                CatalogReviewDetailsHeroExpanded.IsVisible = false;
            if (CatalogReviewDetailsOpenRepoMobileButton != null)
                CatalogReviewDetailsOpenRepoMobileButton.IsVisible = false;
            return;
        }

        var expanded = Model.Expanded;
        if (CatalogReviewDetailsHeroExpanded != null)
            CatalogReviewDetailsHeroExpanded.IsVisible = expanded;
        if (CatalogReviewDetailsAboutButton != null)
            CatalogReviewDetailsAboutButton.Content = expanded ? "Hide" : "About";
        if (CatalogReviewDetailsOpenRepoMobileButton != null)
        {
            var row = Model.Row as CatalogSyncRowItem;
            CatalogReviewDetailsOpenRepoMobileButton.IsVisible = expanded && row != null && ShouldShowCatalogReviewOpenRepo(row.Repository);
        }
    }

    internal bool HandleCatalogReviewDetailsGamepadNavigation(Services.NavigationDirection direction)
    {
        var controls = CollectCatalogReviewDetailsControls();
        var headerCount = CountCatalogReviewDetailsHeaderControls();
        var currentIndex = _gamepadNavigation.ClampIndex(FocusIndex, controls.Count);
        var canScrollUp = CatalogReviewDetailsScrollViewer is { Offset.Y: > 1 };
        var nav = CatalogReviewDetailsGamepadLayout.Move(direction, currentIndex, headerCount, controls.Count, BodyFocused, canScrollUp);
        if (nav.LeaveTopBar)
        {
            BodyFocused = false;
            return _host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.TopBar, null));
        }

        if (nav.LeaveSidebar)
        {
            BodyFocused = false;
            return _host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
        }

        if (nav.ScrollDown || nav.ScrollUp)
        {
            BodyFocused = true;
            ScrollCatalogReviewDetailsBody(nav.ScrollDown);
            return true;
        }

        BodyFocused = nav.Region == CatalogReviewDetailsRegion.Body;
        if (BodyFocused)
            return true;
        ApplyCatalogReviewDetailsActionSelection(nav.Index);
        return true;
    }

    internal void ScrollCatalogReviewDetailsBody(bool down)
    {
        var scrollViewer = CatalogReviewDetailsScrollViewer;
        if (scrollViewer == null)
            return;
        const double step = 96;
        var nextY = down ? scrollViewer.Offset.Y + step : Math.Max(0, scrollViewer.Offset.Y - step);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextY);
    }

    internal int CountCatalogReviewDetailsHeaderControls()
    {
        var count = 0;
        foreach (var control in EnumerateCatalogReviewDetailsHeaderControls())
        {
            if (control is { IsVisible: true, IsEnabled: true, IsEffectivelyVisible: true })
                count++;
        }

        return count;
    }

    internal IEnumerable<Control> EnumerateCatalogReviewDetailsHeaderControls()
    {
        if (PlatformCapabilities.IsMobile)
        {
            if (CatalogReviewDetailsAboutButton != null)
                yield return CatalogReviewDetailsAboutButton;
            if (CatalogReviewDetailsCloseMobileButton != null)
                yield return CatalogReviewDetailsCloseMobileButton;
            if (CatalogReviewDetailsOpenRepoMobileButton != null)
                yield return CatalogReviewDetailsOpenRepoMobileButton;
            yield break;
        }

        if (CatalogReviewDetailsOpenRepoButton != null)
            yield return CatalogReviewDetailsOpenRepoButton;
        if (CloseCatalogReviewDetailsButton != null)
            yield return CloseCatalogReviewDetailsButton;
    }

    internal List<Control> CollectCatalogReviewDetailsControls()
    {
        var controls = new List<Control>();
        foreach (var control in EnumerateCatalogReviewDetailsHeaderControls())
        {
            if (control is { IsVisible: true, IsEnabled: true, IsEffectivelyVisible: true })
                controls.Add(control);
        }

        AddVisibleCatalogReviewDetailsActions(controls, CatalogReviewDetailsActions);
        AddVisibleCatalogReviewDetailsActions(controls, CatalogReviewDetailsActionsMobile);
        return controls;
    }

    internal static void AddVisibleCatalogReviewDetailsActions(List<Control> controls, WrapPanel? panel)
    {
        if (panel is not { IsEffectivelyVisible: true })
            return;
        foreach (var button in panel.Children.OfType<Button>())
        {
            if (button is { IsVisible: true, IsEnabled: true, IsEffectivelyVisible: true })
                controls.Add(button);
        }
    }

    internal void ApplyCatalogReviewDetailsActionSelection(int index)
    {
        var controls = CollectCatalogReviewDetailsControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        FocusIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewDetailsOverlay;
        _host.ClearFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
    }

    internal void ActivateCatalogReviewDetailsAction()
    {
        if (BodyFocused)
            return;
        var controls = CollectCatalogReviewDetailsControls();
        var index = _gamepadNavigation.ClampIndex(FocusIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    internal void ClearCatalogReviewDetailsGamepadFocus()
    {
        var controls = CollectCatalogReviewDetailsControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    public bool SynchronizePointer(object? source) => GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectCatalogReviewDetailsControls(), GamepadNavigationZone.CatalogReviewDetailsOverlay, FocusIndex, ApplyCatalogReviewDetailsActionSelection, source);
    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        if (nextZone != GamepadNavigationZone.CatalogReviewDetailsOverlay)
            ClearCatalogReviewDetailsGamepadFocus();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.CatalogReviewDetailsOverlay:
                ApplyCatalogReviewDetailsActionSelection(transition.SelectedIndex ?? 0);
                return true;
            default:
                return false;
        }
    }
}
