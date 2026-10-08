using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace QuiverLauncher
{
    public enum MainViewMode
    {
        Library,
        Browse,
    }

    public partial class MainView : UserControl, INotifyPropertyChanged, IUpdatePresentation, IAppUpdateReviewActions, IModsFeatureHost, ISettingsFeatureHost
    {
        public ShellViewModel Shell { get; } = new();
        public LibraryViewModel Library { get; private set; } = null!;

        private readonly ThemeEditor _themeEditor;
        private readonly MarkdownRenderer _markdownRenderer;
        private readonly GameManager _gameManager;
        private readonly LibraryPersistenceService _libraryPersistence;
        private readonly LibraryActions _libraryActions;
        private readonly LibraryCustomizationService _libraryCustomization;
        private readonly Views.LibraryLaunchController _libraryLaunch;
        private readonly SettingsViewModel _settingsViewModel;
        private readonly LauncherSession _session = new();
        private readonly Views.MobileShellLayout _mobileLayout;
        public bool CanOpenMobileNavigation => _mobileLayout?.CanOpenNavigation == true;
        private readonly Views.DesktopHeaderLayout? _desktopHeader;
        private Views.DesktopSidebarController? _sidebarController;
        private readonly Views.ShellAppearance _appearance;
        private readonly Views.ShellChromeNavigation _chromeNavigation;
        private readonly ShellNavigationRouter _navigationRouter;
        private readonly bool _initializeOnOpen;
        private readonly LauncherDialogLifetime _dialogs;
        private readonly LauncherPromptService _prompts;
        private readonly LauncherMenuController _menus;
        private readonly UpdateCheckCoordinator _updateChecks;
        private readonly LauncherUpdateWorkflow _updates;
        private readonly BackgroundUpdateScheduler _backgroundUpdates;
        private readonly LauncherMusicService _music;
        private readonly LauncherForegroundController _foreground;
        private DesktopHostController? _desktopHost;
        private readonly HashSet<GameInfo> _subscribedGames = [];
        public ObservableCollection<GameInfo> Games => _gameManager?.Games ?? new ObservableCollection<GameInfo>();
        public ObservableCollection<GameInfo> AppUpdateReviewRows => AppUpdatesReviewPanel.Model.Rows;
        public bool GamepadHintsVisible => IsDesktopPlatform && !Shell.SettingsOpen && !Shell.EntryEditorOpen && !Shell.TagEditorOpen && !IsDisplayFilterOverlayOpen && !Shell.ModsOpen && !Shell.ModDetailsOpen && !Shell.BrowseDetailsOpen;
        /// <summary>
        /// Gamepad chrome actions (zones, overlays, library confirm) when pad input is enabled,
        /// or after keyboard navigation/actions have activated keyboard chrome.
        /// </summary>
        private bool AllowChromeActions => _settings.EnableGamepadInput || GamepadFocusChrome.KeyboardNavigationActive;

        private readonly VelopackUpdateService _velopackUpdateService = new();
        private readonly LibraryAddService _libraryAdd;
        public AppSettings _settings { get => _settingsViewModel.Current; private set => _settingsViewModel.ReplaceCurrent(value); }

        public App _app = null!;
        public SettingsViewModel SettingsModel => _settingsViewModel;
        public AndroidLauncherUpdater? AndroidUpdates => AndroidLauncherUpdater.Current;
        public IBrush WindowBackground => this.Resources["ThemeDarker"] as IBrush ?? Brushes.Transparent;
        public bool IsDesktopPlatform => !PlatformCapabilities.IsMobile;
        public bool KioskLocked => _settingsViewModel.KioskLocked;
        public bool ShowSettingsButton => !KioskLocked;
        public bool ShowCatalogNavigation => !KioskLocked;
        public bool ShowExternalLinks => !KioskLocked;
        public bool ShowUpdateCheckButton => !KioskLocked;
        public bool ShowWindowChromeButtons => IsDesktopPlatform && !KioskLocked;
        public bool ShowMinimizeButton => ShowWindowChromeButtons && !SteamDeckEnvironment.IsGamingMode();
        public string MaximizeButtonTip => GetHostWindowState()is WindowState.Maximized or WindowState.FullScreen ? "Restore" : "Maximize";
        public bool IsMobile => PlatformCapabilities.IsMobile;

        private readonly LauncherInputController _input;
        private InputService? _inputService => _input?.Service;

        private readonly GamepadNavigationService _gamepadNavigation = new();
        private bool _handlingGamepadConfirm = false;
        private bool _hasInitializedFocus = false;
        private bool _closing;
        internal Window? HostWindow => _desktopHost?.Window;

        internal void AttachDesktopHost(DesktopHostController host) => _desktopHost = host;
        internal void NotifyHostWindowStateChanged() => OnPropertyChanged(nameof(MaximizeButtonTip));
        internal void DismissInputForHost() => DismissTextInputFocus();
        internal void RefreshHostUpdateStatus() => _updates.RefreshUpdateCheckStatus();
        internal void PrepareForHostClose()
        {
            _backgroundUpdates.Stop();
            StopLauncherMusic();
        }

        private void ToggleHostMaximized() => _desktopHost?.ToggleMaximized();
        private void CloseAfterLaunchIfNeeded(bool launched) => _desktopHost?.CloseAfterLaunch(launched);
        public void HideToTray() => _desktopHost?.HideToTray();
        public void RestoreFromTray() => _desktopHost?.RestoreFromTray();
        public void RequestExit() => _desktopHost?.RequestExit();
        public void HandleClosing(WindowClosingEventArgs e) => _desktopHost?.HandleClosing(e);
        public void HandleHostWindowStateChanged(WindowState oldState, WindowState newState) => _desktopHost?.HandleStateChanged(newState);
        private bool IsHostActive => HostWindow?.IsActive ?? true;

        private WindowState GetHostWindowState() => HostWindow?.WindowState ?? WindowState.Normal;
        private void SetHostWindowState(WindowState state)
        {
            if (HostWindow != null)
                HostWindow.WindowState = state;
        }

        private void CloseHost() => HostWindow?.Close();
        private Avalonia.Platform.Storage.IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new InvalidOperationException("Storage provider is not available.");

        public MainView() : this(new MainViewDependencies())
        {
        }

        public MainView(MainViewDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            _initializeOnOpen = dependencies.InitializeOnOpen;
            _dialogs = new LauncherDialogLifetime(_session.Token, action => Dispatcher.UIThread.Post(action));
            _session.OnShutdown(_dialogs.Dispose);
            _settingsViewModel = new SettingsViewModel(dependencies.SettingsStore);
            _music = new LauncherMusicService(action => Dispatcher.UIThread.Post(action), dependencies.EnableMusic);
            _foreground = new LauncherForegroundController(_session, _music, () => _inputService, () => _settings, () => IsHostActive, DismissTextInputFocus, RestoreForegroundFocus, () => Library.RefreshManualStatusesAsync(_session.Token), LogGamepadDebug, action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
            _menus = new LauncherMenuController(this, () => LibraryPanel.Navigation.PreserveLibraryGamepadFocusWhileOpeningMenu(), () => LibraryPanel.Navigation.RestoreLibraryGamepadFocusAfterMenu());
            _markdownRenderer = new MarkdownRenderer(OpenUrl, () => Resources);
            _themeEditor = new ThemeEditor(secondary => secondary ? Shell.SecondaryColorBrush.Color : Shell.ThemeColorBrush.Color, ApplyThemeColor, _dialogs);
            InitializeComponent();
            if (IsDesktopPlatform)
            {
                HeaderTitleColumn.ColumnDefinitions = new ColumnDefinitions("*,Auto");
                HeaderTitleColumn.ClipToBounds = true;
                _desktopHeader = new Views.DesktopHeaderLayout(HeaderLayoutGrid, HeaderTitleColumn, DesktopInlineTopBar, HeaderFixedActions,
                    () => LibraryToolbar.IsVisible ? LibraryToolbar.PreferredWidth : 0);
                _session.OnShutdown(_desktopHeader.Dispose);
                LibraryToolbar.PreferredSizeChanged += _desktopHeader.Refresh;
                _session.OnShutdown(() => LibraryToolbar.PreferredSizeChanged -= _desktopHeader.Refresh);
            }
            MessagePromptOverlay.Configure(_session);
            _prompts = new LauncherPromptService(_session, _dialogs, MessagePromptOverlay);
            _mobileLayout = new Views.MobileShellLayout(this, _session, () => _appearance?.ApplyHeader());
            _appearance = new Views.ShellAppearance(this, Shell, _mobileLayout, () => _desktopHeader?.Refresh());
            _session.OnShutdown(_mobileLayout.Dispose);
            _chromeNavigation = new Views.ShellChromeNavigation(this, _session, this, Shell, () => _mobileLayout.IsSearchOpen, WireChromeXyFocusEdges);
            if (IsDesktopPlatform)
            {
                _sidebarController = new Views.DesktopSidebarController(MainSplitView, SidebarPanel, DesktopSidebarToggleButton,
                    _settingsViewModel, () =>
                    {
                        if (IsGamepadFocusActive) _chromeNavigation.ApplyTopBarGamepadSelection(0);
                        else DesktopSidebarToggleButton.Focus();
                        WireChromeXyFocusEdges();
                    });
                _session.OnShutdown(_sidebarController.Dispose);
            }
            _navigationRouter = new ShellNavigationRouter(Shell, _gamepadNavigation, new Dictionary<GamepadNavigationZone, Func<IFeatureNavigationHandler>> { [GamepadNavigationZone.Sidebar] = () => _chromeNavigation, [GamepadNavigationZone.TopBar] = () => _chromeNavigation, [GamepadNavigationZone.AnnouncementBanner] = () => Banners, [GamepadNavigationZone.Library] = () => LibraryPanel.Navigation, [GamepadNavigationZone.BrowseGrid] = () => BrowsePanel.Navigation, [GamepadNavigationZone.BrowseToolbar] = () => BrowsePanel.Navigation, [GamepadNavigationZone.BrowseFilters] = () => BrowsePanel.Navigation, [GamepadNavigationZone.BrowseDetailsOverlay] = () => BrowseDetailsPanel, [GamepadNavigationZone.AppUpdatesReviewToolbar] = () => AppUpdatesReviewPanel, [GamepadNavigationZone.AppUpdatesReviewList] = () => AppUpdatesReviewPanel, [GamepadNavigationZone.AppUpdatesReviewRowActions] = () => AppUpdatesReviewPanel, [GamepadNavigationZone.ModsOverlayToolbar] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlayFilters] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlaySourceFilters] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlayList] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlayRowActions] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsDetailsOverlay] = () => ModsPanel.Details, [GamepadNavigationZone.DisplayFilterOverlay] = () => DisplayFilterOverlay.Navigation, [GamepadNavigationZone.EntryFormOverlay] = () => EntryFormOverlay.Navigation, [GamepadNavigationZone.TagEditOverlay] = () => TagEditOverlay.Navigation, [GamepadNavigationZone.Settings] = () => SettingsPanel.Navigation, }, () => IsDisplayFilterOverlayOpen, () =>
            {
                _chromeNavigation.ClearSidebarGamepadFocus();
                _chromeNavigation.ClearTopBarGamepadFocus();
                Banners.ClearAnnouncementBannerGamepadFocus();
            }, SelectInitialGamepadItemForCurrentView);
            Shell.PropertyChanged += OnShellPropertyChanged;
            _session.OnShutdown(() => Shell.PropertyChanged -= OnShellPropertyChanged);
            ModDetailsPanel.Content = ModsPanel.Details;
            AppUpdatesReviewPanel.Model.Configure(this);
            AppUpdatesReviewPanel.NavigationHost = this;
            AppUpdatesReviewPanel.IsActive = () => !_session.IsClosed;
            _settingsViewModel.PropertyChanged += OnKioskLockChanged;
            _session.OnShutdown(() => _settingsViewModel.PropertyChanged -= OnKioskLockChanged);
            try
            {
                _settings = _settingsViewModel.Load();
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to load settings: {ex.Message}", "Settings Error"));
                _settings = new AppSettings();
            }

            _gameManager = dependencies.GameManager ?? new GameManager(dependencies.SettingsStore);
            Banners.Configure(_session, _settingsViewModel, _gameManager.HttpClient, this, OpenGitHubApiTokenSettings);
            Library = new LibraryViewModel(_gameManager, _settingsViewModel);
            if (_initializeOnOpen) Library.BeginInitialLoad();
            LibraryToolbar.Configure(Library, _session, OnSettingChanged);
            ApplyKioskChrome(refreshShell: false);
            LibraryToolbar.AddRequested += () => ShowEntryFormOverlay(forCreate: true);
            LibraryToolbar.SearchChanged += () =>
            {
                if (_session.IsClosed)
                    return;
                UpdateLibraryEmptyState();
                _mobileLayout.ApplyMobileSearchChrome();
                if (LibraryToolbar.ShouldRestoreSearchFocus(_gamepadNavigation, _chromeNavigation.CollectTopBarControls()))
                    RestoreLibrarySearchGamepadFocus();
                else if (ShouldKeepLibraryChromeFocus())
                    LibraryPanel.Navigation.ClearLibraryCardGamepadFocus();
            };
            _updates = new LauncherUpdateWorkflow(_gameManager, _settingsViewModel, Library, Shell, _session, _prompts, this, () => _app, _velopackUpdateService, () => ModsPanel.Workspace.RefreshAllModUpdateBadgesAsync(), game => _libraryLaunch.HandleUpdateNowAsync(this, game, preferAutoPlatform: true, allowAssetPicker: false, interactive: false));
            _updateChecks = new UpdateCheckCoordinator(_updates);
            _updateChecks.ProgressChanged += () =>
            {
                if (_session.IsClosed) return;
                Shell.UpdateCheckStatus = $"Checking installed apps · {_updateChecks.Progress.Completed} of {_updateChecks.Progress.Total}";
                _updates.RefreshUpdateCheckStatus();
            };
            _backgroundUpdates = new BackgroundUpdateScheduler(_session, () => _updateChecks.CheckAsync(false, false, _session.Token));
            _session.OnShutdown(_backgroundUpdates.Dispose);
            _updateChecks.CheckingChanged += () =>
            {
                if (!_session.IsClosed)
                {
                    if (!_updateChecks.IsChecking && UpdateCheckStatus.GetVisualDescendants().OfType<Button>().Any(b => b.IsFocused))
                        CheckForUpdatesButton.Focus();
                    Shell.IsCheckingUpdates = _updateChecks.IsChecking;
                }
            };
            _session.OnShutdown(Library.Dispose);
            _libraryCustomization = new LibraryCustomizationService(_gameManager, Library, _session, () => StorageProvider, (message, title, question) => ShowMessageBoxAsync(message, title, question));
            _libraryPersistence = new LibraryPersistenceService(_gameManager);
            _libraryActions = new LibraryActions(_gameManager, _libraryPersistence, _settingsViewModel, _session, Library, () => StorageProvider, (message, title, question, cancel) => ShowMessageBoxAsync(message, title, question, cancel), OpenUrl, () =>
            {
                RefreshBrowseLibraryState();
                return Task.CompletedTask;
            });
            _libraryLaunch = new Views.LibraryLaunchController(_gameManager, _settingsViewModel, _session, _libraryPersistence, LibraryPanel.ResolveDownloadMenuAnchor, _menus.Open, (message, title) => ShowMessageBoxAsync(message, title), _libraryActions.OpenGameFolder, () =>
            {
                ApplySorting();
                Library.RefreshContinue();
                if (Shell.AppUpdatesOpen)
                    CloseAppUpdatesReviewIfEmpty();
            }, launched =>
            {
                if (!_session.IsClosed)
                    CloseAfterLaunchIfNeeded(launched);
            }, () => Bounds.Width);
            LibraryPanel.Configure(Library, _session, _libraryLaunch, _menus.Open);
            LibraryPanel.Navigation = new Views.LibraryNavigation(LibraryPanel, this, _session, () => Shell.Mode == MainViewMode.Library && !Shell.AppUpdatesOpen && !Shell.ModsOpen, () => !_session.IsClosed && !Shell.SettingsOpen && !Shell.EntryEditorOpen && !Shell.TagEditorOpen && !IsDisplayFilterOverlayOpen, ShouldKeepLibraryChromeFocus, steal =>
            {
                _chromeNavigation.ClearSidebarGamepadFocus(steal);
                _chromeNavigation.ClearTopBarGamepadFocus(steal);
            }, _libraryLaunch.PerformSelectedGameActionAsync);
            _libraryLaunch.FocusRestorationRequested = LibraryPanel.Navigation.RestoreLibraryGamepadFocusAfterMenu;
            LibraryFiltersPanel.Configure(new LibraryFiltersViewModel(_gameManager, Library), _session, (message, title) => ShowMessageBoxAsync(message, title));
            LibraryFiltersPanel.EditRequested += ShowDisplayFilterOverlay;
            LibraryFiltersPanel.FocusRestorationRequested += _chromeNavigation.RestoreSidebarDisplayFilterFocus;
            DisplayFilterOverlay.Configure(_settingsViewModel, _session, this, (message, title) => ShowMessageBoxAsync(message, title), DismissTextInputFocus);
            DisplayFilterOverlay.CloseRequested += CloseDisplayFilterOverlay;
            DisplayFilterOverlay.Saved += isEdit =>
            {
                Library.RefreshFilters();
                LibraryFiltersPanel.RefreshSidebarFilterSelection();
                if (isEdit)
                {
                    _gameManager.ApplyTagDisplayFilter(_settings);
                    ApplySorting();
                }
            };
            LibraryPanel.ConfigureActions(_libraryActions, _libraryCustomization, ToggleAppAutoUpdateAsync, (message, title) => ShowMessageBoxAsync(message, title));
            LibraryPanel.NavigationRequested += (action, game) => _ = _session.RunAsync(() => HandleLibraryNavigationRequestAsync(action, game));
            ModsPanel.Configure(new ModsFeatureContext(_gameManager, () => _settings, _settingsViewModel, _session, _markdownRenderer, Shell), this);
            var catalog = _catalogClient = new QuiverCatalogClient(_gameManager.HttpClient);
            _libraryAdd = new LibraryAddService(_gameManager, _settingsViewModel, _session);
            BrowsePanel.Configure(new BrowseViewModel(catalog, () => _gameManager.LibraryApps, () => _settings.CustomAppListLocation,
                location => _gameManager.CatalogService.TryLoadListAsync(_gameManager.HttpClient, location, _session.Token)),
                _session, this, () => !Shell.SettingsOpen && !Shell.BrowseDetailsOpen && Shell.Mode == MainViewMode.Browse);
            BrowsePanel.DetailsRequested += OpenBrowseDetails;
            BrowsePanel.AddRequested += item => _ = _session.RunAsync(() => AddCardAsync(item));
            BrowsePanel.OpenInLibraryRequested += OpenInLibrary;
            BrowsePanel.Model.HideLibraryApps = _settings.CatalogHideLibraryApps;
            BrowsePanel.HideLibraryChosen += hide =>
            {
                _settings.CatalogHideLibraryApps = hide;
                _settingsViewModel.Save(_settings);
            };
            BrowsePanel.GameRequested += OpenBrowseGame;
            BrowsePanel.Model.Ai = BrowseText.AiFilters.Any(f => f.Id != null && f.Id == _settings.CatalogAiFilter) ? _settings.CatalogAiFilter : null;
            BrowsePanel.AiFilterChosen += ai =>
            {
                _settings.CatalogAiFilter = ai ?? "";
                _settingsViewModel.Save(_settings);
            };
            BrowseDetailsPanel.Configure(_session, this, _markdownRenderer, new BrowseDetailsViewModel(catalog,
                (app, project) => QuiverCatalogMapping.ToGameInfo(_gameManager.CatalogService, app, project), LoadRepositoryReadmeAsync,
                (app, token) => RepositoryReleaseNotes.FetchAsync(app, _settings, _gameManager.HttpClient, token)),
                BrowsePanel.Model.FindInLibrary, BrowsePanel.Model);
            BrowseDetailsPanel.CloseRequested += () => CloseBrowseDetails();
            BrowseDetailsPanel.AddRequested += app => _ = _session.RunAsync(() => AddFromBrowseAsync(app));
            BrowseDetailsPanel.CardAddRequested += item => _ = _session.RunAsync(() => AddCardAsync(item));
            BrowseDetailsPanel.OpenInLibraryRequested += OpenInLibrary;
            BrowseDetailsPanel.RemoveRequested += app => _ = _libraryActions.RemoveEntryAsync(app);
            BrowseDetailsPanel.OpenUrlRequested += OpenUrl;
            SettingsPanel.Configure(new SettingsFeatureContext(() => _settings, _settingsViewModel, _session, _music, _gameManager, () => _inputService), this);
            TagEditOverlay.Configure(_session, this, new LibraryMetadataService(_gameManager, _settingsViewModel), ReloadLibraryAfterEditAsync, message => ShowMessageBoxAsync(message, "Error"), DismissTextInputFocus);
            TagEditOverlay.CloseRequested += CloseTagEditOverlay;
            EntryFormOverlay.Configure(_session, this, new AppEntryService(_gameManager, _settingsViewModel), ReloadLibraryAfterEditAsync, (message, title) => ShowMessageBoxAsync(message, title), _libraryActions.OpenGameFolder, DismissTextInputFocus);
            EntryFormOverlay.CloseRequested += CloseEntryFormOverlay;
            EntryFormOverlay.EntryCreated += RevealManuallyAddedApp;
            if (dependencies.GameManager == null)
                _session.OnShutdown(_gameManager.Dispose);
            _session.OnShutdown(new LauncherUiDispatch(_gameManager, _session, action => Dispatcher.UIThread.InvokeAsync(action).GetTask()).Dispose);
            // Initialize theme
            Shell.ThemeColorBrush = new SolidColorBrush(Color.Parse(_settings?.PrimaryColor ?? "#18181b"));
            Shell.SecondaryColorBrush = new SolidColorBrush(Color.Parse(_settings?.SecondaryColor ?? "#404040"));
            UpdateThemeColors();
            _settings.EnsureInitialized();
            StartUsageData();
            if (_settings.FirstStartup)
            {
                _settingsViewModel.ApplyCardLayout(CardLayoutPreset.Square, persist: false);
                _settings.FirstStartup = false;
                _settingsViewModel.Save(_settings);
            }

            LoadCurrentVersion();
            UpdateSettingsUI();
            // Apply fullscreen from settings immediately
            // Desktop MainWindow applies StartFullscreen after this view is hosted.
            // Initialize background image from settings
            Shell.RefreshPresentation(_settings);
            // Initialize music from settings
            _music.Path = _settings.LauncherMusicPath ?? string.Empty;
            _music.Volume = _settings.MusicVolume;
            if (!string.IsNullOrEmpty(_music.Path) && File.Exists(_music.Path))
            {
                PlayLauncherMusic(_music.Path);
            }

            _input = new LauncherInputController(this, () => _settings, SettingsPanel.Bindings, () => IsHostActive, () => MessagePromptOverlay.IsVisible, () => MessagePromptOverlay.Dismiss(), new(HandleGamepadNavigation, HandleConfirmAction, HandleCancelAction, HandleOptionsAction, HandleGamepadConnectionChanged, ActivateKeyboardNavChrome), dependencies.EnableInput);
            _input.OnKioskUnlock = () => _ = ToggleKioskFromChordAsync();
            UpdateGamepadChromeClass();
            UpdateGamepadHintsBar();
            SettingsPanel.Bindings.Refresh();
            SettingsPanel.Bindings.RefreshControllers();
            if (CardGamepadFocusSink != null)
                GamepadCardFocusSink.Configure(CardGamepadFocusSink);
            // Tunnel: handle arrows/confirm before Avalonia focus walker or CheckBox Space toggle.
            AddHandler(InputElement.PointerPressedEvent, MainWindow_PointerPressedForChrome, RoutingStrategies.Tunnel);
            // Steam Deck Gaming Mode: Avalonia TextBoxes do not auto-trigger Steam's OSK.
            _gameManager.PropertyChanged += OnGameManagerPropertyChanged;
            DataContext = this;
            _mobileLayout.ApplyMobileShell();
        }

        /// <summary>Usage data follows the saved choice from the start; the start itself is sent once the player has said yes.</summary>
        private void StartUsageData()
        {
            var telemetry = Telemetry.Current;
            telemetry.SetFolders(_gameManager.GamesFolder, QuiverLauncherPaths.UserDataRoot);
            _settingsViewModel.ApplyUsageData();
            telemetry.TrackStartup(new Dictionary<string, object?>
            {
                ["first_run"] = _settings.FirstStartup,
                ["kiosk"] = _settingsViewModel.KioskLocked,
                ["view"] = _settings.UseGridView ? "grid" : "list",
                ["interface_scale"] = _settings.InterfaceScalePercent,
                ["gamepad_input"] = _settings.EnableGamepadInput,
                ["background_update_checks"] = _settings.BackgroundUpdateCheckEnabled,
                ["close_to_tray"] = _settings.CloseToTray,
                ["custom_theme"] = !string.Equals(_settings.PrimaryColor, "#18181b", StringComparison.OrdinalIgnoreCase),
                ["background_image"] = !string.IsNullOrWhiteSpace(_settings.BackgroundImagePath),
                ["background_music"] = !string.IsNullOrWhiteSpace(_settings.LauncherMusicPath),
                ["github_token"] = !string.IsNullOrWhiteSpace(_settings.GitHubApiToken),
                ["custom_app_list"] = !string.IsNullOrWhiteSpace(_settings.CustomAppListLocation),
                ["preview_updates"] = _settings.AllowPrereleaseLauncherUpdates,
            });
            Shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ShellViewModel.Mode) or nameof(ShellViewModel.SettingsOpen) or nameof(ShellViewModel.AppUpdatesOpen)
                    or nameof(ShellViewModel.BrowseDetailsOpen) or nameof(ShellViewModel.ModsOpen) or nameof(ShellViewModel.ModDetailsOpen))
                    TrackScreen();
            };
        }

        /// <summary>Usage data: how big the library is once it has loaded, never which apps are in it.</summary>
        private void TrackLibraryLoaded()
        {
            var apps = _gameManager.LibraryApps.ToList();
            Telemetry.Current.Track("library_loaded", new Dictionary<string, object?>
            {
                ["apps"] = apps.Count,
                ["installed"] = apps.Count(a => a.Status is GameStatus.Installed or GameStatus.UpdateAvailable),
                ["updates_available"] = apps.Count(a => a.Status == GameStatus.UpdateAvailable),
                ["catalog_apps"] = apps.Count(a => !a.IsManuallyManaged && !string.IsNullOrWhiteSpace(a.CatalogSlug)),
                ["local_apps"] = apps.Count(a => a.IsManuallyManaged),
                ["auto_update"] = apps.Count(a => a.AutoUpdate),
                ["flatpak"] = apps.Count(a => a.IsFlatpak),
            });
        }

        private string? _trackedScreen;
        /// <summary>The page in an app's or a game's details, by its catalog slug.</summary>
        private (string Screen, string? Slug)? _detailsScreen;

        /// <summary>Usage data: the screen shown, when it changes.</summary>
        private void TrackScreen()
        {
            var (screen, slug) = Shell.SettingsOpen ? ("settings", null)
                : Shell.AppUpdatesOpen ? ("app_updates", null)
                : Shell.ModDetailsOpen ? ("mod", null)
                : Shell.ModsOpen ? ("mods", null)
                : Shell.BrowseDetailsOpen && _detailsScreen is { } details ? details
                : Shell.Mode == MainViewMode.Browse ? ("browse", null)
                : ("library", (string?)null);
            var key = $"{screen}:{slug}";
            if (key == _trackedScreen) return;
            _trackedScreen = key;
            var properties = new Dictionary<string, object?> { ["screen"] = screen };
            if (screen is "app" or "game")
                properties["slug"] = slug;
            Telemetry.Current.Track("screen_viewed", properties);
        }

        private async Task ToggleAppAutoUpdateAsync(GameInfo game)
        {
            await _session.RunAsync(async () =>
            {
                game.AutoUpdate = !game.AutoUpdate;
                await _libraryPersistence.SaveAutoUpdateAsync(game);
                if (_session.IsClosed)
                    return;
                _updates.RefreshUpdateCheckStatus();
                _updates.NotifyUpdateCheckUiProperties();
                if (game.AutoUpdate && game.Status == GameStatus.UpdateAvailable)
                    await _updates.ApplyAutoUpdatesAsync(showFailureSummary: true);
            });
        }

        private async Task HandleLibraryNavigationRequestAsync(Views.LibraryActionKind action, GameInfo? game)
        {
            if (KioskLocked && !KioskLock.AllowsLibraryAction(action))
                return;
            switch (action)
            {
                case Views.LibraryActionKind.EmptyLibraryAddApp:
                    ShowEntryFormOverlay(forCreate: true);
                    break;
                case Views.LibraryActionKind.EmptyLibraryBrowseCatalog:
                    ShowBrowseView();
                    break;
                case Views.LibraryActionKind.LibrarySearchClear:
                    LibraryToolbar.ClearSearch();
                    break;
                case Views.LibraryActionKind.EditGameEntry when game != null:
                    ShowEntryFormOverlay(forCreate: false, gameToEdit: game);
                    break;
                case Views.LibraryActionKind.EditTagsMenu when game != null:
                    ShowTagEditOverlay(game, MetadataEditMode.Tags);
                    break;
                case Views.LibraryActionKind.EditCustomDisplayNameMenu when game != null:
                    ShowTagEditOverlay(game, MetadataEditMode.CustomDisplayName);
                    break;
                case Views.LibraryActionKind.OpenMods when game?.CanOpenMods == true:
                    await ModsPanel.OpenModsOverlayAsync(game);
                    break;
                // The app's page: its README is the Overview tab and its changelog the Releases tab.
                case Views.LibraryActionKind.ShowReadme when game != null:
                    await OpenLibraryAppPageAsync(game, releases: false);
                    break;
                case Views.LibraryActionKind.ShowChangelog when game != null:
                    await OpenLibraryAppPageAsync(game, releases: true);
                    break;
            }
        }

        private Task ShowMessageBoxAsync(string message, string title) => _prompts.ShowMessageBoxAsync(message, title);
        internal Task<bool> ShowOverlayPromptAsync(string message, string title, bool isQuestion, bool preferCancelDefault = false) => _session.RunAsync(async () => await MessagePromptOverlay.ShowAsync(message, title, isQuestion, preferCancelDefault) == MessagePromptResult.Yes);
        private Task<bool> ShowMessageBoxAsync(string message, string title, bool isQuestion, bool preferCancelDefault = false) => _prompts.ShowMessageBoxAsync(message, title, isQuestion, preferCancelDefault);
        private Task<MessagePromptResult> ShowChoicePromptAsync(string message, string title) => _prompts.ShowChoicePromptAsync(message, title);
        Task<bool> ISettingsFeatureHost.ShowMessageBoxAsync(string message, string title, bool question) => ShowMessageBoxAsync(message, title, question);
        bool IUpdatePresentation.CanPresentResults => IsVisible && !_session.IsClosed;

        bool IUpdatePresentation.CanShowFailureSummary => IsVisible && GetHostWindowState() != WindowState.Minimized && !_session.IsClosed;

        void IUpdatePresentation.OpenAppUpdatesReview()
        {
            if (!_session.IsClosed)
                OpenAppUpdatesReview();
        }

        void IUpdatePresentation.OpenBrowse()
        {
            if (!_session.IsClosed)
                ShowBrowseView();
        }
        void IUpdatePresentation.UpdateStatusChanged() => _app?.UpdateTrayTooltip(Shell.PendingUpdatesCount, Shell.IsCheckingUpdates);
        private GamepadNavigationZone GetMainContentGamepadZone() => _navigationRouter.MainZone;
        private bool HandleGamepadNavigation(Services.NavigationDirection direction)
        {
            if (_session.IsClosed || _foreground.LaunchedAppOwnsInput || !AllowChromeActions)
                return false;
            LogGamepadDebug("nav", $"dir={direction}");
            if (MessagePromptOverlay.Model.IsOpen)
                return MessagePromptOverlay.Navigate(direction);
            if (GamepadTextInput.IsEditing)
                return true;
            return _navigationRouter.Navigate(direction);
        }

        private void HandleConfirmActionCore()
        {
            LogGamepadDebug("confirm");
            if (MessagePromptOverlay.Confirm())
                return;
            if (AllowChromeActions && GamepadTextInput.IsEditing)
            {
                GamepadTextInput.TryEndEdit();
                return;
            }

            if (_navigationRouter.ConfirmPriorityDetails())
                return;
            if (AllowChromeActions && (_inputService?.TryHandleContextMenuConfirm() == true || _inputService?.TryHandleModalConfirm() == true || _inputService?.TryHandleComboBoxConfirm() == true || _inputService?.TryHandleMenuFlyoutConfirm() == true))
                return;
            if (_navigationRouter.ConfirmFeature(AllowChromeActions, TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
                return;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            switch (focused)
            {
                case MenuItem menu:
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    break;
                case CheckBox check:
                    GamepadControlActivation.ActivateCheckBox(check);
                    break;
                case ToggleButton toggle:
                    toggle.IsChecked = toggle.IsChecked != true;
                    break;
                case Button button:
                    if (button.ContextMenu != null)
                        _menus.Open(button, button.ContextMenu);
                    else
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
            }
        }

        private void HandleCancelAction()
        {
            if (_session.IsClosed)
                return;
            if (SettingsPanel.Bindings.ListeningGamepad.HasValue || SettingsPanel.Bindings.ListeningKeyboard.HasValue)
            {
                SettingsPanel.Bindings.Cancel();
                return;
            }

            if (MessagePromptOverlay.Dismiss())
                return;
            if (Shell.ModDetailsOpen)
            {
                ModsPanel.Details.Cancel();
                return;
            }

            if (Shell.BrowseDetailsOpen)
            {
                BrowseDetailsPanel.Cancel();
                return;
            }

            if (AllowChromeActions && (_inputService?.TryHandleContextMenuCancel() == true || _inputService?.TryHandleModalCancel() == true || _inputService?.TryHandleComboBoxCancel() == true || _inputService?.TryHandleMenuFlyoutCancel() == true))
                return;
            if (AllowChromeActions && GamepadTextInput.TryEndEdit())
                return;
            _navigationRouter.CancelFeature(AllowChromeActions);
        }

        private void UpdateSettingsUI()
        {
            SettingsPanel.UpdateSettingsUI();
            Library.SortBy = _settings.SortBy ?? "Name";
            LibraryToolbar.SelectSort(Library.SortBy);
            UpdateGridLayoutVisibility();
            Shell.ThemeColorBrush = new SolidColorBrush(Color.Parse(_settings.PrimaryColor ?? "#18181b"));
            Shell.SecondaryColorBrush = new SolidColorBrush(Color.Parse(_settings.SecondaryColor ?? "#404040"));
            UpdateThemeColors();
            UpdateGamepadHintsBar();
            Library.RefreshFilters();
            LibraryFiltersPanel.RefreshSidebarFilterSelection();
        }

        void ISettingsFeatureHost.NotifyPresentationChanged()
        {
            Shell.RefreshPresentation(_settings);
            _music.Path = _settings.LauncherMusicPath ?? string.Empty;
            _music.Volume = _settings.MusicVolume;
            UpdateThemeColors();
            OnPropertyChanged(nameof(WindowBackground));
        }

        void ISettingsFeatureHost.ApplyTopBanner() => Banners.ApplyTopBanner();
        void ISettingsFeatureHost.CloseSettings() => CloseSettingsPanel();
        void ISettingsFeatureHost.EditTheme(bool secondary) => _ = _session.RunAsync(() => secondary ? _themeEditor.ShowSecondaryAsync() : _themeEditor.ShowPrimaryAsync());
        void ISettingsFeatureHost.UpdateGridLayoutVisibility() => UpdateGridLayoutVisibility();
        void ISettingsFeatureHost.ApplyLibraryDisplaySettingsToGames() => Library.ApplyDisplaySettings();
        void ISettingsFeatureHost.FitMobileLibraryCardWidth() => LibraryPanel.FitMobileLibraryCardWidth();
        void ISettingsFeatureHost.ApplySorting() => ApplySorting();
        void ISettingsFeatureHost.CustomAppListChanged()
        {
            BrowsePanel.Model.ForgetCustomList();
            if (Shell.Mode == MainViewMode.Browse) BrowsePanel.Reload();
        }
        void ISettingsFeatureHost.ApplyTrayAndBackgroundUpdateSettings() => ApplyTrayAndBackgroundUpdateSettings();
        void ISettingsFeatureHost.UpdateGamepadHintsBar() => UpdateGamepadHintsBar();
        void ISettingsFeatureHost.UpdateGamepadChromeClass() => UpdateGamepadChromeClass();
        void ISettingsFeatureHost.SelectInitialGamepadItemForCurrentView() => SelectInitialGamepadItemForCurrentView();
        void ISettingsFeatureHost.NotifyGamepadUiChanged() => NotifyGamepadUiChanged();
        void ISettingsFeatureHost.ClearGamepadFocus() => ClearGamepadFocus();
        void ISettingsFeatureHost.SetIgnoreArticlesWhenSorting(bool enabled) => SetIgnoreArticlesWhenSorting(enabled);
        void ISettingsFeatureHost.OpenUrl(string url) => OpenUrl(url);
        void ISettingsFeatureHost.OpenMenu(Control anchor, ContextMenu menu) => _menus.Open(anchor, menu);
        private void UpdateGameCollectionUi()
        {
            if (_session.IsClosed)
                return;
            OnPropertyChanged(nameof(Games));
            Library.RefreshContinue();
            _updates.RefreshUpdateCheckStatus();
            UpdateLibraryEmptyState();
            LibraryPanel.Navigation.SyncGamepadLibrarySelection();
            foreach (var game in _gameManager.Games)
                SubscribeToGameEvents(game);
        }

        private async Task ReloadLibraryAfterEditAsync()
        {
            await _gameManager.ReloadLibraryFromDiskAsync(allowNetwork: false);
            if (!_session.IsClosed)
                ApplySorting();
        }

        private void OnKioskLockChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.KioskLocked))
                ApplyKioskChrome(refreshShell: true);
        }

        private void ApplyKioskChrome(bool refreshShell)
        {
            OnPropertyChanged(nameof(KioskLocked));
            OnPropertyChanged(nameof(ShowSettingsButton));
            OnPropertyChanged(nameof(ShowMinimizeButton));
            OnPropertyChanged(nameof(ShowWindowChromeButtons));
            OnPropertyChanged(nameof(ShowUpdateCheckButton));
            OnPropertyChanged(nameof(ShowCatalogNavigation));
            OnPropertyChanged(nameof(ShowExternalLinks));
            if (SettingsButton != null) SettingsButton.IsVisible = ShowSettingsButton;
            if (MinimizeButton != null) MinimizeButton.IsVisible = ShowMinimizeButton;
            if (ToggleMaximizeButton != null) ToggleMaximizeButton.IsVisible = ShowWindowChromeButtons;
            if (CloseLauncherButton != null) CloseLauncherButton.IsVisible = ShowWindowChromeButtons;
            if (CheckForUpdatesButton != null) CheckForUpdatesButton.IsVisible = ShowUpdateCheckButton;
            if (BrowseNavButton != null) BrowseNavButton.IsVisible = ShowCatalogNavigation;
            if (ExternalLinksHost != null) ExternalLinksHost.IsVisible = ShowExternalLinks;
            if (HeaderFixedActions != null)
                HeaderFixedActions.Margin = new Thickness(0, 0, KioskLocked ? 16 : 0, 0);
            if (!refreshShell && KioskLocked)
                _sidebarController?.CollapseForKioskStartup();
            if (!KioskLocked)
                _sidebarController?.ReleaseKioskSidebar();
            Library?.ApplyDisplaySettings();
            if (refreshShell && _appearance != null)
                UpdateMainViewUi();
        }

        private int _kioskUnlockBusy;
        private async Task ToggleKioskFromChordAsync()
        {
            if (_session.IsClosed || Interlocked.CompareExchange(ref _kioskUnlockBusy, 1, 0) != 0)
                return;
            try
            {
                if (!KioskLocked)
                {
                    _settingsViewModel.LockKioskSession();
                    return;
                }

                if (KioskLock.HasPin(_settings))
                {
                    var pin = await _prompts.PromptKioskPinAsync();
                    if (pin == null || _session.IsClosed)
                        return;
                    if (!KioskLock.VerifyPin(_settings, pin))
                    {
                        await ShowMessageBoxAsync("Incorrect PIN.", "Kiosk mode");
                        return;
                    }
                }

                _settingsViewModel.UnlockKioskSession();
            }
            finally
            {
                Interlocked.Exchange(ref _kioskUnlockBusy, 0);
            }
        }

        private void ShowTagEditOverlay(GameInfo game, MetadataEditMode mode = MetadataEditMode.Tags)
        {
            if (KioskLocked)
                return;
            Shell.TagEditorOpen = true;
            ClearGamepadFocus();
            _chromeNavigation.ClearSidebarGamepadFocus();
            _chromeNavigation.ClearTopBarGamepadFocus();
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.TagEditOverlay;
            OnPropertyChanged(nameof(GamepadHintsVisible));
            TagEditOverlay.Open(game, mode);
        }

        private void CloseTagEditOverlay()
        {
            TagEditOverlay.CloseEditor();
            Shell.TagEditorOpen = false;
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.TagEditOverlay)
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                SelectInitialGamepadItemForCurrentView();
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        private void ShowEntryFormOverlay(bool forCreate, GameInfo? gameToEdit = null)
        {
            if (KioskLocked)
                return;
            if (Shell.SettingsOpen)
                CloseSettingsPanel();
            Shell.EntryEditorOpen = true;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.EntryFormOverlay;
            OnPropertyChanged(nameof(GamepadHintsVisible));
            EntryFormOverlay.Open(forCreate ? null : gameToEdit);
        }

        private void CloseEntryFormOverlay()
        {
            EntryFormOverlay.CloseEditor();
            Shell.EntryEditorOpen = false;
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.EntryFormOverlay)
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                SelectInitialGamepadItemForCurrentView();
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        internal void RevealManuallyAddedApp(string folder)
        {
            if (_session.IsClosed) return;
            // A successful create should remain discoverable even from Installed Only or a search.
            if (!_gameManager.Games.Any(g => string.Equals(g.FolderName, folder, StringComparison.OrdinalIgnoreCase)))
            {
                Library.CancelSearch();
                _gameManager.LibrarySearchText = string.Empty;
                _settings.ActiveTagDisplayFilterId = null;
                _gameManager.SetListScope(AppListScope.AllApps, _settings);
                Library.RefreshFilters();
                LibraryFiltersPanel.RefreshSidebarFilterSelection();
            }
            ShowLibraryView();
            ApplySorting();
            Dispatcher.UIThread.Post(() =>
            {
                if (_session.IsClosed || Shell.Mode != MainViewMode.Library || Shell.EntryEditorOpen) return;
                var app = _gameManager.Games.FirstOrDefault(g => string.Equals(g.FolderName, folder, StringComparison.OrdinalIgnoreCase));
                if (app == null) return;
                if (IsGamepadFocusActive)
                    LibraryPanel.Navigation.ApplyLibraryGamepadSelection(_gameManager.Games.IndexOf(app));
                LibraryPanel.FindGameCardRoot(app)?.BringIntoView();
            }, DispatcherPriority.Loaded);
        }

        private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_session.IsClosed)
                return;
            if (e.PropertyName is nameof(ShellViewModel.ThemeColorBrush) or nameof(ShellViewModel.SecondaryColorBrush))
                UpdateThemeColors();
            if (e.PropertyName is nameof(ShellViewModel.PendingUpdatesCount) or nameof(ShellViewModel.IsCheckingUpdates))
                _app?.UpdateTrayTooltip(Shell.PendingUpdatesCount, Shell.IsCheckingUpdates);
            if (e.PropertyName == nameof(ShellViewModel.ShowUpdateCheckStatus))
                LibraryPanel.SetUpdateStatusVisible(Shell.ShowUpdateCheckStatus);
        }

        private void OnGameManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_session.IsClosed && e.PropertyName is nameof(GameManager.Games) or nameof(GameManager.IsLibraryEmpty) or nameof(GameManager.HasNoLibrarySearchMatches))
                Dispatcher.UIThread.Post(UpdateGameCollectionUi);
        }

        private void NotifyGamepadUiChanged()
        {
            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        // Is Theme Color Light
        private void UpdateThemeColors()
        {
            _themeEditor.ApplyResources(Resources, Shell.ThemeColorBrush.Color, Shell.SecondaryColorBrush.Color, _settings.ThemeTextMode);
            OnPropertyChanged(nameof(WindowBackground));
        }

        private void ApplyThemeColor(bool secondary, Color color)
        {
            if (_session.IsClosed)
                return;
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (secondary)
            {
                _settings.SecondaryColor = hex;
                Shell.SecondaryColorBrush = new SolidColorBrush(color);
            }
            else
            {
                _settings.PrimaryColor = hex;
                Shell.ThemeColorBrush = new SolidColorBrush(color);
            }

            OnSettingChanged();
        }

        private void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_settings.ShowOSTopBar)
                return;
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    // Double-click to maximize/restore
                    ToggleHostMaximized();
                }
                else
                {
                    HostWindow?.BeginMoveDrag(e);
                }
            }
        }

        private void LoadCurrentVersion()
        {
            try
            {
                var velopackVersion = _velopackUpdateService.CurrentVersion;
                if (!string.IsNullOrWhiteSpace(velopackVersion))
                {
                    Shell.Version = velopackVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? velopackVersion : $"v{velopackVersion}";
                    return;
                }

                string currentAppDirectory = AppDomain.CurrentDomain.BaseDirectory;
                Shell.Version = LauncherVersionService.ReadInstalledVersion(currentAppDirectory);
            }
            catch (Exception ex)
            {
                Shell.Version = "Unknown";
                Debug.WriteLine($"Failed to load version: {ex.Message}");
            }
        }

        public void HandleOpened()
        {
            UpdateGamepadChromeClass();
            if (_initializeOnOpen && _session.TryInitialize())
                _ = _session.RunAsync(InitializeGamesAsync);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LauncherArtworkLoader.DisplayScale = ArtworkDisplayScale;
            LauncherArtworkLoader.ThumbnailBox = ArtworkThumbnailBox;
            UpdateGamepadChromeClass();
            _mobileLayout.Attach();
            if (HostWindow == null && !_hasInitializedFocus)
                HandleOpened();
        }

        /// <summary>The sharpest any screen shows the interface, so artwork is decoded large enough for it.</summary>
        private double ArtworkDisplayScale()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var screen = topLevel?.Screens?.All.Select(s => s.Scaling).DefaultIfEmpty(1).Max() ?? 1;
            var interfaceScale = PlatformCapabilities.IsMobile ? 1 : _settingsViewModel.InterfaceScalePercent / 100d;
            return Math.Max(topLevel?.RenderScaling ?? 1, screen) * Math.Max(1, interfaceScale);
        }

        // Views/BrowseCard.axaml: a catalog card is 256 wide and its cover band 140-156 tall.
        private static readonly Size CatalogCardArt = new(256, 156);

        /// <summary>The largest area artwork is shown in: library covers at the current card settings, or catalog cards.</summary>
        private Size ArtworkThumbnailBox()
        {
            // Mobile cards stretch to the screen's width.
            if (PlatformCapabilities.IsMobile)
                return new(512, 512);
            var settings = _settingsViewModel;
            var row = Math.Min(settings.IconSize, settings.ListRowHeight);
            var library = settings.UseGridView ? new Size(settings.SlotSize, settings.IconSize) : new Size(row, row);
            return new(Math.Max(library.Width, CatalogCardArt.Width), Math.Max(library.Height, CatalogCardArt.Height));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _mobileLayout.Detach();
            base.OnDetachedFromVisualTree(e);
        }

        private void MobileLibrarySortFlyout_Opening(object? sender, EventArgs e)
        {
            GamepadMenuFlyoutNavigation.Attach(sender as MenuFlyout);
            LibraryToolbar.SortFlyoutOpening(sender as MenuFlyout);
        }

        private void MobileLibrarySortItem_Click(object? sender, RoutedEventArgs e) => LibraryToolbar.SelectSort((sender as MenuItem)?.Tag as string);
        internal async Task InitializeGamesAsync()
        {
            Library.BeginInitialLoad();
            try
            {
                // Startup must not wait for catalog servers, release checks, or rate-limit cooldowns.
                await Task.Run(() => _gameManager.ReloadLibraryFromDiskAsync(allowNetwork: false), _session.Token);
                if (_session.IsClosed)
                    return;
                _settings = _settingsViewModel.Load();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_session.IsClosed)
                        return;
                    ApplySorting();
                    Library.RefreshContinue();
                    Library.RefreshFilters();
                    LibraryFiltersPanel.RefreshSidebarFilterSelection();
                    UpdateMainViewUi();
                    _updates.RefreshUpdateCheckStatus();
                    UpdateLibraryEmptyState();
                    if (!_hasInitializedFocus)
                    {
                        SetInitialFocus();
                        _hasInitializedFocus = true;
                    }

                    Banners.ApplyTopBanner();
                });
                // The saved library is already usable.
                await RefreshStartupMetadataAsync();
                if (_session.IsClosed) return;
                await _updates.ShowFirstRunWelcomeIfNeededAsync();
                // After the welcome, so a new player's yes to usage data counts these too.
                TrackLibraryLoaded();
                TrackScreen();
                _ = _session.RunAsync(Banners.RefreshAnnouncementBannerAsync);
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (_session.IsClosed)
                        return;
                    UpdateGameCollectionUi();
                    Library.FailInitialLoad();
                    await ShowMessageBoxAsync($"Failed to load apps: {ex.Message}", "Load Error");
                });
            }
        }

        private async Task RefreshStartupMetadataAsync()
        {
            try { await _gameManager.RefreshLoadedLibraryMetadataAsync(_session.Token); }
            catch (OperationCanceledException) when (_session.IsClosed) { }
            // An online refresh failure must not replace a successfully loaded library.
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Startup library refresh failed: {ex.GetType().Name}"); }
        }

        private bool IsGamepadFocusActive => GamepadFocusChrome.ShouldShowGamepadChrome(_settings.EnableGamepadInput, _inputService?.HasConnectedGamepad == true, GamepadFocusChrome.KeyboardNavigationActive, SteamDeckEnvironment.IsGamingMode());

        private void UpdateGamepadChromeClass()
        {
            var active = IsGamepadFocusActive;
            GamepadFocusChrome.SetActive(active, this, HostWindow);
            GamepadModalDialogNavigation.Instance.SyncChromeClass(active);
        }

        private void ActivateKeyboardNavChrome()
        {
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            UpdateGamepadChromeClass();
        }

        private void MainWindow_PointerPressedForChrome(object? sender, PointerPressedEventArgs e)
        {
            if (e.Pointer.Type != PointerType.Mouse)
                return;
            if (!IsGamepadFocusActive)
                return;
            if (_navigationRouter.SynchronizePointer(e.Source))
                return;
            if (_inputService?.HasConnectedGamepad == true)
                return;
            if (!GamepadFocusChrome.KeyboardNavigationActive)
                return;
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            UpdateGamepadChromeClass();
            ClearGamepadFocus();
        }

        private bool _gamepadTracked;
        private void HandleGamepadConnectionChanged(bool hasConnected)
        {
            if (hasConnected && !_gamepadTracked)
            {
                _gamepadTracked = true;
                Telemetry.Current.Track("gamepad_connected");
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (_session.IsClosed)
                    return;
                UpdateGamepadChromeClass();
                if (GamepadTextInput.IsEditing)
                {
                    SettingsPanel.Bindings.RefreshControllers();
                    return;
                }
                if (!_settings.EnableGamepadInput)
                {
                    ClearGamepadFocus();
                    SettingsPanel.Bindings.RefreshControllers();
                    return;
                }

                if (hasConnected || IsGamepadFocusActive)
                {
                    if (Shell.SettingsOpen)
                        SettingsPanel.Navigation.ApplySettingsGamepadSelection(SettingsPanel.Navigation.FocusIndex < 0 ? 0 : SettingsPanel.Navigation.FocusIndex);
                    else
                        SelectInitialGamepadItemForCurrentView();
                }
                else
                {
                    ClearGamepadFocus();
                }

                SettingsPanel.Bindings.RefreshControllers();
            });
        }

        private static bool TryFocus(Control? control) =>
            control is { IsVisible: true, IsEnabled: true, Focusable: true } && control.Focus();

        private void SetInitialFocus()
        {
            // Small delay to ensure UI is fully rendered
            Dispatcher.UIThread.Post(() =>
            {
                if (_session.IsClosed)
                    return;
                if (IsGamepadFocusActive && Shell.Mode == MainViewMode.Library && Games.Count > 0)
                {
                    LibraryPanel.Navigation.SelectInitialLibraryGamepadItem();
                    return;
                }

                if (IsGamepadFocusActive && Shell.Mode == MainViewMode.Browse)
                {
                    BrowsePanel.Navigation.SelectInitial();
                    return;
                }

                // The sidebar nav button exists while kiosk has the pane collapsed, but it cannot take focus.
                if (Library.IsContinueVisible && TryFocus(this.FindControl<Button>("ContinueButton")))
                    return;
                if (TryFocus(this.FindControl<Button>("LibraryNavButton")))
                    return;
                if (TryFocus(DesktopSidebarToggleButton))
                    return;

                var firstFocusable = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable);
                TryFocus(firstFocusable);
            }, DispatcherPriority.Loaded);
        }

        public void CloseLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (CloseSettingsPanel())
                return;

            if (Shell.ModDetailsOpen)
            {
                ModsPanel.Details.Close();
                return;
            }

            if (Shell.BrowseDetailsOpen)
            {
                CloseBrowseDetails();
                return;
            }

            if (Shell.EntryEditorOpen)
            {
                CloseEntryFormOverlay();
                return;
            }

            if (Shell.TagEditorOpen)
            {
                CloseTagEditOverlay();
                return;
            }

            if (MessagePromptOverlay.Dismiss())
                return;
            CloseHost();
        }

        public void ToggleMaximize_Click(object sender, RoutedEventArgs e) => ToggleHostMaximized();
        public void MinimizeButton_Click(object sender, RoutedEventArgs e) => SetHostWindowState(WindowState.Minimized);
        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            _mobileLayout.CloseMobileNav();
            await _libraryLaunch.ContinueAsync();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (KioskLocked || SettingsPanel == null)
                return;
            if (Shell.EntryEditorOpen)
                CloseEntryFormOverlay();
            if (Shell.TagEditorOpen)
                CloseTagEditOverlay();
            if (Shell.BrowseDetailsOpen)
                CloseBrowseDetails(restoreSelection: false);
            Shell.SettingsOpen = !Shell.SettingsOpen;
            SettingsPanel.IsVisible = Shell.SettingsOpen;
            if (Shell.SettingsOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;
                NotifyGamepadUiChanged();
                ClearGamepadFocus();
                SettingsPanel.Bindings.RefreshControllers();
                SettingsPanel.Bindings.Refresh();
                Dispatcher.UIThread.Post(() =>
                {
                    if (_session.IsClosed || !Shell.SettingsOpen)
                        return;
                    if (IsGamepadFocusActive)
                        SettingsPanel.Navigation.ApplySettingsGamepadSelection(
                            SettingsPanel.Navigation.GetSelectedSettingsTabControlIndex(SettingsPanel.Navigation.CollectSettingsTabItems()));
                    else
                        SettingsPanel.Navigation.ClearSettingsGamepadFocusClasses(SettingsPanel.Navigation.CollectSettingsFocusableControls());
                }, DispatcherPriority.Loaded);
            }
            else
            {
                SettingsPanel.Bindings.Cancel();
                SettingsPanel.Navigation.ClearSettingsGamepadFocusClasses(SettingsPanel.Navigation.CollectSettingsFocusableControls());
                SettingsPanel.Navigation.FocusIndex = -1;
            }
        }

        public void OpenSettings()
        {
            if (!Shell.SettingsOpen)
                SettingsButton_Click(this, new RoutedEventArgs());
        }

        public void OpenGitHubApiTokenSettings() => OpenAdvancedApiTokenSettings(focusGitLab: false);
        public void OpenGitLabApiTokenSettings() => OpenAdvancedApiTokenSettings(focusGitLab: true);
        private void OpenAdvancedApiTokenSettings(bool focusGitLab)
        {
            if (SettingsPanel == null)
                return;
            if (Shell.EntryEditorOpen)
                CloseEntryFormOverlay();
            if (Shell.TagEditorOpen)
                CloseTagEditOverlay();
            if (Shell.BrowseDetailsOpen)
                CloseBrowseDetails(restoreSelection: false);
            Shell.SettingsOpen = true;
            SettingsPanel.IsVisible = true;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;
            ClearGamepadFocus();
            NotifyGamepadUiChanged();
            SettingsPanel.FocusApiToken(focusGitLab);
        }

        private bool CloseSettingsPanel()
        {
            if (!Shell.SettingsOpen || SettingsPanel == null)
                return false;
            Shell.SettingsOpen = false;
            SettingsPanel.IsVisible = false;
            SettingsPanel.Bindings.Cancel();
            SettingsPanel.Navigation.ClearSettingsGamepadFocusClasses(SettingsPanel.Navigation.CollectSettingsFocusableControls());
            SettingsPanel.Navigation.FocusIndex = -1;
            _gamepadNavigation.ActiveZone = _navigationRouter.MainZone;
            NotifyGamepadUiChanged();
            if (_settings.EnableGamepadInput)
            {
                DismissTextInputFocus();
                SelectInitialGamepadItemForCurrentView();
                return true;
            }

            var settingsButton = this.FindControl<Button>("SettingsButton");
            if (settingsButton != null)
                settingsButton.Focus();
            else
            {
                var firstFocusable = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable && !IsInsideSettingsPanel(c));
                firstFocusable?.Focus();
            }

            return true;
        }

        private void CloseSettingsPanel_Click(object? sender, RoutedEventArgs e)
        {
            CloseSettingsPanel();
        }

        private void LibraryNavButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowLibraryView();
            _mobileLayout.CloseMobileNav();
        }

        private void BrowseNavButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowBrowseView();
            _mobileLayout.CloseMobileNav();
        }

        private void ShowLibraryView()
        {
            Shell.Mode = MainViewMode.Library;
            Shell.AppUpdatesOpen = false;
            if (Shell.ModsOpen)
                ModsPanel.CloseModsOverlay();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();
            if (IsGamepadFocusActive)
                LibraryPanel.Navigation.SelectInitialLibraryGamepadItem();
            else
                ClearGamepadFocus();
        }

        private void ShowBrowseView()
        {
            if (KioskLocked)
                return;
            if (Shell.BrowseDetailsOpen)
                CloseBrowseDetails(restoreSelection: false);
            Shell.Mode = MainViewMode.Browse;
            Shell.AppUpdatesOpen = false;
            if (Shell.ModsOpen)
                ModsPanel.CloseModsOverlay();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();
            BrowsePanel.Model.RefreshLibraryState();
            BrowsePanel.EnsureLoaded();
            if (IsGamepadFocusActive)
                BrowsePanel.Navigation.SelectInitial();
            else
                ClearGamepadFocus();
        }

        private void OpenAppUpdatesReview()
        {
            if (KioskLocked)
                return;
            if (Shell.ModsOpen)
                ModsPanel.CloseModsOverlay();
            Shell.Mode = MainViewMode.Library;
            Shell.AppUpdatesOpen = true;
            RefreshAppUpdateReviewRows();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();
            if (IsGamepadFocusActive)
                AppUpdatesReviewPanel.SelectInitialAppUpdatesReviewGamepadItem();
            else
                ClearGamepadFocus();
        }

        void IModsFeatureHost.RefreshShell() => UpdateMainViewUi();
        void IModsFeatureHost.ResetNavigation() => ResetGamepadNavigationIndices();
        void IModsFeatureHost.RestoreLibraryFocus() => LibraryPanel.Navigation.SelectInitialLibraryGamepadItem();
        void IModsFeatureHost.RestoreCurrentFocus() => SelectInitialGamepadItemForCurrentView();
        void IModsFeatureHost.NotifyHints() => OnPropertyChanged(nameof(GamepadHintsVisible));
        void IModsFeatureHost.OpenUrl(string url) => OpenUrl(url);
        Task IModsFeatureHost.ShowErrorAsync(string message, string title) => ShowMessageBoxAsync(message, title);
        Task<MessagePromptResult> IModsFeatureHost.ChooseAsync(string message, string title) => ShowChoicePromptAsync(message, title);
        bool IAppUpdateReviewActions.IsReviewOpen => Shell.AppUpdatesOpen && !_session.IsClosed;

        IReadOnlyList<GameInfo> IAppUpdateReviewActions.GetReviewRows() => _updates.GetAppUpdateReviewRows();
        IReadOnlyList<GameInfo> IAppUpdateReviewActions.GetPendingUpdates() => _updates.GetPendingAppUpdates();
        async Task IAppUpdateReviewActions.UpdateAsync(GameInfo game, bool automaticSelection) => await _libraryLaunch.HandleUpdateNowAsync(AppUpdatesReviewPanel.GetUpdateAnchor(game), game, preferAutoPlatform: automaticSelection);
        Task IAppUpdateReviewActions.SkipAsync(GameInfo game) => _libraryLaunch.HandleSkipUpdateAsync(game);
        void IAppUpdateReviewActions.ShowVersions(GameInfo game) => _libraryLaunch.ShowUpdateActionMenu(AppUpdatesReviewPanel.GetVersionsAnchor(game), game);
        void IAppUpdateReviewActions.UpdatesChanged()
        {
            _updates.RefreshUpdateCheckStatus();
            _updates.NotifyUpdateCheckUiProperties();
        }

        void IAppUpdateReviewActions.ReturnToLibrary() => ShowLibraryView();
        GamepadNavigationService IFeatureNavigationHost.Navigation => _gamepadNavigation;

        GamepadNavigationZone IFeatureNavigationHost.MainContentZone => GetMainContentGamepadZone();

        bool IFeatureNavigationHost.IsFocusActive => IsGamepadFocusActive;

        bool IFeatureNavigationHost.ApplyTransition(GamepadZoneTransition transition) => _navigationRouter.ApplyTransition(transition);
        void IFeatureNavigationHost.ClearSidebarFocus() => _chromeNavigation.ClearSidebarGamepadFocus();
        void IFeatureNavigationHost.ClearFocus() => ClearGamepadFocus();
        void IFeatureNavigationHost.FocusCard(bool stealFocus) => GamepadPointerFocusSync.ApplyCardSelectionFocus(CardGamepadFocusSink, stealFocus);
        private void RefreshAppUpdateReviewRows() => AppUpdatesReviewPanel.Model.Refresh();
        private void CloseAppUpdatesReviewIfEmpty()
        {
            if (!Shell.AppUpdatesOpen)
                return;
            RefreshAppUpdateReviewRows();
            if (AppUpdateReviewRows.Count == 0)
                ShowLibraryView();
        }

        private void UpdateMainViewUi()
        {
            UpdateGamepadHintsBar();
            if (Shell.BrowseDetailsOpen && Shell.Mode != _detailsOpenedFrom)
                CloseBrowseDetails(restoreSelection: false);
            _appearance.Refresh();
            UpdateLibraryEmptyState();
            NotifyGamepadUiChanged();
        }

        private void UpdateLibraryEmptyState()
        {
            LibraryPanel.UpdateEmptyState(Shell.Mode == MainViewMode.Library && !Shell.AppUpdatesOpen && !Shell.ModsOpen);
            LibraryToolbar.RefreshClearButton();
        }

        // The page app details were opened over (the App Catalog, or the Library), which Back returns to.
        private MainViewMode _detailsOpenedFrom = MainViewMode.Browse;

        private void OpenBrowseDetails(BrowseItem item) => OpenBrowseDetails(item, releases: false);

        private void OpenBrowseDetails(BrowseItem item, bool releases)
        {
            _detailsScreen = ("app", item.App?.Slug);
            _detailsOpenedFrom = Shell.Mode;
            Shell.BrowseDetailsOpen = true;
            TrackScreen();
            BrowseDetailsPanel.Open(item, releases, Shell.Mode == MainViewMode.Library ? "Library" : "App Catalog");
            NotifyGamepadUiChanged();
        }

        /// <summary>
        /// A library app's README and changelog, on its app page (Overview and Releases). An app that isn't in the App Catalog
        /// opens the same page with its repository's README and releases.
        /// </summary>
        private async Task OpenLibraryAppPageAsync(GameInfo game, bool releases)
        {
            _inputService?.TryHandleContextMenuOptionsDismiss();
            QuiverCatalogApp? listed = null;
            if (!string.IsNullOrWhiteSpace(game.CatalogSlug))
            {
                listed = _gameManager.CatalogReleases.ListedApp(game);
                if (listed == null)
                {
                    try { listed = (await _catalogClient.GetDetailAsync(game.CatalogSlug, _session.Token)).Entry; }
                    // Offline or gone from the catalog: the repository's README and releases still show.
                    catch (Exception ex) when (!_session.Token.IsCancellationRequested)
                    {
                        Debug.WriteLine($"Catalog page unavailable for {game.CatalogSlug}: {ex.Message}");
                    }
                }
            }
            if (_session.IsClosed) return;
            OpenBrowseDetails(listed != null ? BrowsePanel.Model.CardFor(listed) : BrowseItem.FromList(game), releases);
        }

        private void OpenBrowseGame(BrowseGame game)
        {
            _detailsScreen = ("game", game.Slug);
            _detailsOpenedFrom = Shell.Mode;
            Shell.BrowseDetailsOpen = true;
            TrackScreen();
            BrowseDetailsPanel.OpenGame(game.Slug, game.Title);
            NotifyGamepadUiChanged();
        }

        private void CloseBrowseDetails(bool restoreSelection = true)
        {
            var restore = restoreSelection && _gamepadNavigation.ActiveZone == GamepadNavigationZone.BrowseDetailsOverlay;
            Shell.BrowseDetailsOpen = false;
            BrowseDetailsPanel.Close();
            NotifyGamepadUiChanged();
            if (!restore)
                return;
            if (_detailsOpenedFrom == MainViewMode.Library)
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                LibraryPanel.Navigation.RestoreLibraryGamepadFocusAfterMenu();
                return;
            }
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.BrowseGrid;
            if (IsGamepadFocusActive)
                BrowsePanel.Navigation.SelectInitial();
        }

        private void RefreshBrowseLibraryState()
        {
            if (_session.IsClosed)
                return;
            BrowsePanel.Model.RefreshLibraryState();
            if (Shell.BrowseDetailsOpen)
                BrowseDetailsPanel.Refresh();
        }

        private QuiverCatalogClient _catalogClient = null!;

        /// <summary>Open in Library on a catalog card: the Library, with that app highlighted and scrolled to.</summary>
        private void OpenInLibrary(BrowseItem item)
        {
            var game = (item.App?.Slug is { } slug ? _gameManager.LibraryApps.FirstOrDefault(a => a.CatalogSlug == slug) : null)
                ?? (item.ListApp is { } listed ? BrowsePanel.Model.FindInLibrary(listed) : null)
                ?? _gameManager.LibraryApps.FirstOrDefault(a => string.Equals(a.FolderName?.Trim(), item.FolderName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (Shell.BrowseDetailsOpen)
                CloseBrowseDetails(restoreSelection: false);
            ShowLibraryView();
            if (game == null || LibraryPanel.Navigation.SelectGame(game))
                return;
            // A library search can hide it: clear the search and look again.
            LibraryToolbar.ClearSearch();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_session.IsClosed) LibraryPanel.Navigation.SelectGame(game);
            }, DispatcherPriority.Loaded);
        }

        /// <summary>
        /// A card's Add button (or Y on a highlighted card): adds the app as its page's Add would, without opening the page.
        /// A catalog app's page is read first, for where the app comes from.
        /// </summary>
        private async Task AddCardAsync(BrowseItem item)
        {
            if (!item.CanAdd || item.IsAdding) return;
            item.IsAdding = true;
            try
            {
                var app = item.ListApp;
                if (app == null && item.App is { } listed)
                {
                    try
                    {
                        var detail = await _catalogClient.GetDetailAsync(listed.Slug, _session.Token);
                        app = QuiverCatalogMapping.ToGameInfo(_gameManager.CatalogService, detail.Entry, detail.Project);
                    }
                    catch (Exception ex) when (!_session.Token.IsCancellationRequested)
                    {
                        await ShowMessageBoxAsync($"Couldn't load {item.Title} from quiverlauncher.com, so it wasn't added. {ex.Message}", "Could Not Add");
                        return;
                    }
                }
                if (app == null) return;
                await AddFromBrowseAsync(app, "app_catalog_card", item.App?.Slug);
                // A game page's cards aren't the catalog's: mark this one too.
                BrowsePanel.Model.MarkLibraryState([item]);
            }
            finally
            {
                item.IsAdding = false;
            }
        }

        private async Task AddFromBrowseAsync(GameInfo app, string from = "app_catalog", string? catalogSlug = null)
        {
            try
            {
                var result = await _libraryAdd.AddAsync(app);
                if (result.Outcome == LibraryAddOutcome.Added)
                {
                    // An App Catalog app by its slug; one from the player's own list only by where it's from.
                    var usage = Telemetry.AppRef(app);
                    if ((catalogSlug ?? (_detailsScreen is ("app", { } shown) ? shown : null)) is { } slug && !app.IsManuallyManaged)
                        (usage["slug"], usage["source"]) = (slug, "catalog");
                    usage["from"] = from;
                    Telemetry.Current.Track("app_added", usage);
                }
                if (result.Outcome == LibraryAddOutcome.FolderConflict)
                    await ShowMessageBoxAsync($"This app wasn't added. {result.Error}", "Could Not Add");
            }
            catch (LibraryAddPreparationException ex)
            {
                await ShowMessageBoxAsync($"{app.Name} was added to your library, but its files could not be prepared: {ex.InnerException?.Message}", "App Preparation");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"This app wasn't added. {ex.Message}", "Could Not Add");
            }
            RefreshBrowseLibraryState();
        }

        private readonly RepositoryReadmeService _browseReadmes = new();
        private async Task<DocumentContent> LoadRepositoryReadmeAsync(GameInfo app, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(app.Repository)) return new("This app has no repository README.", IsMarkdown: false);
            var result = await _browseReadmes.GetReadmeAsync(_gameManager.HttpClient, app.EffectiveRepositorySource, app.Repository,
                _settings.GitHubApiToken, _settings.GitLabApiToken, QuiverLauncherPaths.CacheDirectory, DateTime.UtcNow, token);
            return result.Status switch
            {
                RepositoryReadmeStatus.Markdown when !string.IsNullOrWhiteSpace(result.Markdown) => new(result.Markdown, result.RawRootUrl),
                RepositoryReadmeStatus.Error => new(result.ErrorMessage ?? "Couldn't load the README.", IsMarkdown: false),
                _ => new("No README found for this repository.", IsMarkdown: false),
            };
        }

        private void OnSettingChanged()
        {
            if (SettingsPanel._suppressSettingsUiEvents)
                return;
            try
            {
                _settingsViewModel.Save(_settings);
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to save settings: {ex.Message}", "Save Error"));
            }
        }

        private void UpdateGridLayoutVisibility()
        {
            if (_settings == null)
                return;
            var useGrid = _settings.UseGridView;
            var compact = _settings.GridCompactCards;
            LibraryPanel.UpdateLayoutMode();
            // Don't restore library card focus while Settings is open — App Cards
            // toggles/presets change layout and would steal the focus ring.
            if (_settings.EnableGamepadInput && !Shell.SettingsOpen && Shell.Mode == MainViewMode.Library)
            {
                LibraryPanel.Navigation.SyncGamepadLibrarySelection();
            }
        }

        private async void CheckforUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (KioskLocked)
                return;
            await RunUpdateCheckAsync(promptForReview: true, isManualCheck: true);
        }
        private void CancelUpdateCheck_Click(object? sender, EventArgs e) { CheckForUpdatesButton.Focus(); _updateChecks.Cancel(); }
        private void DismissUpdateCheck_Click(object? sender, EventArgs e)
        {
            CheckForUpdatesButton.Focus();
            Shell.DismissUpdateCheckStatus();
        }
        private async void RetryUpdateCheck_Click(object? sender, EventArgs e) =>
            await _session.RunAsync(() => _updateChecks.RetryAsync(true, true, _session.Token));
        /// <summary>
        /// Shared update check used by the toolbar button, tray menu, and background timer.
        /// </summary>
        public Task RunUpdateCheckAsync(bool promptForReview, bool isManualCheck) => _session.RunAsync(async () =>
        {
            await _updateChecks.CheckAsync(promptForReview, isManualCheck, _session.Token);
        });
        private void GithubButton_Click(object sender, RoutedEventArgs e)
        {
            if (KioskLocked)
                return;
            try
            {
                string url = "https://github.com/tgeorgiadis/quiver-launcher/";
                OpenUrl(url);
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to open Github link: {ex.Message}", "Action Error"));
            }
        }

        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            if (KioskLocked)
                return;
            try
            {
                OpenUrl("https://discord.gg/5XRThpWHGk");
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to open Discord link: {ex.Message}", "Action Error"));
            }
        }

        private void KofiButton_Click(object sender, RoutedEventArgs e)
        {
            if (KioskLocked)
                return;
            try
            {
                OpenUrl("https://ko-fi.com/magicturt1e");
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to open Ko-fi link: {ex.Message}", "Action Error"));
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                UrlLauncher.Open(url);
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to open URL: {ex.Message}", "Error"));
            }
        }

        private void ShowDisplayFilterOverlay(string? filterId)
        {
            _mobileLayout.CloseMobileNav();
            if (!DisplayFilterOverlay.Open(filterId))
                return;
            DisplayFilterOverlay.IsVisible = true;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.DisplayFilterOverlay;
            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        private void CloseDisplayFilterOverlay()
        {
            DisplayFilterOverlay.CloseEditor();
            DisplayFilterOverlay.IsVisible = false;
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.DisplayFilterOverlay)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Sidebar;
                _chromeNavigation.ApplySidebarGamepadSelection(Math.Max(0, _gamepadNavigation.SidebarSelectedIndex));
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        private bool IsDisplayFilterOverlayOpen => DisplayFilterOverlay?.IsVisible == true;

        private void ApplySorting()
        {
            if (_gameManager?.Games == null || _gameManager.Games.Count == 0)
            {
                Debug.WriteLine("ApplySorting: No apps to sort");
                return;
            }

            Debug.WriteLine($"ApplySorting: Sorting {_gameManager.Games.Count} apps by {Library.SortBy}");
            Library.ApplySorting();
            Debug.WriteLine("ApplySorting: Completed sorting");
        }

        private void UpdateGamepadHintsBar()
        {
            if (GamepadHintsBar == null || _settings == null)
                return;
            _settings.EnsureInitialized();
            // In the App Catalog the options button adds the highlighted app, or shows it in the library when it's already there.
            var options = Shell.Mode == MainViewMode.Browse && !Shell.BrowseDetailsOpen ? "Add / Open in Library" : "Options";
            var padHints = GamepadBindingLabels.FormatHints(_settings.GamepadBindings, options);
            var keyHints = KeyboardBindingLabels.FormatHints(_settings.KeyboardBindings, options);
            GamepadHintsBar.Text = _settings.EnableGamepadInput ? $"{padHints}  |  {keyHints}" : keyHints;
        }

        private void SetIgnoreArticlesWhenSorting(bool enabled)
        {
            if (_settings == null)
                return;
            _settings.IgnoreArticlesWhenSorting = enabled;
            OnSettingChanged();
            ApplySorting();
        }

        private void AddNewEntryButton_Click(object? sender, RoutedEventArgs e)
        {
            if (KioskLocked)
                return;
            ShowEntryFormOverlay(forCreate: true);
        }

        private void PlayLauncherMusic(string path) => _music.Play(path);
        private void StopLauncherMusic()
        {
            _foreground.StopMusic();
        }

        private void LogGamepadDebug(string eventName, string extra = "")
        {
            if (!GamepadDebugLog.IsEnabled())
                return;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            var focusedName = focused is Control control ? (string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name) : focused?.GetType().Name ?? "none";
            GamepadDebugLog.Write(GamepadDebugLog.FormatEvent(eventName, zone: _gamepadNavigation.ActiveZone.ToString(), top: _gamepadNavigation.TopBarSelectedIndex, focused: focusedName, editing: GamepadTextInput.IsEditing, gaming: SteamDeckEnvironment.IsGamingMode(), skipFocus: GamepadTextInput.ShouldSkipNativeFocus(), chrome: GamepadFocusChrome.IsActive, pads: _inputService?.ConnectedGamepadCount ?? 0, extra: extra));
        }

        private void WireChromeXyFocusEdges()
        {
            XyFocusNavigation.EnableOn(this);
            if (SidebarPanel != null)
                XyFocusNavigation.EnableOn(SidebarPanel);
            if (SettingsPanel != null)
                XyFocusNavigation.EnableOn(SettingsPanel);
            if (EntryFormOverlay != null)
                XyFocusNavigation.EnableOn(EntryFormOverlay);
            if (DisplayFilterOverlay != null)
                XyFocusNavigation.EnableOn(DisplayFilterOverlay);
            if (TagEditOverlay != null)
                XyFocusNavigation.EnableOn(TagEditOverlay);
            if (ModsPanel != null)
                XyFocusNavigation.EnableOn(ModsPanel);
            if (BrowsePanel != null)
                XyFocusNavigation.EnableOn(BrowsePanel);
            if (BrowseDetailsPanel != null)
                XyFocusNavigation.EnableOn(BrowseDetailsPanel);
            if (ModDetailsPanel != null)
                XyFocusNavigation.EnableOn(ModDetailsPanel);
            var topBarRoot = _chromeNavigation.GetActiveTopBarRoot();
            if (topBarRoot != null)
                XyFocusNavigation.EnableOn(topBarRoot);
            var sidebar = _chromeNavigation.CollectSidebarFocusableControls();
            var topBar = _chromeNavigation.CollectTopBarControls();
            if (topBar.Count == 0)
                return;
            XYFocus.SetLeft(topBar[0], null);
            if (sidebar.Count == 0)
                return;
            // Declared Focusable neighbors. Library/catalog cards stay non-Focusable;
            // zone exits call Focus() on these controls instead.
            XYFocus.SetLeft(topBar[0], sidebar[0]);
            XYFocus.SetRight(sidebar[^1], topBar[0]);
        }

        private void ResetGamepadNavigationIndices()
        {
            _gamepadNavigation.SidebarSelectedIndex = -1;
            _gamepadNavigation.TopBarSelectedIndex = -1;
            _gamepadNavigation.AppUpdatesReviewToolbarIndex = -1;
            _gamepadNavigation.AppUpdatesReviewSelectedIndex = -1;
            _gamepadNavigation.AppUpdatesReviewRowActionIndex = -1;
        }

        private void DismissTextInputFocus()
        {
            GamepadTextInput.Reset();
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
        }

        /// <summary>Called from App after tray wiring is ready.</summary>
        public void ApplyTraySettingsFromApp() => ApplyTrayAndBackgroundUpdateSettings();
        private void ApplyTrayAndBackgroundUpdateSettings()
        {
            _desktopHost?.ApplyTraySettings();
            _backgroundUpdates.Configure(_settings);
            _updates.RefreshUpdateCheckStatus();
        }

        private void SelectInitialGamepadItemForCurrentView()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (Shell.Mode == MainViewMode.Library && Shell.ModsOpen)
                ModsPanel.Navigation.SelectInitialModsGamepadItem();
            else if (Shell.Mode == MainViewMode.Library && Shell.AppUpdatesOpen)
                AppUpdatesReviewPanel.SelectInitialAppUpdatesReviewGamepadItem();
            else if (Shell.Mode == MainViewMode.Library)
                LibraryPanel.Navigation.SelectInitialLibraryGamepadItem();
            else if (Shell.BrowseDetailsOpen)
                BrowseDetailsPanel.RestoreFocus();
            else if (Shell.Mode == MainViewMode.Browse)
                BrowsePanel.Navigation.SelectInitial();
        }

        private bool ShouldKeepLibraryChromeFocus() => _gamepadNavigation.ShouldKeepLibraryChromeFocus(_gamepadNavigation.ActiveZone, LibraryToolbar.ShouldRestoreSearchFocus(_gamepadNavigation, _chromeNavigation.CollectTopBarControls()));
        private void RestoreLibrarySearchGamepadFocus()
        {
            if (!IsGamepadFocusActive)
                return;
            LibraryPanel.Navigation.ClearLibraryCardGamepadFocus();
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.TopBar;
            var controls = _chromeNavigation.CollectTopBarControls();
            var index = LibraryToolbar.SearchIndex(controls);
            if (index >= 0)
                _gamepadNavigation.TopBarSelectedIndex = index;
            // Keep the caret if the user is mid-query; Highlight() would exit edit.
            if (LibraryToolbar.IsEditingSearch)
            {
                return;
            }

            if (index >= 0)
                _chromeNavigation.ApplyTopBarGamepadSelection(index);
        }

        private void ClearGamepadFocus()
        {
            LibraryPanel.Navigation.ClearLibraryCardGamepadFocus();
            BrowsePanel.Navigation?.ClearHighlights();
            BrowseDetailsPanel.ClearHighlights();
            _chromeNavigation.ClearTopBarGamepadFocusClasses(_chromeNavigation.CollectTopBarControls());
            _chromeNavigation.ClearSidebarGamepadFocusClasses(_chromeNavigation.CollectSidebarFocusableControls());
            Banners.ClearAnnouncementBannerGamepadFocus();
            AppUpdatesReviewPanel.ClearAppUpdatesReviewToolbarGamepadFocus();
            AppUpdatesReviewPanel.ClearAppUpdatesReviewRowActionsGamepadFocus();
            AppUpdatesReviewPanel.ClearAppUpdatesReviewRowFocus();
            ModsPanel.Navigation.ClearModsGamepadFocus();
            SettingsPanel.Navigation.ClearSettingsGamepadFocusClasses(SettingsPanel.Navigation.CollectSettingsFocusableControls());
        }

        private void FocusSidebarNav()
        {
            _chromeNavigation.ApplySidebarGamepadSelection(0);
        }

        private void HandleOptionsAction()
        {
            if (_session.IsClosed || MessagePromptOverlay.Options() || !AllowChromeActions)
                return;
            if (_navigationRouter.OptionsPriorityDetails())
                return;
            if (_inputService?.TryHandleContextMenuOptionsDismiss() == true || _inputService?.TryHandleMenuFlyoutCancel() == true)
                return;
            if (_inputService?.IsGamepadOverlayActive == true)
                return;
            _navigationRouter.OptionsFeature();
        }

        private void HandleConfirmAction()
        {
            if (_session.IsClosed || _handlingGamepadConfirm)
                return;
            _handlingGamepadConfirm = true;
            try
            {
                HandleConfirmActionCore();
            }
            finally
            {
                _handlingGamepadConfirm = false;
            }
        }

        private bool IsInsideSettingsPanel(Control control)
        {
            var parent = control.Parent;
            while (parent != null)
            {
                if (parent == SettingsPanel)
                    return true;
                parent = parent.Parent;
            }

            return false;
        }

        public void HandleClosed()
        {
            if (_closing)
                return;
            _closing = true;
            if (ReferenceEquals(App.TryGetHostedMainView(), this))
                App.CurrentHostedMainView = null;
            _desktopHost?.Dispose();
            MessagePromptOverlay.Model.Complete(MessagePromptResult.Cancel);
            _gameManager.PropertyChanged -= OnGameManagerPropertyChanged;
            foreach (var game in _subscribedGames)
                game.GameProcessStarted -= OnGameProcessStarted;
            _subscribedGames.Clear();
            _backgroundUpdates.Dispose();
            LibraryPanel.CancelMobileGameCardHoldTimer();
            LibraryFiltersPanel.EndTagFilterDragSession();
            _mobileLayout.Detach();
            BrowseDetailsPanel.Model.Readme.Cancel();
            ModsPanel.Details.Model.Close();
            ModsPanel.Workspace.Cancel();
            SettingsPanel.CancelPendingWork();
            Library.Dispose();
            _menus.Dispose();
            _input.Dispose();
            RemoveHandler(InputElement.PointerPressedEvent, MainWindow_PointerPressedForChrome);
            _foreground.Dispose();
            _music.Dispose();
            _ = ObserveShutdownAsync();
        }

        private async Task ObserveShutdownAsync()
        {
            try
            {
                await _session.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Launcher shutdown failed: {ex}");
            }
        }

        public Task ShutdownAsync()
        {
            HandleClosed();
            return _session.DisposeAsync().AsTask();
        }

        public void HandleActivated() => _foreground.Activated();
        public void HandleDeactivated() => _foreground.Deactivated();
        private void SubscribeToGameEvents(GameInfo game)
        {
            if (!_subscribedGames.Add(game))
                return;
            game.GameProcessStarted += OnGameProcessStarted;
        }

        private void OnGameProcessStarted(Process? process) => _foreground.GameStarted(process);
        private void RestoreForegroundFocus()
        {
            if (_session.IsClosed)
                return;
            UpdateGamepadChromeClass();
            if (IsGamepadFocusActive)
                _navigationRouter.RestoreCurrentFocus(bringIntoView: false);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
