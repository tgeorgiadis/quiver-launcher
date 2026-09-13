using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class LibraryView : UserControl
{
    private LauncherSession _session = null!;
    private LibraryLaunchController _libraryLaunch = null!;
    private Action<Control, ContextMenu> _openMenu = (_, _) =>
    {
    };
    private AppSettings _settings => Model.Settings.Current;
    public LibraryViewModel Model { get; private set; } = null!;
    public LibraryNavigation Navigation { get; internal set; } = null!;

    public event Action<LibraryActionKind, GameInfo?>? NavigationRequested;
    private LibraryActions _actions = null!;
    private LibraryCustomizationService _customization = null!;
    private Func<GameInfo, Task> _autoUpdate = null!;
    private Func<string, string, Task> _message = null!;
    public void ConfigureActions(LibraryActions actions, LibraryCustomizationService customization, Func<GameInfo, Task> autoUpdate, Func<string, string, Task> message)
    {
        _actions = actions;
        _customization = customization;
        _autoUpdate = autoUpdate;
        _message = message;
    }

    public LibraryView()
    {
        InitializeComponent();
    }

    public void Configure(LibraryViewModel model, LauncherSession session, LibraryLaunchController launch, Action<Control, ContextMenu> openMenu)
    {
        Model = model;
        DataContext = model;
        _session = session;
        _libraryLaunch = launch;
        _openMenu = openMenu;
        Surface.SizeChanged += (_, _) => FitMobileLibraryCardWidth();
    }

    public void ApplyMobileLayout()
    {
        LibraryItemsHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        LibraryItemsHost.VerticalAlignment = VerticalAlignment.Top;
        LibraryItemsHost.Margin = new Thickness(0);
        ClassicGridViewControl.VerticalAlignment = VerticalAlignment.Top;
        CompactGridViewControl.VerticalAlignment = VerticalAlignment.Top;
        LibraryContentPanel.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        FitMobileLibraryCardWidth();
    }

    public void ApplyContentInsets(Thickness margin)
    {
        LibraryContentPanel.Margin = margin;
        EmptyLibraryPanel.Margin = new Thickness(16);
        LibrarySearchNoMatchesPanel.Margin = new Thickness(16);
    }

    public void UpdateEmptyState(bool showLibrary)
    {
        var empty = showLibrary && Model.IsLibraryEmpty;
        var noMatches = showLibrary && Model.HasNoSearchMatches;
        EmptyLibraryPanel.IsVisible = empty;
        LibrarySearchNoMatchesPanel.IsVisible = noMatches;
        LibraryContentPanel.IsVisible = showLibrary && !empty && !noMatches;
    }

    public void UpdateLayoutMode()
    {
        ClassicGridViewControl.IsVisible = _settings.UseGridView && !_settings.GridCompactCards;
        CompactGridViewControl.IsVisible = _settings.UseGridView && _settings.GridCompactCards;
    }

    private void OpenContextMenu(Control anchor, ContextMenu menu)
    {
        if (!_session.IsClosed)
            _openMenu(anchor, menu);
    }

    private async void Request(object? sender, LibraryActionKind action)
    {
        if (_session.IsClosed || sender is not Control control)
            return;
        var game = (control as MenuItem)?.CommandParameter as GameInfo ?? (control as Button)?.CommandParameter as GameInfo ?? control.DataContext as GameInfo;
        await _session.RunAsync(async () =>
        {
            var anchor = game == null ? null : ResolveDownloadMenuAnchor(game, FindGameMenuAnchor(game));
            switch (action)
            {
                case LibraryActionKind.AddToSteam:
                    await _actions.AddToSteamAsync(game);
                    break;
                case LibraryActionKind.ConfigureWindowsRunner:
                    await _actions.ConfigureRunnerAsync(game);
                    break;
                case LibraryActionKind.CreateShortcut:
                    await _actions.CreateShortcutAsync(game);
                    break;
                case LibraryActionKind.DeleteGameFromLibrary:
                    await _actions.UninstallAsync(game);
                    break;
                case LibraryActionKind.ForceUpdate:
                    await _actions.ForceUpdateAsync(game);
                    break;
                case LibraryActionKind.LocateExistingInstall:
                    await _actions.LocateInstallAsync(game);
                    break;
                case LibraryActionKind.RemoveGameEntry:
                    await _actions.RemoveEntryAsync(game);
                    break;
                case LibraryActionKind.HideGame:
                    await _customization.ToggleHiddenAsync(game);
                    break;
                case LibraryActionKind.RemoveCustomIcon:
                    await _customization.RemoveIconAsync(game);
                    break;
                case LibraryActionKind.SetCustomIcon:
                    await _customization.SetIconAsync(game);
                    break;
                case LibraryActionKind.OpenGitHubPage:
                    await _actions.OpenRepositoryAsync(game);
                    break;
                case LibraryActionKind.OpenFolder:
                    if (game != null)
                        _actions.OpenGameFolder(game);
                    else
                        await _message("Unable to identify the game folder.", "Action Error");
                    break;
                case LibraryActionKind.AutoUpdateMenu:
                    if (game != null)
                        await _autoUpdate(game);
                    break;
                case LibraryActionKind.LaunchGameMenu:
                    await _libraryLaunch.LaunchFromMenuAsync(game, anchor);
                    break;
                case LibraryActionKind.SelectDifferentExecutable:
                    await _libraryLaunch.SelectExecutableAsync(game, anchor);
                    break;
                case LibraryActionKind.SkipUpdate:
                    if (game != null)
                        await _libraryLaunch.HandleSkipUpdateAsync(game);
                    else
                        await _message("Unable to identify the selected app.", "Error");
                    break;
                case LibraryActionKind.ChangeVersion:
                case LibraryActionKind.UpdateNowMenu:
                    if (game == null)
                        await _message("Unable to identify the selected app.", "Error");
                    else if (anchor == null)
                        await _message($"Unable to open the {(action == LibraryActionKind.ChangeVersion ? "version" : "update")} menu for this game.", "Error");
                    else if (action == LibraryActionKind.ChangeVersion)
                        await _libraryLaunch.HandleChangeVersionAsync(anchor, game);
                    else
                        await _libraryLaunch.HandleUpdateNowAsync(anchor, game);
                    break;
                default:
                    NavigationRequested?.Invoke(action, game);
                    break;
            }
        });
    }

    private void AddToSteam_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.AddToSteam);
    private void AutoUpdateMenu_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.AutoUpdateMenu);
    private void ChangeVersion_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.ChangeVersion);
    private void ConfigureWindowsRunner_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.ConfigureWindowsRunner);
    private void CreateShortcut_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.CreateShortcut);
    private void DeleteGameFromLibrary_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.DeleteGameFromLibrary);
    private void EditCustomDisplayNameMenu_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.EditCustomDisplayNameMenu);
    private void EditGameEntry_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.EditGameEntry);
    private void EditTagsMenu_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.EditTagsMenu);
    private void EmptyLibraryAddApp_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.EmptyLibraryAddApp);
    private void EmptyLibraryAction_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button) Navigation?.SynchronizeEmptyActionFocus(button);
    }
    private void EmptyLibraryBrowseCatalog_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.EmptyLibraryBrowseCatalog);
    private void ForceUpdate_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.ForceUpdate);
    private void HideGame_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.HideGame);
    private void LaunchGameMenu_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.LaunchGameMenu);
    private void LibrarySearchClear_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.LibrarySearchClear);
    private void LocateExistingInstall_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.LocateExistingInstall);
    private void OpenFolder_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.OpenFolder);
    private void OpenGitHubPage_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.OpenGitHubPage);
    private void OpenMods_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.OpenMods);
    private void RemoveCustomIcon_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.RemoveCustomIcon);
    private void RemoveGameEntry_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.RemoveGameEntry);
    private void ReviewCatalogChanges_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.ReviewCatalogChanges);
    private void SelectDifferentExecutable_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.SelectDifferentExecutable);
    private void SetCustomIcon_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.SetCustomIcon);
    private void ShowChangelog_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.ShowChangelog);
    private void ShowReadme_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.ShowReadme);
    private void SkipUpdate_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.SkipUpdate);
    private void UpdateNowMenu_Click(object? sender, RoutedEventArgs e) => Request(sender, LibraryActionKind.UpdateNowMenu);
    internal bool _suppressNextGameCardTap;
    internal bool _gameCardGestureConsumed;
    internal Control? _gameCardPressControl;
    internal Point _gameCardPressOrigin;
    internal DispatcherTimer? _gameCardHoldTimer;
    internal int _mobileGridColumns;
    internal double GetMobileLibraryViewportWidth()
    {
        if (Surface is { Bounds.Width: > 1 } container)
        {
            var width = container.Bounds.Width;
            if (LibraryContentPanel != null)
                width -= LibraryContentPanel.Margin.Left + LibraryContentPanel.Margin.Right;
            return width;
        }

        if (LibraryContentPanel is { Bounds.Width: > 1 } scroller)
            return scroller.Bounds.Width;
        var fallback = Bounds.Width - Padding.Left - Padding.Right;
        if (LibraryContentPanel != null)
            fallback -= LibraryContentPanel.Margin.Left + LibraryContentPanel.Margin.Right;
        return fallback;
    }

    internal void FitMobileLibraryCardWidth()
    {
        if (!PlatformCapabilities.IsMobile)
            return;
        var width = GetMobileLibraryViewportWidth();
        if (width <= 1 || double.IsNaN(width) || double.IsInfinity(width))
            return;
        // Mobile card Margin=4 on each side, so each card occupies width + 8.
        const double gutter = 8;
        var minCard = Math.Max(120, _settings?.SlotSize ?? 180);
        var columns = Math.Max(1, (int)Math.Floor(width / (minCard + gutter)));
        if (columns < 1)
            return;
        ApplyMobileLibraryItemsPanel(columns);
    }

    internal void ApplyMobileLibraryItemsPanel(int columns)
    {
        if (_mobileGridColumns == columns)
            return;
        _mobileGridColumns = columns;
        ITemplate<Panel?> template = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = columns, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top });
        if (ClassicGridViewControl != null)
            ClassicGridViewControl.ItemsPanel = template;
        if (CompactGridViewControl != null)
            CompactGridViewControl.ItemsPanel = template;
    }

    internal async void GameButton_Click(object sender, RoutedEventArgs e)
    {
        await _session.RunAsync(async () =>
        {
            if (sender is Button button && button.DataContext is GameInfo game)
                await _libraryLaunch.PerformGamePrimaryActionAsync(game, button);
        });
    }

    internal Control? FindGameMenuAnchor(GameInfo game)
    {
        return this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => ReferenceEquals(button.DataContext, game) || ReferenceEquals(button.Tag, game));
    }

    internal void GameCard_Tapped(object? sender, TappedEventArgs e)
    {
        CancelMobileGameCardHoldTimer();
        if (sender is not Control card)
            return;
        if (TryConsumeMobileGameCardTap(card, e.Source))
            e.Handled = true;
    }

    internal void GameCard_Holding(object? sender, HoldingRoutedEventArgs e)
    {
        if (!PlatformCapabilities.IsMobile || e.HoldingState != HoldingState.Started)
            return;
        if (TryOpenMobileGameCardMenu(sender as Control, e.Source))
            e.Handled = true;
    }

    internal void GameCard_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (TryOpenMobileGameCardMenu(sender as Control, e.Source) || TryOpenGameCardContextMenu(sender as Control))
        {
            _suppressNextGameCardTap = true;
            e.Handled = true;
        }
    }

    internal void GameCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is InputElement input)
            input.PerformFeedback(FeedbackAction.Click);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (TryOpenGameCardContextMenu(sender as Control))
                e.Handled = true;
            return;
        }

        if (!PlatformCapabilities.IsMobile || sender is not Control card)
            return;
        if (IsTapOnNestedButton(card, e.Source))
            return;
        BeginMobileGameCardPress(card, e);
    }

    internal void GameCard_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_gameCardHoldTimer == null || sender is not Control card)
            return;
        if (IsBeyondTapSlop(e.GetPosition(card) - _gameCardPressOrigin))
            CancelMobileGameCardHoldTimer();
    }

    internal void GameCard_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var holdTimerWasRunning = _gameCardHoldTimer != null;
        CancelMobileGameCardHoldTimer();
        if (!PlatformCapabilities.IsMobile || sender is not Control card)
            return;
        if (!holdTimerWasRunning && _gameCardGestureConsumed)
            return;
        if (IsBeyondTapSlop(e.GetPosition(card) - _gameCardPressOrigin))
            return;
        if (TryConsumeMobileGameCardTap(card, e.Source))
            e.Handled = true;
    }

    internal void GameCard_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CancelMobileGameCardHoldTimer();
        _gameCardPressControl = null;
    }

    internal void BeginMobileGameCardPress(Control card, PointerEventArgs e)
    {
        CancelMobileGameCardHoldTimer();
        _gameCardGestureConsumed = false;
        _suppressNextGameCardTap = false;
        _gameCardPressControl = card;
        _gameCardPressOrigin = e.GetPosition(card);
        _gameCardHoldTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _gameCardHoldTimer.Tick += GameCardHoldTimer_Tick;
        _gameCardHoldTimer.Start();
    }

    internal void GameCardHoldTimer_Tick(object? sender, EventArgs e)
    {
        CancelMobileGameCardHoldTimer();
        TryOpenMobileGameCardMenu(_gameCardPressControl, _gameCardPressControl);
    }

    internal void CancelMobileGameCardHoldTimer()
    {
        if (_gameCardHoldTimer == null)
            return;
        _gameCardHoldTimer.Tick -= GameCardHoldTimer_Tick;
        _gameCardHoldTimer.Stop();
        _gameCardHoldTimer = null;
    }

    internal bool TryOpenMobileGameCardMenu(Control? card, object? source)
    {
        if (!PlatformCapabilities.IsMobile || card == null || _gameCardGestureConsumed)
            return false;
        if (IsTapOnNestedButton(card, source))
            return false;
        if (!TryOpenGameCardContextMenu(card))
            return false;
        _gameCardGestureConsumed = true;
        _suppressNextGameCardTap = true;
        return true;
    }

    internal bool TryConsumeMobileGameCardTap(Control card, object? source)
    {
        if (!PlatformCapabilities.IsMobile)
            return false;
        if (_gameCardGestureConsumed || _suppressNextGameCardTap)
        {
            _suppressNextGameCardTap = false;
            _gameCardGestureConsumed = true;
            return false;
        }

        if (IsTapOnNestedButton(card, source) || card.DataContext is not GameInfo game)
            return false;
        _gameCardGestureConsumed = true;
        _ = _session.RunAsync(() => _libraryLaunch.PerformGamePrimaryActionAsync(game, card));
        return true;
    }

    internal static bool IsBeyondTapSlop(Point delta) => Math.Abs(delta.X) > 16 || Math.Abs(delta.Y) > 16;
    internal bool TryOpenGameCardContextMenu(Control? card)
    {
        if (card == null)
            return false;
        Control? placementTarget = null;
        ContextMenu? contextMenu = card.ContextMenu;
        if (contextMenu != null)
        {
            placementTarget = card;
        }
        else
        {
            var optionsButton = card.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.ContextMenu != null);
            contextMenu = optionsButton?.ContextMenu;
            placementTarget = optionsButton;
        }

        if (contextMenu == null || placementTarget == null)
            return false;
        if (contextMenu.IsOpen)
            return true;
        OpenContextMenu(placementTarget, contextMenu);
        return true;
    }

    internal static bool IsTapOnNestedButton(Control card, object? source)
    {
        if (source is not Visual visual)
            return false;
        var button = visual.FindAncestorOfType<Button>(includeSelf: true);
        return button != null && !ReferenceEquals(button, card);
    }

    internal void OptionsButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button?.ContextMenu != null)
            OpenContextMenu(button, button.ContextMenu);
    }

    internal void LibraryCard_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: GameInfo game })
            game.IsHovered = true;
    }

    internal void LibraryCard_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: GameInfo game })
            game.IsHovered = false;
    }

    internal ItemsControl? GetActiveGamesItemsControl()
    {
        if (_settings.UseGridView)
            return _settings.GridCompactCards ? CompactGridViewControl : ClassicGridViewControl;
        return ListViewControl;
    }

    internal Border? FindGameCardBorder(GameInfo game, ItemsControl? itemsControl = null)
    {
        itemsControl ??= GetActiveGamesItemsControl();
        if (itemsControl == null)
            return null;
        return itemsControl.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.DataContext, game) && (b.Classes.Contains("gamecard") || b.Classes.Contains("gamecardgrid") || b.Classes.Contains("gamecardcompact")));
    }

    internal Control? FindGameCardRoot(GameInfo game, ItemsControl? itemsControl = null)
    {
        var border = FindGameCardBorder(game, itemsControl);
        if (border == null)
            return null;
        if (border.Classes.Contains("gamecardgrid") && border.Parent is StackPanel stack && ReferenceEquals(stack.DataContext, game))
        {
            return stack;
        }

        return border;
    }

    internal Button? FindGameActionButton(GameInfo game) => GetActiveGamesItemsControl()?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.DataContext, game) && (b.Classes.Contains("modern") || b.Classes.Contains("modern-icon")));
    internal Button? FindGameOptionsButton(GameInfo game) => GetActiveGamesItemsControl()?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.DataContext, game) && b.ContextMenu != null);
    /// <summary>
    /// Prefer a stable placement target for download/executable menus. The action button
    /// mutates during PerformActionAsync and can orphan X11 popups under Gamescope.
    /// </summary>
    internal Control? ResolveDownloadMenuAnchor(GameInfo game, Control? fallback = null) => FindGameOptionsButton(game) as Control ?? FindGameCardBorder(game) ?? FindGameActionButton(game) ?? fallback ?? FindGameMenuAnchor(game);
}
