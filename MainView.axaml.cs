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
        AppCatalog,
    }

    public enum AppCatalogSubView
    {
        Sources,
        Review,
    }

    public partial class MainView : UserControl, INotifyPropertyChanged, IUpdatePresentation, IAppUpdateReviewActions, IModsFeatureHost, ISettingsFeatureHost
    {
        public ShellViewModel Shell { get; } = new();
        public LibraryViewModel Library { get; private set; } = null!;

        private readonly ThemeEditor _themeEditor;
        private readonly MarkdownRenderer _markdownRenderer;
        private readonly RepositoryReadmeService _repositoryReadmeService = new();
        private readonly GameManager _gameManager;
        private readonly LibraryPersistenceService _libraryPersistence;
        private readonly LibraryActions _libraryActions;
        private readonly LibraryCustomizationService _libraryCustomization;
        private readonly Views.LibraryLaunchController _libraryLaunch;
        private readonly CatalogViewModel _catalogViewModel = new();
        private readonly SettingsViewModel _settingsViewModel;
        private readonly LauncherSession _session = new();
        private readonly Views.MobileShellLayout _mobileLayout;
        private readonly Views.DesktopHeaderLayout? _desktopHeader;
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
        public ObservableCollection<CatalogSourceListItem> CatalogSources => _catalogViewModel.Sources;
        public ResettableObservableCollection<CatalogSyncRowItem> CatalogSyncRows => _catalogSyncViewModel.Rows;
        public ObservableCollection<GameInfo> AppUpdateReviewRows => AppUpdatesReviewPanel.Model.Rows;
        public bool GamepadHintsVisible => IsDesktopPlatform && !Shell.SettingsOpen && !Shell.EntryEditorOpen && !Shell.TagEditorOpen && !Shell.DocumentOpen && !IsDisplayFilterOverlayOpen && !Shell.ModsOpen && !Shell.ModDetailsOpen && !Shell.CatalogDetailsOpen && (Shell.Mode == MainViewMode.Library || (Shell.Mode == MainViewMode.AppCatalog && (Shell.CatalogSubView == AppCatalogSubView.Sources || Shell.CatalogSubView == AppCatalogSubView.Review)));
        /// <summary>
        /// Gamepad chrome actions (zones, overlays, library confirm) when pad input is enabled,
        /// or after keyboard navigation/actions have activated keyboard chrome.
        /// </summary>
        private bool AllowChromeActions => _settings.EnableGamepadInput || GamepadFocusChrome.KeyboardNavigationActive;

        private readonly VelopackUpdateService _velopackUpdateService = new();
        private readonly CatalogSyncViewModel _catalogSyncViewModel = new();
        private readonly CatalogReviewWorkspace _catalogReview;
        private readonly CatalogReleasePrefetch _catalogReleasePrefetch;
        private AppCatalogSource? _activeCatalogSyncSource => _catalogReview.ActiveSource;
        public AppSettings _settings { get => _settingsViewModel.Current; private set => _settingsViewModel.ReplaceCurrent(value); }

        public App _app = null!;
        public SettingsViewModel SettingsModel => _settingsViewModel;
        public AndroidLauncherUpdater? AndroidUpdates => AndroidLauncherUpdater.Current;
        public IBrush WindowBackground => this.Resources["ThemeDarker"] as IBrush ?? Brushes.Transparent;
        public bool IsDesktopPlatform => !PlatformCapabilities.IsMobile;
        public bool ShowMinimizeButton => IsDesktopPlatform && !SteamDeckEnvironment.IsGamingMode();
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
            _markdownRenderer = new MarkdownRenderer(OpenUrl);
            _themeEditor = new ThemeEditor(secondary => secondary ? Shell.SecondaryColorBrush.Color : Shell.ThemeColorBrush.Color, ApplyThemeColor, _dialogs);
            InitializeComponent();
            if (IsDesktopPlatform)
            {
                HeaderTitleColumn.ColumnDefinitions = new ColumnDefinitions("*,Auto");
                HeaderTitleColumn.ClipToBounds = true;
                CatalogReviewCompactSummary.MaxWidth = 180;
                _desktopHeader = new Views.DesktopHeaderLayout(HeaderLayoutGrid, HeaderTitleColumn, DesktopInlineTopBar, HeaderFixedActions,
                    () => LibraryToolbar.IsVisible ? LibraryToolbar.PreferredWidth : CatalogReviewBackButton.IsVisible ? CatalogReviewBackButton.DesiredSize.Width : 0);
                _session.OnShutdown(_desktopHeader.Dispose);
                LibraryToolbar.PreferredSizeChanged += _desktopHeader.Refresh;
                _session.OnShutdown(() => LibraryToolbar.PreferredSizeChanged -= _desktopHeader.Refresh);
            }
            MessagePromptOverlay.Configure(_session);
            _prompts = new LauncherPromptService(_session, _dialogs, MessagePromptOverlay);
            _mobileLayout = new Views.MobileShellLayout(this, _session, () => _appearance?.ApplyHeader());
            _appearance = new Views.ShellAppearance(this, Shell, _mobileLayout, _catalogSyncViewModel, () => _catalogReview?.ActiveSource, () => _desktopHeader?.Refresh());
            _session.OnShutdown(_mobileLayout.Dispose);
            _chromeNavigation = new Views.ShellChromeNavigation(this, _session, this, Shell, () => _mobileLayout.IsSearchOpen, WireChromeXyFocusEdges);
            _navigationRouter = new ShellNavigationRouter(Shell, _gamepadNavigation, new Dictionary<GamepadNavigationZone, Func<IFeatureNavigationHandler>> { [GamepadNavigationZone.Sidebar] = () => _chromeNavigation, [GamepadNavigationZone.TopBar] = () => _chromeNavigation, [GamepadNavigationZone.AnnouncementBanner] = () => Banners, [GamepadNavigationZone.Library] = () => LibraryPanel.Navigation, [GamepadNavigationZone.CatalogSources] = () => CatalogSourcesPanel.Navigation, [GamepadNavigationZone.CatalogSourcesToolbar] = () => CatalogSourcesPanel.Navigation, [GamepadNavigationZone.CatalogSourcesFilters] = () => CatalogSourcesPanel.Navigation, [GamepadNavigationZone.CatalogSourceCardActions] = () => CatalogSourcesPanel.Navigation, [GamepadNavigationZone.CatalogReviewFilters] = () => CatalogReviewPanel.Navigation, [GamepadNavigationZone.CatalogReviewList] = () => CatalogReviewPanel.Navigation, [GamepadNavigationZone.CatalogReviewRowActions] = () => CatalogReviewPanel.Navigation, [GamepadNavigationZone.CatalogReviewDetailsOverlay] = () => CatalogReviewDetailsPanel, [GamepadNavigationZone.AppUpdatesReviewToolbar] = () => AppUpdatesReviewPanel, [GamepadNavigationZone.AppUpdatesReviewList] = () => AppUpdatesReviewPanel, [GamepadNavigationZone.AppUpdatesReviewRowActions] = () => AppUpdatesReviewPanel, [GamepadNavigationZone.ModsOverlayToolbar] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlayFilters] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlaySourceFilters] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlayList] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsOverlayRowActions] = () => ModsPanel.Navigation, [GamepadNavigationZone.ModsDetailsOverlay] = () => ModsPanel.Details, [GamepadNavigationZone.DisplayFilterOverlay] = () => DisplayFilterOverlay.Navigation, [GamepadNavigationZone.EntryFormOverlay] = () => EntryFormOverlay.Navigation, [GamepadNavigationZone.TagEditOverlay] = () => TagEditOverlay.Navigation, [GamepadNavigationZone.Settings] = () => SettingsPanel.Navigation, [GamepadNavigationZone.ChangelogOverlay] = () => ChangelogPanel, }, () => IsDisplayFilterOverlayOpen, () =>
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
            ChangelogPanel.NavigationHost = this;
            ChangelogPanel.ConfigureRenderer(_markdownRenderer);
            ChangelogPanel.CloseRequested += CloseChangelog;
            if (MinimizeButton != null)
                MinimizeButton.IsVisible = !SteamDeckEnvironment.IsGamingMode();
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
            CatalogPerformance.Enabled = File.Exists(Path.Combine(QuiverLauncherPaths.UserDataRoot, "catalog-performance.enabled"));
            if (CatalogPerformance.Enabled)
            {
                _ = _session.RunAsync(() => Task.Run(async () =>
                {
                    while (!_session.Token.IsCancellationRequested)
                    {
                        await Task.Delay(50, _session.Token).ConfigureAwait(false);
                        var queued = Stopwatch.GetTimestamp();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var delay = Stopwatch.GetElapsedTime(queued).TotalMilliseconds;
                            if (delay > 100) CatalogPerformance.Report("ui-dispatch-delay", delay);
                        }, DispatcherPriority.Send, _session.Token);
                    }
                }, _session.Token));
            }
            Banners.Configure(_session, _settingsViewModel, _gameManager.HttpClient, this, OpenGitHubApiTokenSettings);
            Library = new LibraryViewModel(_gameManager, _settingsViewModel);
            LibraryToolbar.Configure(Library, _session, OnSettingChanged);
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
            _updates = new LauncherUpdateWorkflow(_gameManager, _settingsViewModel, Library, Shell, _session, _prompts, this, () => _app, _velopackUpdateService, () => ModsPanel.Workspace.RefreshAllModUpdateBadgesAsync(), game => _libraryLaunch.HandleUpdateNowAsync(this, game, preferAutoPlatform: true, allowAssetPicker: false));
            _updateChecks = new UpdateCheckCoordinator(_updates);
            _updates.CatalogsRefreshed = RefreshCatalogSources;
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
            _libraryActions = new LibraryActions(_gameManager, _libraryPersistence, _settingsViewModel, _session, Library, () => StorageProvider, (message, title, question, cancel) => ShowMessageBoxAsync(message, title, question, cancel), OpenUrl, async () =>
            {
                RefreshCatalogSources();
                if (_activeCatalogSyncSource != null)
                    await _catalogReview.RefreshAsync(_session.Token);
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
            _catalogReview = new CatalogReviewWorkspace(_catalogSyncViewModel, _settingsViewModel, new CatalogReviewService(_gameManager, _settingsViewModel, ApplySorting, _session), (message, title, question) => ShowMessageBoxAsync(message, title, question), async () =>
            {
                await _catalogViewModel.RefreshAsync(_session.Token);
                await ApplyLibraryCatalogPendingBadgesAsync();
            }, _session.CatalogMutations, () =>
            {
                _catalogViewModel.RefreshPresentation();
                return Task.CompletedTask;
            });
            _catalogReleasePrefetch = new CatalogReleasePrefetch(_session, _gameManager, _settingsViewModel, _catalogSyncViewModel, _catalogReview, () => Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review, action => Dispatcher.UIThread.InvokeAsync(action).GetTask(), CatalogReviewPanel.StagePlatformDiscoveries,
                reviewStatusChanged: _catalogViewModel.RefreshPresentation);
            _session.OnShutdown(_catalogReleasePrefetch.Dispose);
            _catalogReview.SourceOpened += ShowAppCatalogReviewView;
            _catalogReview.RowsChanged += CatalogRowsChanged;
            _catalogReview.AdditionRowsChanged += CatalogReviewPanel.RefreshPresentedCatalog;
            _settingsViewModel.CredentialsChanged += CatalogCredentialsChanged;
            _session.OnShutdown(() =>
            {
                _catalogReview.SourceOpened -= ShowAppCatalogReviewView;
                _catalogReview.RowsChanged -= CatalogRowsChanged;
                _catalogReview.AdditionRowsChanged -= CatalogReviewPanel.RefreshPresentedCatalog;
                _settingsViewModel.CredentialsChanged -= CatalogCredentialsChanged;
                _catalogReview.Dispose();
            });
            CatalogReviewPanel.Configure(_catalogSyncViewModel, _catalogReview, _settingsViewModel, _session, this, CatalogReviewDetailsPanel, () => Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review, message => LogGamepadDebug(message));
            CatalogReviewPanel.HeaderChanged += _appearance.ApplyHeader;
            CatalogReviewPanel.LibraryRequested += ShowLibraryView;
            CatalogReviewPanel.SourcesRequested += ShowAppCatalogSourcesView;
            CatalogReviewPanel.GitHubTokenSettingsRequested += OpenGitHubApiTokenSettings;
            CatalogReviewPanel.GitLabTokenSettingsRequested += OpenGitLabApiTokenSettings;
            CatalogReviewPanel.PlatformRetryRequested += _catalogReleasePrefetch.Restart;
            CatalogReviewPanel.DetailsRequested += OpenCatalogReviewDetails;
            CatalogReviewPanel.DetailsRefreshRequested += RefreshCatalogReviewDetailsBinding;
            _catalogViewModel.Configure(_settingsViewModel, new CatalogSourcesService(_gameManager), (message, title, cancel) => ShowMessageBoxAsync(message, title, cancel), async () =>
            {
                await _gameManager.LoadGamesAsync();
                if (!_session.IsClosed)
                    ApplySorting();
            }, ApplyLibraryCatalogPendingBadgesAsync, async () =>
            {
                await _updates.TryPromptCatalogReviewAsync();
            });
            CatalogSourcesPanel.Configure(_catalogViewModel, _session, this, () => !Shell.SettingsOpen && Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Sources, async id =>
            {
                var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == id);
                await OpenCatalogReviewAsync(id, source is { PendingReviewCount: > 0 } ? CatalogReviewFilter.NeedsReview : CatalogReviewFilter.All);
            });
            _catalogViewModel.ListChanged += RefreshCatalogBadgeCounts;
            _session.OnShutdown(() => _catalogViewModel.ListChanged -= RefreshCatalogBadgeCounts);
            CatalogReviewDetailsPanel.Configure(_session, this, _markdownRenderer, new CatalogReadmeService(_gameManager, _settingsViewModel).LoadAsync, OpenUrl);
            CatalogReviewDetailsPanel.CloseRequested += () => CloseCatalogReviewDetails();
            CatalogReviewDetailsPanel.ActionRequested += (action, id) => _ = _session.RunAsync(() => _catalogReview.ExecuteAsync(action, id, _session.Token));
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
            switch (action)
            {
                case Views.LibraryActionKind.EmptyLibraryAddApp:
                    ShowEntryFormOverlay(forCreate: true);
                    break;
                case Views.LibraryActionKind.EmptyLibraryBrowseCatalog:
                    await OpenCommunityCatalogFromLibraryAsync();
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
                case Views.LibraryActionKind.ReviewCatalogChanges when game != null:
                    await OpenCatalogReviewForLibraryAppAsync(game);
                    break;
                case Views.LibraryActionKind.ShowReadme:
                    if (string.IsNullOrEmpty(game?.Repository))
                        await ShowMessageBoxAsync("Unable to retrieve README.", "Error");
                    else
                        await ShowLibraryReadmeAsync(game);
                    break;
                case Views.LibraryActionKind.ShowChangelog:
                    if (string.IsNullOrEmpty(game?.Repository))
                        await ShowMessageBoxAsync("Unable to retrieve changelog information.", "Error");
                    else
                        await ShowChangelogAsync(game);
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

        void IUpdatePresentation.OpenCatalogSources()
        {
            if (!_session.IsClosed)
                ShowAppCatalogSourcesView();
        }

        Task IUpdatePresentation.OpenCatalogReviewAsync() => _session.IsClosed ? Task.CompletedTask : OpenAppCatalogForReviewAsync();
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
            if (_navigationRouter.ConfirmFeature(AllowChromeActions))
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
            if (Shell.DocumentOpen)
            {
                _inputService?.TryHandleContextMenuOptionsDismiss();
                ChangelogPanel.Cancel();
                return;
            }

            if (Shell.ModDetailsOpen)
            {
                ModsPanel.Details.Cancel();
                return;
            }

            if (Shell.CatalogDetailsOpen)
            {
                CatalogReviewDetailsPanel.Cancel();
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
            CatalogReviewPanel.ApplyCatalogReviewSortSelection(_settings.CatalogReviewSortBy ?? "Name");
            CatalogReviewPanel.UpdateCatalogReviewPlatformButton();
            CatalogReviewPanel.UpdateCatalogReviewLayoutVisibility();
            UpdateGridLayoutVisibility();
            Shell.ThemeColorBrush = new SolidColorBrush(Color.Parse(_settings.PrimaryColor ?? "#18181b"));
            Shell.SecondaryColorBrush = new SolidColorBrush(Color.Parse(_settings.SecondaryColor ?? "#404040"));
            UpdateThemeColors();
            UpdateGamepadHintsBar();
            RefreshCatalogSources();
            Library.RefreshFilters();
            LibraryFiltersPanel.RefreshSidebarFilterSelection();
        }

        void ISettingsFeatureHost.NotifyPresentationChanged()
        {
            Shell.RefreshPresentation(_settings);
            _music.Path = _settings.LauncherMusicPath ?? string.Empty;
            _music.Volume = _settings.MusicVolume;
            OnPropertyChanged(nameof(WindowBackground));
        }

        void ISettingsFeatureHost.ApplyTopBanner() => Banners.ApplyTopBanner();
        void ISettingsFeatureHost.CloseSettings() => CloseSettingsPanel();
        void ISettingsFeatureHost.EditTheme(bool secondary) => _ = _session.RunAsync(() => secondary ? _themeEditor.ShowSecondaryAsync() : _themeEditor.ShowPrimaryAsync());
        void ISettingsFeatureHost.UpdateGridLayoutVisibility() => UpdateGridLayoutVisibility();
        void ISettingsFeatureHost.ApplyLibraryDisplaySettingsToGames() => Library.ApplyDisplaySettings();
        void ISettingsFeatureHost.FitMobileLibraryCardWidth() => LibraryPanel.FitMobileLibraryCardWidth();
        void ISettingsFeatureHost.ApplySorting() => ApplySorting();
        Task ISettingsFeatureHost.ApplyLibraryCatalogPendingBadgesAsync() => ApplyLibraryCatalogPendingBadgesAsync();
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
            await _gameManager.LoadGamesAsync();
            if (!_session.IsClosed)
                ApplySorting();
        }

        private void ShowTagEditOverlay(GameInfo game, MetadataEditMode mode = MetadataEditMode.Tags)
        {
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
            if (Shell.SettingsOpen)
                CloseSettingsPanel();
            if (Shell.DocumentOpen)
                CloseChangelog();
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
            _themeEditor.ApplyResources(Resources, Shell.ThemeColorBrush.Color, Shell.SecondaryColorBrush.Color);
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
            UpdateGamepadChromeClass();
            _mobileLayout.Attach();
            if (HostWindow == null && !_hasInitializedFocus)
                HandleOpened();
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

        private void MobileCatalogSortFlyout_Opening(object? sender, EventArgs e)
        {
            GamepadMenuFlyoutNavigation.Attach(sender as MenuFlyout);
            CatalogReviewPanel.SortFlyoutOpening(sender as MenuFlyout);
        }

        private void MobileLibrarySortItem_Click(object? sender, RoutedEventArgs e) => LibraryToolbar.SelectSort((sender as MenuItem)?.Tag as string);
        private void MobileCatalogSortItem_Click(object? sender, RoutedEventArgs e) => CatalogReviewPanel.SelectSort((sender as MenuItem)?.Tag as string);
        internal async Task InitializeGamesAsync()
        {
            try
            {
                // Startup must not wait for catalog servers, release checks, or rate-limit cooldowns.
                await _gameManager.ReloadLibraryFromDiskAsync(allowNetwork: false);
                if (_session.IsClosed)
                    return;
                _settings = _settingsViewModel.Load();
                await RefreshAllCatalogPendingCountsAsync();
                if (_session.IsClosed)
                    return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_session.IsClosed)
                        return;
                    ApplySorting();
                    Library.RefreshContinue();
                    RefreshCatalogSources();
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
                // The saved library and cached source counts are already usable.
                await RefreshStartupMetadataAsync();
                if (_session.IsClosed) return;
                await _updates.NotifyCatalogUpdatesIfNeededAsync();
                _ = _session.RunAsync(Banners.RefreshAnnouncementBannerAsync);
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (_session.IsClosed)
                        return;
                    UpdateGameCollectionUi();
                    await ShowMessageBoxAsync($"Failed to load apps: {ex.Message}", "Load Error");
                });
            }
        }

        private async Task RefreshStartupMetadataAsync()
        {
            try
            {
                await _gameManager.CatalogService.RefreshAllSourcesAsync(_gameManager.HttpClient, _settings);
                if (_session.IsClosed) return;
                await _gameManager.CatalogService.EnsureCommunitySourcesCachedAsync(_gameManager.HttpClient, _settings);
                if (_session.IsClosed) return;
                _settingsViewModel.Save(_settings);
                await RefreshAllCatalogPendingCountsAsync();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_session.IsClosed) RefreshCatalogSources();
                });
                await _gameManager.RefreshLoadedLibraryMetadataAsync(_session.Token);
            }
            catch (OperationCanceledException) when (_session.IsClosed) { }
            catch (Exception ex)
            {
                // An online refresh failure must not replace a successfully loaded library.
                System.Diagnostics.Debug.WriteLine($"Startup metadata refresh failed: {ex.GetType().Name}");
            }
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

        private void HandleGamepadConnectionChanged(bool hasConnected)
        {
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

                if (IsGamepadFocusActive && Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Sources && CatalogSources.Count > 0)
                {
                    CatalogSourcesPanel.Navigation.SelectInitialCatalogGamepadItem();
                    return;
                }

                // Try to focus the Continue button if visible
                if (Library.IsContinueVisible && this.FindControl<Button>("ContinueButton")is Button continueBtn)
                {
                    continueBtn.Focus();
                    return;
                }

                // Try Library nav button
                if (this.FindControl<Button>("LibraryNavButton")is Button libraryBtn)
                {
                    libraryBtn.Focus();
                    return;
                }

                // Fallback to first focusable control
                var firstFocusable = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable);
                firstFocusable?.Focus();
            }, DispatcherPriority.Loaded);
        }

        public void CloseLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (CloseSettingsPanel())
                return;
            // Close changelog if open
            if (Shell.DocumentOpen)
            {
                CloseChangelog();
                return;
            }

            if (Shell.ModDetailsOpen)
            {
                ModsPanel.Details.Close();
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
            if (SettingsPanel == null)
                return;
            if (Shell.EntryEditorOpen)
                CloseEntryFormOverlay();
            if (Shell.TagEditorOpen)
                CloseTagEditOverlay();
            if (Shell.CatalogDetailsOpen)
                CloseCatalogReviewDetails(restoreReviewSelection: false);
            Shell.SettingsOpen = !Shell.SettingsOpen;
            SettingsPanel.IsVisible = Shell.SettingsOpen;
            if (Shell.SettingsOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;
                NotifyGamepadUiChanged();
                CatalogSourcesPanel.Navigation.ClearCatalogSourcesToolbarGamepadFocus();
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
            if (Shell.CatalogDetailsOpen)
                CloseCatalogReviewDetails(restoreReviewSelection: false);
            Shell.SettingsOpen = true;
            SettingsPanel.IsVisible = true;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;
            ClearGamepadFocus();
            NotifyGamepadUiChanged();
            RefreshCatalogSources();
            SettingsPanel.FocusApiToken(focusGitLab);
        }

        private string? _catalogCredentialContext;
        private bool CloseSettingsPanel()
        {
            if (!Shell.SettingsOpen || SettingsPanel == null)
                return false;
            Shell.SettingsOpen = false;
            SettingsPanel.IsVisible = false;
            SettingsPanel.Bindings.Cancel();
            SettingsPanel.Navigation.ClearSettingsGamepadFocusClasses(SettingsPanel.Navigation.CollectSettingsFocusableControls());
            SettingsPanel.Navigation.FocusIndex = -1;
            _gamepadNavigation.ActiveZone = Shell.Mode == MainViewMode.Library ? GamepadNavigationZone.Library : GamepadNavigationZone.CatalogSources;
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

        private void RefreshCatalogSources() => _ = _session.RunAsync(() => _catalogViewModel.RefreshAsync(_session.Token));
        private void RefreshCatalogBadgeCounts()
        {
            if (_session.IsClosed)
                return;
            Shell.CatalogReviewBadgeCount = CatalogSources.Where(source => source.Enabled).Sum(source => source.PendingReviewCount);
        }

        private void LibraryNavButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowLibraryView();
            _mobileLayout.CloseMobileNav();
        }

        private void AppCatalogNavButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowAppCatalogSourcesView();
            _mobileLayout.CloseMobileNav();
        }

        private void CatalogReviewBack_Click(object? sender, RoutedEventArgs e) => ShowAppCatalogSourcesView();
        private void ShowLibraryView()
        {
            if (Shell.CatalogSubView == AppCatalogSubView.Review)
            {
                _catalogReleasePrefetch.Cancel();
                _catalogReview.Close();
            }

            Shell.Mode = MainViewMode.Library;
            Shell.CatalogSubView = AppCatalogSubView.Sources;
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

        private void ShowAppCatalogSourcesView()
        {
            if (Shell.CatalogDetailsOpen)
                CloseCatalogReviewDetails(restoreReviewSelection: false);
            _catalogReleasePrefetch.Cancel();
            _catalogReview.Close();
            Shell.Mode = MainViewMode.AppCatalog;
            Shell.CatalogSubView = AppCatalogSubView.Sources;
            Shell.AppUpdatesOpen = false;
            if (Shell.ModsOpen)
                ModsPanel.CloseModsOverlay();
            CatalogSyncRows.Clear();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();
            if (IsGamepadFocusActive)
                CatalogSourcesPanel.Navigation.SelectInitialCatalogGamepadItem();
            else
                ClearGamepadFocus();
        }

        private void ShowAppCatalogReviewView(AppCatalogSource source)
        {
            Shell.Mode = MainViewMode.AppCatalog;
            Shell.CatalogSubView = AppCatalogSubView.Review;
            Shell.AppUpdatesOpen = false;
            CatalogReviewPanel.ResetSearch();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();
            CatalogReviewPanel.ApplyCatalogReviewChrome();
            _appearance.ApplyHeader();
            if (IsGamepadFocusActive)
                CatalogReviewPanel.Navigation.SelectInitialCatalogReviewGamepadItem();
            else
                ClearGamepadFocus();
        }

        private void OpenAppUpdatesReview()
        {
            if (Shell.ModsOpen)
                ModsPanel.CloseModsOverlay();
            Shell.Mode = MainViewMode.Library;
            Shell.CatalogSubView = AppCatalogSubView.Sources;
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
            if (!(Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review) && Shell.CatalogDetailsOpen)
                CloseCatalogReviewDetails(restoreReviewSelection: false);
            _appearance.Refresh();
            UpdateLibraryEmptyState();
            NotifyGamepadUiChanged();
        }

        private void UpdateLibraryEmptyState()
        {
            LibraryPanel.UpdateEmptyState(Shell.Mode == MainViewMode.Library && !Shell.AppUpdatesOpen && !Shell.ModsOpen);
            LibraryToolbar.RefreshClearButton();
        }

        private Task OpenCommunityCatalogFromLibraryAsync()
        {
            AppCatalogService.MigrateLegacyCatalogSources(_settings);
            OnSettingChanged();
            ShowAppCatalogSourcesView();
            return Task.CompletedTask;
        }

        private async Task RefreshAllCatalogPendingCountsAsync()
        {
            foreach (var source in _settings.AppCatalogSources.Where(s => s.Enabled))
                await _gameManager.CatalogService.RefreshUpdateAvailableAsync(source);
            RefreshCatalogSources();
            await ApplyLibraryCatalogPendingBadgesAsync();
        }

        private async Task ApplyLibraryCatalogPendingBadgesAsync()
        {
            if (_gameManager?.Games == null || _settings == null)
                return;
            await _gameManager.CatalogService.ApplyPendingCatalogChangeFlagsAsync(_gameManager.Games, _settings);
        }

        private async Task OpenAppCatalogForReviewAsync()
        {
            ShowAppCatalogSourcesView();
            var source = _settings.AppCatalogSources.Where(s => s.Enabled && s.PendingReviewCount > 0).OrderByDescending(s => s.PendingReviewCount).FirstOrDefault();
            if (source != null)
                await OpenCatalogReviewAsync(source.Id, CatalogReviewFilter.NeedsReview);
        }

        private static string GetCatalogReviewFilterTag(CatalogReviewFilter filter)
        {
            if (filter == CatalogReviewFilter.New)
                return "NotInLibrary";
            foreach (var(tag, value)in Views.CatalogReviewView.CatalogReviewFilters)
            {
                if (value == filter)
                    return tag;
            }

            return "All";
        }

        private async Task OpenCatalogReviewForLibraryAppAsync(GameInfo game)
        {
            if (_settings == null || _gameManager == null)
                return;
            var sourceId = await _gameManager.CatalogService.FindPendingCatalogSourceIdAsync(game, _settings);
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                await ShowMessageBoxAsync("Could not find this app in a catalog source with pending changes.", "Catalog Review");
                return;
            }

            await OpenCatalogReviewAsync(sourceId, CatalogReviewFilter.NeedsReview);
            var index = CatalogCompareService.FindRowIndexForLibraryApp(CatalogSyncRows, game);
            if (index < 0)
                return;
            CatalogReviewPanel.Navigation.ApplyCatalogReviewRowSelection(index);
            OpenCatalogReviewDetails(CatalogSyncRows[index]);
        }

        private async Task OpenCatalogReviewAsync(string sourceId, CatalogReviewFilter? initialFilter = null)
        {
            var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null || _session.IsClosed)
                return;
            _catalogReleasePrefetch.Cancel();
            CatalogReviewPanel.ResetSearch();
            CatalogReviewPanel.EnsureCatalogPlatformFilterDefault();
            CatalogReviewPanel.ReplaceCatalogSyncRows([]);
            var filter = initialFilter ?? (source.PendingReviewCount > 0 || source.UpdateAvailable ? CatalogReviewFilter.NeedsReview : CatalogReviewFilter.All);
            await _catalogReview.OpenAsync(source, filter, _session.Token);
        }

        private CatalogSyncRowItem? FindCatalogSyncRow(string key) => _catalogReview.FindRow(key);
        private void CatalogCredentialsChanged(string provider)
        {
            if (_session.IsClosed) return;
            // Remove inaccessible private evidence immediately; accepted public results survive the change.
            CatalogReviewPanel.RefreshPresentedCatalog();
        }
        private void CatalogRowsChanged()
        {
            if (_session.IsClosed)
                return;
            CatalogReviewPanel.RefreshCatalogReviewFilterButtons(GetCatalogReviewFilterTag(_catalogSyncViewModel.ReviewFilter));
            CatalogReviewPanel.RefreshPresentedCatalog();
            CatalogReviewPanel.UpdateCatalogReviewPlatformButton();
            CatalogReviewPanel.UpdateCatalogSyncBulkButtons();
            _catalogCredentialContext = ReleaseRequestCoordinator.CredentialKey(_settings.GitHubApiToken) + ":" + ReleaseRequestCoordinator.CredentialKey(_settings.GitLabApiToken);
            _catalogReleasePrefetch.Start();
        }

        private void OpenCatalogReviewDetails(CatalogSyncRowItem row)
        {
            Shell.CatalogDetailsOpen = true;
            CatalogReviewDetailsPanel.Open(row);
        }

        private void CloseCatalogReviewDetails(bool restoreReviewSelection = true)
        {
            Shell.CatalogDetailsOpen = false;
            CatalogReviewDetailsPanel.Close();
            if (restoreReviewSelection && _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewDetailsOverlay)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewList;
                if (IsGamepadFocusActive)
                    CatalogReviewPanel.Navigation.SelectInitialCatalogReviewGamepadItem();
            }
        }

        private void RefreshCatalogReviewDetailsBinding()
        {
            if (!Shell.CatalogDetailsOpen || string.IsNullOrWhiteSpace(CatalogReviewDetailsPanel.Model.Row?.IdentityKey))
                return;
            var row = CatalogSyncRows.FirstOrDefault(r => string.Equals(r.IdentityKey, CatalogReviewDetailsPanel.Model.Row?.IdentityKey, StringComparison.OrdinalIgnoreCase));
            if (row == null)
                CloseCatalogReviewDetails();
            else
                CatalogReviewDetailsPanel.Bind(row);
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

        private async void CheckforUpdates_Click(object sender, RoutedEventArgs e) => await RunUpdateCheckAsync(promptForReview: true, isManualCheck: true);
        private void CancelUpdateCheck_Click(object? sender, EventArgs e) { CheckForUpdatesButton.Focus(); _updateChecks.Cancel(); }
        private async void RetryUpdateCheck_Click(object? sender, EventArgs e) => await RunUpdateCheckAsync(true, true);
        /// <summary>
        /// Shared update check used by the toolbar button, tray menu, and background timer.
        /// </summary>
        public Task RunUpdateCheckAsync(bool promptForReview, bool isManualCheck) => _session.RunAsync(async () =>
        {
            if (isManualCheck && Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review)
                _catalogReleasePrefetch.RefreshAll();
            await _updateChecks.CheckAsync(promptForReview, isManualCheck, _session.Token);
        });
        private void GithubButton_Click(object sender, RoutedEventArgs e)
        {
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
            var padHints = GamepadBindingLabels.FormatHints(_settings.GamepadBindings);
            var keyHints = KeyboardBindingLabels.FormatHints(_settings.KeyboardBindings);
            GamepadHintsBar.Text = _settings.EnableGamepadInput ? $"{padHints}  |  {keyHints}" : keyHints;
        }

        private void SetIgnoreArticlesWhenSorting(bool enabled)
        {
            if (_settings == null)
                return;
            _settings.IgnoreArticlesWhenSorting = enabled;
            OnSettingChanged();
            ApplySorting();
            if (Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review)
            {
                _catalogSyncViewModel.IgnoreArticlesWhenSorting = enabled;
                CatalogReviewPanel.ApplyCatalogSyncFilter();
            }
        }

        private void AddNewEntryButton_Click(object? sender, RoutedEventArgs e)
        {
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
            if (CatalogReviewPanel != null)
                XyFocusNavigation.EnableOn(CatalogReviewPanel);
            if (ModDetailsPanel != null)
                XyFocusNavigation.EnableOn(ModDetailsPanel);
            var topBarRoot = _chromeNavigation.GetActiveTopBarRoot();
            if (topBarRoot != null)
                XyFocusNavigation.EnableOn(topBarRoot);
            var sidebar = _chromeNavigation.CollectSidebarFocusableControls();
            var topBar = _chromeNavigation.CollectTopBarControls();
            if (sidebar.Count == 0 || topBar.Count == 0)
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
            _gamepadNavigation.CatalogReviewFilterIndex = -1;
            _gamepadNavigation.CatalogReviewSelectedIndex = -1;
            _gamepadNavigation.CatalogReviewRowActionIndex = -1;
            _gamepadNavigation.AppUpdatesReviewToolbarIndex = -1;
            _gamepadNavigation.AppUpdatesReviewSelectedIndex = -1;
            _gamepadNavigation.AppUpdatesReviewRowActionIndex = -1;
            _gamepadNavigation.CatalogSourcesToolbarSelectedIndex = -1;
            _gamepadNavigation.CatalogSourcesFilterIndex = -1;
            _gamepadNavigation.CatalogSourceCardActionIndex = -1;
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
            else if (Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Sources)
                CatalogSourcesPanel.Navigation.SelectInitialCatalogGamepadItem();
            else if (Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review)
                CatalogReviewPanel.Navigation.SelectInitialCatalogReviewGamepadItem();
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
            foreach (var source in CatalogSources)
                source.IsGamepadFocused = false;
            foreach (var row in CatalogSyncRows)
                row.IsGamepadFocused = false;
            _chromeNavigation.ClearTopBarGamepadFocusClasses(_chromeNavigation.CollectTopBarControls());
            _chromeNavigation.ClearSidebarGamepadFocusClasses(_chromeNavigation.CollectSidebarFocusableControls());
            Banners.ClearAnnouncementBannerGamepadFocus();
            CatalogSourcesPanel.Navigation.ClearCatalogSourcesToolbarGamepadFocus();
            CatalogSourcesPanel.Navigation.ClearCatalogSourcesFiltersGamepadFocus();
            CatalogSourcesPanel.Navigation.ClearCatalogSourceCardActionsGamepadFocus();
            CatalogReviewPanel.Navigation.ClearCatalogReviewRowActionsGamepadFocus();
            CatalogReviewPanel.Navigation.ClearCatalogReviewEmptyActionGamepadFocus();
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

        private async Task ReviewCatalogSourceByIdAsync(string sourceId)
        {
            var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == sourceId);
            var filter = source is { PendingReviewCount: > 0 } ? CatalogReviewFilter.NeedsReview : CatalogReviewFilter.All;
            await OpenCatalogReviewAsync(sourceId, filter);
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
            ChangelogPanel.Model.Cancel();
            _catalogReleasePrefetch.Cancel();
            CatalogReviewDetailsPanel.Model.Document.Cancel();
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
                _navigationRouter.RestoreCurrentFocus();
        }

        private Task ShowChangelogAsync(GameInfo game) => ShowLibraryDocumentAsync(game, readme: false);
        private Task ShowLibraryReadmeAsync(GameInfo game) => ShowLibraryDocumentAsync(game, readme: true);
        private Task ShowLibraryDocumentAsync(GameInfo game, bool readme) => _session.RunAsync(async () =>
        {
            _inputService?.TryHandleContextMenuOptionsDismiss();
            Shell.DocumentOpen = true;
            OnPropertyChanged(nameof(GamepadHintsVisible));
            HeaderTitleText.Text = readme ? $"{game.DisplayName} README" : $"{game.Name} - Version {game.LatestVersion ?? "Unknown"}";
            try
            {
                await ChangelogPanel.OpenAsync(readme ? $"{game.DisplayName} README" : $"{game.Name} Changelog", readme ? "Loading README..." : "Loading changelog...", token => LibraryDocumentService.LoadAsync(game, readme, _settings, _gameManager.HttpClient, _repositoryReadmeService, token), _session.Token);
            }
            catch (Exception ex)
            {
                if (_session.IsClosed)
                    return;
                await ShowMessageBoxAsync($"Failed to load {(readme ? "README" : "changelog")}: {ex.Message}", "Error");
                CloseChangelog();
            }
        });
        private void CloseChangelog()
        {
            ChangelogPanel.Close();
            Shell.DocumentOpen = false;
            UpdateMainViewUi();
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.ChangelogOverlay)
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                if (Shell.Mode == MainViewMode.Library)
                    LibraryPanel.Navigation.RestoreLibraryGamepadFocusAfterMenu();
                else
                    SelectInitialGamepadItemForCurrentView();
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
