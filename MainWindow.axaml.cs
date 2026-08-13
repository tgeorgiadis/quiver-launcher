using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Platform;

#if WINDOWS
using NAudio.Wave;
#endif

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

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly GameManager _gameManager;
        private readonly CatalogViewModel _catalogViewModel = new();
        private readonly GameGridViewModel _gameGridViewModel = new();
        private readonly SettingsViewModel _settingsViewModel = new();
        public ObservableCollection<GameInfo> Games => _gameManager?.Games ?? new ObservableCollection<GameInfo>();
        public bool IsLibraryEmpty => _gameManager.IsLibraryEmpty;
        public ObservableCollection<TagDisplayFilterListItem> TagDisplayFilters { get; } = new();
        public ObservableCollection<CatalogSourceListItem> CatalogSources { get; } = new();
        public ObservableCollection<CatalogSyncRowItem> CatalogSyncRows { get; } = new();
        public ObservableCollection<TagChipListItem> CatalogTagChips => _catalogSyncViewModel.TagChips;
        public bool HasCatalogTagChips => CatalogTagChips.Count > 0;
        public ObservableCollection<GameInfo> AppUpdateReviewRows { get; } = new();
        public ObservableCollection<ModListItem> ModListRows { get; } = new();

        public int CatalogReviewBadgeCount =>
            CatalogSources.Where(s => s.Enabled).Sum(s => s.PendingReviewCount);

        public bool CatalogReviewBadgeVisible => CatalogReviewBadgeCount > 0;

        public bool GamepadHintsVisible =>
            !isSettingsPanelOpen &&
            !_isEntryFormOpen &&
            !_isTagEditOpen &&
            !_isChangelogOpen &&
            !IsDisplayFilterOverlayOpen &&
            !_isModsOverlayOpen &&
            !_isModDetailsOpen &&
            (_mainViewMode == MainViewMode.Library ||
             (_mainViewMode == MainViewMode.AppCatalog &&
              (_appCatalogSubView == AppCatalogSubView.Sources ||
               _appCatalogSubView == AppCatalogSubView.Review)));

        /// <summary>
        /// Gamepad chrome actions (zones, overlays, library confirm) when pad input is enabled,
        /// or after keyboard navigation/actions have activated keyboard chrome.
        /// </summary>
        private bool AllowChromeActions =>
            _settings.EnableGamepadInput || GamepadFocusChrome.KeyboardNavigationActive;

        private readonly LauncherUpdateService _launcherUpdateService = new();
        private readonly VelopackUpdateService _velopackUpdateService = new();
        private bool _isCheckingUpdates;
        private int _pendingUpdatesCount;
        private DateTime? _lastUpdateCheckTime;
        private string? _lastLauncherCheckNote;
        private bool _forceExit;
        private DispatcherTimer? _backgroundUpdateTimer;
        private bool _isBackgroundUpdateTickRunning;

        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            private set
            {
                if (_isCheckingUpdates == value)
                    return;

                _isCheckingUpdates = value;
                OnPropertyChanged(nameof(IsCheckingUpdates));
                NotifyUpdateCheckUiProperties();
            }
        }

        public int PendingUpdatesCount
        {
            get => _pendingUpdatesCount;
            private set
            {
                if (_pendingUpdatesCount == value)
                    return;

                _pendingUpdatesCount = value;
                OnPropertyChanged(nameof(PendingUpdatesCount));
                NotifyUpdateCheckUiProperties();
            }
        }

        public bool UpdatesBadgeVisible => PendingUpdatesCount > 0 && !IsCheckingUpdates;

        public bool UpdatesUpToDateBadgeVisible =>
            !IsCheckingUpdates &&
            PendingUpdatesCount == 0 &&
            LastUpdateCheckTime != null &&
            string.IsNullOrEmpty(_lastLauncherCheckNote);

        public string UpdatesBadgeText =>
            PendingUpdatesCount > 9 ? "9+" : PendingUpdatesCount.ToString();

        public double CheckForUpdatesIconOpacity => IsCheckingUpdates ? 0.55 : 1.0;

        public DateTime? LastUpdateCheckTime
        {
            get => _lastUpdateCheckTime;
            private set
            {
                if (_lastUpdateCheckTime == value)
                    return;

                _lastUpdateCheckTime = value;
                OnPropertyChanged(nameof(LastUpdateCheckTime));
                NotifyUpdateCheckUiProperties();
            }
        }

        public string CheckForUpdatesToolTip
        {
            get
            {
                if (IsCheckingUpdates)
                    return "Checking Quiver Launcher and apps…";

                var lastChecked = GetLastCheckedText();
                if (PendingUpdatesCount > 0)
                {
                    var updateLabel = PendingUpdatesCount == 1
                        ? "1 update available"
                        : $"{PendingUpdatesCount} updates available";
                    return string.IsNullOrEmpty(lastChecked)
                        ? $"Check for Quiver Launcher and app updates · {updateLabel}"
                        : $"Check for Quiver Launcher and app updates · {updateLabel} · {lastChecked}";
                }

                if (!string.IsNullOrEmpty(_lastLauncherCheckNote))
                {
                    return string.IsNullOrEmpty(lastChecked)
                        ? $"Check for Quiver Launcher and app updates · {_lastLauncherCheckNote}"
                        : $"Check for Quiver Launcher and app updates · {_lastLauncherCheckNote} · {lastChecked}";
                }

                if (!string.IsNullOrEmpty(lastChecked))
                    return $"Check for Quiver Launcher and app updates · Up to date · {lastChecked}";

                return "Check for Quiver Launcher and app updates";
            }
        }

        private void NotifyUpdateCheckUiProperties()
        {
            OnPropertyChanged(nameof(UpdatesBadgeVisible));
            OnPropertyChanged(nameof(UpdatesUpToDateBadgeVisible));
            OnPropertyChanged(nameof(UpdatesBadgeText));
            OnPropertyChanged(nameof(CheckForUpdatesIconOpacity));
            OnPropertyChanged(nameof(CheckForUpdatesToolTip));
            _app?.UpdateTrayTooltip(PendingUpdatesCount, IsCheckingUpdates);
        }

        private void RefreshUpdateCheckStatus(DateTime? manualCheckTime = null)
        {
            if (manualCheckTime.HasValue)
                LastUpdateCheckTime = manualCheckTime.Value;

            var launcherPending = (_app?.IsLauncherUpdatePending() ?? false)
                || _velopackUpdateService.IsUpdatePendingRestart;
            var gamePending = AppUpdateSelection.CountManualPendingUpdates(Games);
            PendingUpdatesCount = LauncherUpdateService.ComputePendingUpdatesCount(launcherPending, gamePending);
        }

        private readonly CatalogSyncViewModel _catalogSyncViewModel = new();
        private AppCatalogSource? _activeCatalogSyncSource;
        private MainViewMode _mainViewMode = MainViewMode.Library;
        private AppCatalogSubView _appCatalogSubView = AppCatalogSubView.Sources;
        private bool _isAppUpdatesReviewOpen;
        private string? _activeAnnouncementId;
        private bool _suppressCatalogSourceUiEvents;
        private bool _suppressSettingsUiEvents;
        private bool _isRefreshingCatalogSources;
        private string? _editingDisplayFilterId;
        private string? _tagFilterDragId;
        private double _tagFilterDragStartY;
        private double _tagFilterDragListTop;
        private double _tagFilterDragRowStride;
        private bool _tagFilterDragActive;
        private bool _tagFilterSuppressRowClick;
        private List<string>? _tagFilterOrderAtDragStart;
        private Button? _tagFilterDragRowButton;
        private IPointer? _tagFilterDragPointer;
        public AppSettings _settings = new();
        public App _app = null!;
        public AppSettings Settings => _settings;
        private bool isSettingsPanelOpen = false;
        public string IconFillStretch = "Uniform";
        private string _backgroundImagePath = string.Empty;
        private string _backgroundImageUri = string.Empty;
        public string BackgroundImagePath
        {
            get => _backgroundImagePath;
            set
            {
                if (_backgroundImagePath != value)
                {
                    _backgroundImagePath = value;
                    if (!string.IsNullOrEmpty(value) && File.Exists(value))
                    {
                        _backgroundImageUri = new Uri(value).AbsoluteUri;
                    }
                    else
                    {
                        _backgroundImageUri = string.Empty;
                    }
                    OnPropertyChanged(nameof(BackgroundImagePath));
                    OnPropertyChanged(nameof(BackgroundImageUri));
                }
            }
        }
        public string BackgroundImageUri => _backgroundImageUri ?? string.Empty;
        public float BackgroundOpacity
        {
            get => _settings.BackgroundOpacity;
            set
            {
                if (Math.Abs(_settings.BackgroundOpacity - value) > 0.001f)
                {
                    _settings.BackgroundOpacity = value;
                    OnPropertyChanged(nameof(BackgroundOpacity));
                }
            }
        }
        private System.Threading.CancellationTokenSource? _fadeTaskCts;
        private const int FADE_DURATION_MS = 500;
        #if WINDOWS
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFileReader;
        #endif
        private Process? _musicProcess;
        private bool _musicPausedByDeactivation = false;
        private bool _launchedGameOwnsInput;
        private bool _trackingLaunchedGameProcess;
        private WindowState _windowStateBeforeMinimize = WindowState.Normal;
        private bool _restoreWindowStateAfterMinimize;
        private string _launcherMusicPath = string.Empty;
        public string LauncherMusicPath
        {
            get => _launcherMusicPath;
            set
            {
                if (_launcherMusicPath != value)
                {
                    _launcherMusicPath = value;
                    OnPropertyChanged(nameof(LauncherMusicPath));
                }
            }
        }
        private float _musicVolume = 0.2f;
        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                if (Math.Abs(_musicVolume - value) > 0.001f)
                {
                    _musicVolume = value;
                    OnPropertyChanged(nameof(MusicVolume));

                #if WINDOWS
                if (_audioFileReader != null)
                {
                    _audioFileReader.Volume = value;
                }
                #else
                if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
                {
                    PlayLauncherMusic(LauncherMusicPath);
                }
                #endif
                }
            }
        }
        public bool IsFullscreen
        {
            get => _settings.StartFullscreen;
            set
            {
                if (_settings.StartFullscreen != value)
                {
                    _settings.StartFullscreen = value;
                    OnPropertyChanged(nameof(IsFullscreen));
                }
            }
        }
        public bool CloseAfterLaunch
        {
            get => _settings.CloseAfterLaunch;
            set
            {
                if (_settings.CloseAfterLaunch != value)
                {
                    _settings.CloseAfterLaunch = value;
                    OnPropertyChanged(nameof(CloseAfterLaunch));
                }
            }
        }
        public IBrush WindowBackground =>
            this.Resources["ThemeDarker"] as IBrush ?? Brushes.Transparent;
        public bool ExtendClientAreaEnabled => !_settings.ShowOSTopBar;
        public ExtendClientAreaChromeHints ChromeHints
        {
            get => _settings.ShowOSTopBar ? ExtendClientAreaChromeHints.PreferSystemChrome : ExtendClientAreaChromeHints.NoChrome;
        }
        private string _currentSortBy = "Name";
        private string _currentCatalogReviewSortBy = "Name";
        private string _currentVersionString = string.Empty;
        public string currentVersionString
        {
            get => _currentVersionString;
            set
            {
                if (_currentVersionString != value)
                {
                    _currentVersionString = value;
                    OnPropertyChanged(nameof(currentVersionString));
                }
            }
        }
        private string _platformstring = string.Empty;
        public string PlatformString
        {             
            get => _platformstring;
            set
            {
                if (_platformstring != value)
                {
                    _platformstring = value;
                    OnPropertyChanged(nameof(PlatformString));
                    OnPropertyChanged(nameof(IsLinuxPlatform));
                }
            }
        }
        public bool IsLinuxPlatform => PlatformString.Contains("Linux", StringComparison.OrdinalIgnoreCase);

        private bool _isContinueVisible;
        public bool IsContinueVisible
        {
            get => _isContinueVisible;
            set
            {
                if (_isContinueVisible != value)
                {
                    _isContinueVisible = value;
                    OnPropertyChanged(nameof(IsContinueVisible));
                }
            }
        }

        private InputService? _inputService;
        private GamepadAction? _rebindListeningAction;
        private GamepadAction? _keyboardRebindListeningAction;
        private Action<GamepadBinding>? _rawInputHandler;
        private readonly GamepadNavigationService _gamepadNavigation = new();
        private int _settingsGamepadFocusIndex = -1;
        private int _displayFilterGamepadFocusIndex = -1;
        private int _entryFormGamepadFocusIndex = -1;
        private int _changelogGamepadFocusIndex = -1;
        private bool _isProcessingInput = false;
        private bool _handlingGamepadConfirm = false;
        /// <summary>
        /// After Space/Enter confirm, suppress the matching KeyUp so a newly focused CheckBox
        /// (e.g. catalog Enabled) does not also toggle.
        /// </summary>
        private bool _suppressConfirmKeyUp;
        private bool _hasInitializedFocus = false;

        private bool _isChangelogOpen = false;
        private GameInfo? _currentChangelogGame;

        private GameInfo? _continueGameInfo;
        public GameInfo? ContinueGameInfo
        {
            get => _continueGameInfo;
            set
            {
                if (_continueGameInfo != value)
                {
                    _continueGameInfo = value;
                    OnPropertyChanged(nameof(ContinueGameInfo));
                }
            }
        }
        private bool _isEntryFormOpen = false;
        private bool _isTagEditOpen = false;
        private int _tagEditGamepadFocusIndex = -1;
        private bool _entryFormShowValidation;
        private GameInfo? _editingTagsGame;
        public string InfoTextLength = "*";
        private SolidColorBrush _themeColorBrush = new(Colors.Transparent);
        public SolidColorBrush ThemeColorBrush
        {
            get => _themeColorBrush;
            set
            {
                if (_themeColorBrush != value)
                {
                    _themeColorBrush = value;
                    OnPropertyChanged(nameof(ThemeColorBrush));
                    UpdateThemeColors();
                }
            }
        }
        private SolidColorBrush _secondaryColorBrush = new(Colors.Transparent);
        public SolidColorBrush SecondaryColorBrush
        {
            get => _secondaryColorBrush;
            set
            {
                if (_secondaryColorBrush != value)
                {
                    _secondaryColorBrush = value;
                    OnPropertyChanged(nameof(SecondaryColorBrush));
                    UpdateThemeColors();
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            if (MinimizeButton != null)
                MinimizeButton.IsVisible = !SteamDeckEnvironment.IsGamingMode();

            try
            {
                _settings = _settingsViewModel.Load();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to load settings: {ex.Message}", "Settings Error");
                _settings = new AppSettings();
            }

            _gameManager = new GameManager();
            GameManager.UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask();

            // Initialize theme
            ThemeColorBrush = new SolidColorBrush(Color.Parse(_settings?.PrimaryColor ?? "#18181b"));
            SecondaryColorBrush = new SolidColorBrush(Color.Parse(_settings?.SecondaryColor ?? "#404040"));
            UpdateThemeColors();

            _settings.EnsureInitialized();
            if (_settings.FirstStartup)
            {
                ApplySquareLayoutPreset();
                _settings.FirstStartup = false;
                AppSettings.Save(_settings);
            }

            LoadCurrentVersion();
            LoadCurrentPlatform();
            UpdateSettingsUI();

            // Apply fullscreen from settings immediately
            if (_settings.StartFullscreen)
            {
                WindowState = WindowState.FullScreen;
            }

            // Initialize background image from settings
            BackgroundImagePath = _settings.BackgroundImagePath ?? string.Empty;

            // Initialize music from settings
            LauncherMusicPath = _settings.LauncherMusicPath ?? string.Empty;
            MusicVolume = _settings.MusicVolume;
            if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
            {
                PlayLauncherMusic(LauncherMusicPath);
            }

            _inputService = new InputService(this, _settings);
            _inputService.NavigationInterceptor = HandleGamepadNavigation;
            _inputService.OnConfirm += HandleConfirmAction;
            _inputService.OnCancel += HandleCancelAction;
            _inputService.OnOptions += HandleOptionsAction;
            _inputService.OnGamepadConnectionChanged += HandleGamepadConnectionChanged;
            GamepadContextMenuNavigation.Instance.ResolveKeyboardAction = (key, modifiers) =>
            {
                _settings.EnsureInitialized();
                return KeyboardBindingDefaults.FindAction(_settings.KeyboardBindings, key, modifiers);
            };
            GamepadModalDialogNavigation.Instance.ResolveKeyboardAction = (key, modifiers) =>
            {
                _settings.EnsureInitialized();
                return KeyboardBindingDefaults.FindAction(_settings.KeyboardBindings, key, modifiers);
            };
            GamepadModalDialogNavigation.Instance.OnKeyboardNavigationActivated = ActivateKeyboardNavChrome;
            UpdateGamepadChromeClass();
            UpdateGamepadHintsBar();
            RefreshGamepadBindingsPanel();
            RefreshConnectedGamepadsList();

            GamepadComboBoxNavigation.Attach(SortByComboBox);
            GamepadComboBoxNavigation.Attach(CatalogReviewSortByComboBox);
            GamepadComboBoxNavigation.Attach(ModsSortByComboBox);
            GamepadComboBoxNavigation.Attach(DisplayFilterMatchModeComboBox);
            GamepadComboBoxNavigation.Attach(DisplayFilterExcludeMatchModeComboBox);
            GamepadComboBoxNavigation.Attach(BackgroundUpdateIntervalComboBox);

            // Tunnel: handle arrows/confirm before Avalonia focus walker or CheckBox Space toggle.
            AddHandler(InputElement.KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);
            AddHandler(InputElement.KeyUpEvent, MainWindow_KeyUp, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerPressedEvent, MainWindow_PointerPressedForChrome, RoutingStrategies.Tunnel);

            this.Activated += MainWindow_Activated;
            this.Deactivated += MainWindow_Deactivated;

            // Steam Deck Gaming Mode: Avalonia TextBoxes do not auto-trigger Steam's OSK.
            AddHandler(InputElement.GotFocusEvent, OnTextBoxGotFocusForSteamOsk, RoutingStrategies.Bubble);

            _gameManager.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(GameManager.Games) or nameof(GameManager.IsLibraryEmpty))
                    Dispatcher.UIThread.Post(UpdateGameCollectionUi);
            };

            DataContext = this;
        }

        private void UpdateGameCollectionUi()
        {
            OnPropertyChanged(nameof(Games));
            OnPropertyChanged(nameof(IsLibraryEmpty));
            UpdateContinueButtonState();
            RefreshUpdateCheckStatus();
            UpdateLibraryEmptyState();
            SyncGamepadLibrarySelection();

            foreach (var game in _gameManager.Games)
                SubscribeToGameEvents(game);
        }

        private void NotifyGamepadUiChanged()
        {
            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        // Is Theme Color Light
        private bool IsLightColor(Color color)
        {
            // Calculate perceived brightness using standard formula
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return brightness > 0.5;
        }

        // Theme Color Shades
        private Color GetShadedColor(Color baseColor, double factor)
        {
            byte r = (byte)Math.Min(255, Math.Max(0, baseColor.R * factor));
            byte g = (byte)Math.Min(255, Math.Max(0, baseColor.G * factor));
            byte b = (byte)Math.Min(255, Math.Max(0, baseColor.B * factor));
            return Color.FromRgb(r, g, b);
        }

        private void UpdateThemeColors()
        {
            if (_themeColorBrush == null || _secondaryColorBrush == null) return;

            var primaryColor = _themeColorBrush.Color;
            var secondaryColor = _secondaryColorBrush.Color;
            var themeBase = new SolidColorBrush(primaryColor);
            var themeLighter = new SolidColorBrush(GetShadedColor(primaryColor, 1.3));
            var themeDarker = new SolidColorBrush(GetShadedColor(primaryColor, 0.7));
            var themeBorder = new SolidColorBrush(secondaryColor);

            var textColor = CalculateLuminance(primaryColor) > 0.5 ? Colors.Black : Colors.White;
            var tintedText = new SolidColorBrush(BlendColors(textColor, secondaryColor, 0.08));
            var tintedTextSecondary = new SolidColorBrush(
                CalculateLuminance(primaryColor) > 0.5
                    ? BlendColors(Color.FromRgb(70, 70, 70), secondaryColor, 0.15)
                    : BlendColors(Color.FromRgb(200, 200, 200), secondaryColor, 0.15)
            );

            Resources["ThemeBase"] = themeBase;
            Resources["ThemeLighter"] = themeLighter;
            Resources["ThemeDarker"] = themeDarker;
            Resources["ThemeBorder"] = themeBorder;
            Resources["ThemeText"] = tintedText;
            Resources["ThemeTextSecondary"] = tintedTextSecondary;
            Resources["ThemeSuccess"] = new SolidColorBrush(Color.Parse("#22c55e"));
            Resources["ThemeWarning"] = new SolidColorBrush(Color.Parse("#f59e0b"));
            Resources["ThemeError"] = new SolidColorBrush(Color.Parse("#ef4444"));
            Resources["ThemeAccent"] = new SolidColorBrush(Color.Parse("#f59e0b"));
            Resources["ThemeFocusRing"] = new SolidColorBrush(Color.Parse("#f59e0b"));

            OnPropertyChanged(nameof(WindowBackground));
        }

        private double CalculateLuminance(Color color)
        {
            return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        }

        // Color Picker Preset Dialog
        private async void ThemeColorPicker_Click(object sender, RoutedEventArgs e)
        {
            // Simple color presets dialog
            var presets = new Dictionary<string, string>
            {
                { "Black", "#000000" },
                { "Darker Gray", "#101010" },
                { "Dark Gray (Default)", "#18181b" },
                { "Charcoal Gray", "#2c2c2c" },
                { "Slate Gray", "#36454f" },

                { "Dark Navy Blue", "#1e3a5f" },
                { "Deep Navy", "#0f2b46" },
                { "Deep Indigo", "#2c3e50" },
                { "Dark Grayish Blue", "#45475a" },
                { "Midnight Black", "#19191c" },

                { "Deep Forest Green", "#1a4d2e" },
                { "Darkest Green", "#063204" },
                { "Forest Green", "#228b22" },
                { "Deep Moss Green", "#2c5f2d" },
                { "Deep Forest", "#134411" },
                { "Black Forest Green", "#051D01" },
                { "Dark Olive Green", "#556b2f" },
                { "Dark Olive Drab", "#2a2922" },

                { "Deep Purple", "#2d1b4e" },
                { "Deep Plum", "#4b0082" },
                { "Dark Eggplant", "#614051" },

                { "Dark Burgundy", "#4d1f1f" },
                { "Burgundy", "#800020" },
                { "Deep Maroon", "#5c0b0b" },

                { "Light Gray", "#e5e5e5" },
                { "Silver Gray", "#c0c0c0" },
                { "Pale Gray", "#f0f0f0" },

                { "Soft Blue", "#d4e4f7" },
                { "Sky Blue", "#87ceeb" },
                { "Powder Blue", "#b0e0e6" },

                { "Seafoam Green", "#d4f1e8" },
                { "Mint Green", "#98fb98" },
                { "Bright Sea Foam", "#98ff98" }
            };

            await ShowColorPresetsDialog(presets);
        }

        // Secondary Color Picker
        private async void SecondaryColorPicker_Click(object sender, RoutedEventArgs e)
        {
            // Simple color presets dialog
            var presets = new Dictionary<string, string>
            {
                { "Black", "#000000" },
                { "Dark Gray (Default)", "#404040" },
                { "Gray", "#737373" },
                { "Light Gray", "#d4d4d4" },
                { "White", "#ffffff" },

                { "Red", "#ef4444" },
                { "Orange", "#f97316" },
                { "Yellow", "#eab308" },
                { "Lime", "#84cc16" },
                { "Green", "#10b981" },
                { "Teal", "#14b8a6" },
                { "Cyan", "#06b6d4" },
                { "Sky Blue", "#0ea5e9" },
                { "Blue", "#3b82f6" },
                { "Purple", "#a855f7" },
                { "Violet", "#8b5cf6" },
                { "Indigo", "#6366f1" },
                { "Pink", "#ec4899" } 
            };

            await ShowColorPresetsDialog(presets, true);
        }

        private Color BlendColors(Color baseColor, Color blendColor, double blendAmount)
        {
            byte r = (byte)(baseColor.R * (1 - blendAmount) + blendColor.R * blendAmount);
            byte g = (byte)(baseColor.G * (1 - blendAmount) + blendColor.G * blendAmount);
            byte b = (byte)(baseColor.B * (1 - blendAmount) + blendColor.B * blendAmount);
            return Color.FromRgb(r, g, b);
        }

        private async Task ShowColorPresetsDialog(Dictionary<string, string> presets, bool isSecondary = false)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var stackPanel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

                    // Add Custom Color button at the top
                    var customButton = new Button
                    {
                        Content = "🎨 Custom Color Picker",
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Height = 50,
                        Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontWeight = FontWeight.Bold
                    };

                    customButton.Click += async (s, e) =>
                    {
                        // Close presets dialog
                        var window = (s as Button)?.GetVisualRoot() as Window;
                        window?.Close();

                        // Open custom color picker
                        await ShowCustomColorPicker(isSecondary);
                    };

                    stackPanel.Children.Add(customButton);

                    // Add separator
                    stackPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Margin = new Thickness(0, 5, 0, 5)
                    });

                    foreach (var preset in presets)
                    {
                        var button = new Button
                        {
                            Content = preset.Key,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Height = 40,
                            Background = new SolidColorBrush(Color.Parse(preset.Value)),
                            Foreground = new SolidColorBrush(IsLightColor(Color.Parse(preset.Value)) ? Colors.Black : Colors.White),
                            Tag = preset.Value
                        };

                        button.Click += (s, e) =>
                        {
                            var colorHex = (s as Button)?.Tag as string;
                            if (!string.IsNullOrEmpty(colorHex))
                            {
                                if (isSecondary)
                                {
                                    _settings.SecondaryColor = colorHex;
                                    SecondaryColorBrush = new SolidColorBrush(Color.Parse(colorHex));
                                }
                                else
                                {
                                    _settings.PrimaryColor = colorHex;
                                    ThemeColorBrush = new SolidColorBrush(Color.Parse(colorHex));
                                }
                                OnSettingChanged();

                                // Close the dialog after selection
                                if (s is Button btn && btn.Parent != null)
                                {
                                    var window = btn.GetVisualRoot() as Window;
                                    window?.Close();
                                }
                            }
                        };

                        stackPanel.Children.Add(button);
                    }

                    var messageBox = new Window
                    {
                        Title = isSecondary ? "Select Secondary Color" : "Select Primary Color",
                        Width = 300,
                        Height = 1000,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new ScrollViewer { Content = stackPanel }
                    };

                    GamepadModalDialogNavigation.Attach(messageBox);

                    await messageBox.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private async Task ShowCustomColorPicker(bool isSecondary = false)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var currentColor = isSecondary ? SecondaryColorBrush.Color : ThemeColorBrush.Color;
                    var (h, s, l) = RgbToHsl(currentColor);

                    var pickerPanel = new StackPanel { Margin = new Thickness(20), Spacing = 15 };

                    // Preview box
                    var previewBorder = new Border
                    {
                        Width = 260,
                        Height = 60,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(currentColor),
                        BorderBrush = new SolidColorBrush(Colors.White),
                        BorderThickness = new Thickness(2)
                    };
                    pickerPanel.Children.Add(previewBorder);

                    // HSL Sliders
                    var hSlider = CreateHslSlider("Hue", h, 0, 360, "°");
                    var sSlider = CreateHslSlider("Saturation", s, 0, 100, "%");
                    var lSlider = CreateHslSlider("Lightness", l, 0, 100, "%");

                    pickerPanel.Children.Add(hSlider.panel);
                    pickerPanel.Children.Add(sSlider.panel);
                    pickerPanel.Children.Add(lSlider.panel);

                    // Update preview on slider change
                    EventHandler<AvaloniaPropertyChangedEventArgs> updatePreview = (s, e) =>
                    {
                        var newColor = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        previewBorder.Background = new SolidColorBrush(newColor);
                    };

                    hSlider.slider.PropertyChanged += updatePreview;
                    sSlider.slider.PropertyChanged += updatePreview;
                    lSlider.slider.PropertyChanged += updatePreview;

                    // Hex input
                    var hexPanel = new StackPanel { Spacing = 5 };
                    hexPanel.Children.Add(new TextBlock
                    {
                        Text = "Hex Color",
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    });

                    var hexBox = new TextBox
                    {
                        Text = $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}",
                        Watermark = "#RRGGBB",
                        Foreground = new SolidColorBrush(Colors.White),
                        Background = new SolidColorBrush(Color.FromRgb(40, 40, 40))
                    };

                    hexBox.TextChanged += (s, e) =>
                    {
                        try
                        {
                            var text = hexBox.Text?.Trim();
                            if (!string.IsNullOrEmpty(text) && text.StartsWith("#") && text.Length == 7)
                            {
                                var color = Color.Parse(text);
                                var (hue, sat, light) = RgbToHsl(color);
                                hSlider.slider.Value = hue;
                                sSlider.slider.Value = sat;
                                lSlider.slider.Value = light;
                            }
                        }
                        catch { }
                    };

                    // Update hex box when sliders change
                    EventHandler<AvaloniaPropertyChangedEventArgs> updateHex = (s, e) =>
                    {
                        var color = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        hexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    };

                    hSlider.slider.PropertyChanged += updateHex;
                    sSlider.slider.PropertyChanged += updateHex;
                    lSlider.slider.PropertyChanged += updateHex;

                    hexPanel.Children.Add(hexBox);
                    pickerPanel.Children.Add(hexPanel);

                    // Buttons
                    var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };

                    var applyButton = new Button
                    {
                        Content = "Apply",
                        Width = 120,
                        Height = 35,
                        Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                        Foreground = new SolidColorBrush(Colors.White)
                    };

                    var cancelButton = new Button
                    {
                        Content = "Cancel",
                        Width = 120,
                        Height = 35,
                        Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Foreground = new SolidColorBrush(Colors.White)
                    };

                    buttonPanel.Children.Add(applyButton);
                    buttonPanel.Children.Add(cancelButton);
                    pickerPanel.Children.Add(buttonPanel);

                    var pickerWindow = new Window
                    {
                        Title = isSecondary ? "Custom Secondary Color" : "Custom Primary Color",
                        Width = 320,
                        Height = 480,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                        Content = pickerPanel,
                        CanResize = false
                    };

                    applyButton.Click += (s, e) =>
                    {
                        var finalColor = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        var hexColor = $"#{finalColor.R:X2}{finalColor.G:X2}{finalColor.B:X2}";

                        if (isSecondary)
                        {
                            _settings.SecondaryColor = hexColor;
                            SecondaryColorBrush = new SolidColorBrush(finalColor);
                        }
                        else
                        {
                            _settings.PrimaryColor = hexColor;
                            ThemeColorBrush = new SolidColorBrush(finalColor);
                        }
                        OnSettingChanged();
                        pickerWindow.Close();
                    };

                    cancelButton.Click += (s, e) => pickerWindow.Close();

                    GamepadModalDialogNavigation.Attach(pickerWindow);

                    await pickerWindow.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private (StackPanel panel, Slider slider) CreateHslSlider(string label, double initialValue, double min, double max, string unit)
        {
            var panel = new StackPanel { Spacing = 5 };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Width = 80
            });

            var valueText = new TextBlock
            {
                Text = $"{(int)initialValue}{unit}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Width = 50,
                TextAlignment = TextAlignment.Right
            };
            headerPanel.Children.Add(valueText);

            panel.Children.Add(headerPanel);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = initialValue,
                Width = 260,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };

            slider.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "Value")
                {
                    valueText.Text = $"{(int)slider.Value}{unit}";
                }
            };

            panel.Children.Add(slider);

            return (panel, slider);
        }

        // Convert RGB to HSL
        private (double h, double s, double l) RgbToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            double s = 0;
            double l = (max + min) / 2.0;

            if (delta != 0)
            {
                s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

                if (max == r)
                    h = ((g - b) / delta + (g < b ? 6 : 0)) / 6.0;
                else if (max == g)
                    h = ((b - r) / delta + 2) / 6.0;
                else
                    h = ((r - g) / delta + 4) / 6.0;
            }

            return (h * 360, s * 100, l * 100);
        }

        // Convert HSL to RGB
        private Color HslToRgb(double h, double s, double l)
        {
            h = h / 360.0;
            s = s / 100.0;
            l = l / 100.0;

            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;

                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return Color.FromRgb(
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255)
            );
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private void UpdateContinueButtonState()
        {
            ContinueGameInfo = _gameManager.GetLatestPlayedInstalledGame();
            IsContinueVisible = ContinueGameInfo != null;
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
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                }
                else
                {
                    // Single-click drag to move
                    BeginMoveDrag(e);
                }
            }
        }

        private void LoadCurrentPlatform()
        {
            if (_settings != null)
            {
                PlatformString = _settings.Platform switch
                {
                    TargetOS.Auto => "Automatic",
                    TargetOS.Windows => "Windows",
                    TargetOS.MacOS => "macOS",
                    TargetOS.LinuxX64 => "Linux x64",
                    TargetOS.LinuxARM64 => "Linux ARM64",
                    _ => "Unknown"
                };
            }
            else
            {
                PlatformString = "Unknown";
            }
        }

        private void LoadCurrentVersion()
        {
            try
            {
                var velopackVersion = _velopackUpdateService.CurrentVersion;
                if (!string.IsNullOrWhiteSpace(velopackVersion))
                {
                    currentVersionString = velopackVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                        ? velopackVersion
                        : $"v{velopackVersion}";
                    return;
                }

                string currentAppDirectory = AppDomain.CurrentDomain.BaseDirectory;
                currentVersionString = LauncherVersionService.ReadInstalledVersion(currentAppDirectory);
            }
            catch (Exception ex)
            {
                currentVersionString = "Unknown";
                Debug.WriteLine($"Failed to load version: {ex.Message}");
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _ = InitializeGamesAsync();
        }

        private async Task InitializeGamesAsync()
        {
            try
            {
                await _gameManager.LoadGamesAsync();
                _settings = AppSettings.Load();

                await _gameManager.CatalogService.EnsureCommunitySourcesCachedAsync(
                    _gameManager.HttpClient,
                    _settings);
                AppSettings.Save(_settings);

                await RefreshAllCatalogPendingCountsAsync();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplySorting();
                    UpdateContinueButtonState();
                    RefreshCatalogSourcesList();
                    RefreshTagDisplayFiltersUI();
                    RefreshSidebarFilterSelection();
                    UpdateMainViewUi();
                    RefreshUpdateCheckStatus();
                    UpdateLibraryEmptyState();

                    if (!_hasInitializedFocus)
                    {
                        SetInitialFocus();
                        _hasInitializedFocus = true;
                    }
                });

                await NotifyCatalogUpdatesIfNeededAsync();
                _ = RefreshAnnouncementBannerAsync();
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    UpdateGameCollectionUi();
                    await ShowMessageBoxAsync($"Failed to load apps: {ex.Message}", "Load Error");
                });
            }
        }

        private async Task RefreshAnnouncementBannerAsync()
        {
            try
            {
                var payload = await AnnouncementService
                    .TryFetchAsync(_gameManager.HttpClient)
                    .ConfigureAwait(true);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _settings.EnsureInitialized();
                    if (!AnnouncementService.ShouldShow(payload, _settings.DismissedAnnouncementIds))
                    {
                        HideAnnouncementBanner();
                        return;
                    }

                    ShowAnnouncementBanner(payload!);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Announcement banner fetch failed: {ex.Message}");
            }
        }

        private void ShowAnnouncementBanner(AnnouncementPayload payload)
        {
            _activeAnnouncementId = payload.Id;
            if (AnnouncementBannerText != null)
                AnnouncementBannerText.Text = payload.Message.Trim();
            if (AnnouncementBanner != null)
                AnnouncementBanner.IsVisible = true;
        }

        private void HideAnnouncementBanner()
        {
            _activeAnnouncementId = null;
            if (AnnouncementBanner != null)
                AnnouncementBanner.IsVisible = false;
            if (AnnouncementBannerText != null)
                AnnouncementBannerText.Text = string.Empty;
        }

        private void AnnouncementBannerClose_Click(object? sender, RoutedEventArgs e)
        {
            DismissAnnouncementBanner();
        }

        private void DismissAnnouncementBanner()
        {
            var id = _activeAnnouncementId;
            HideAnnouncementBanner();

            if (string.IsNullOrWhiteSpace(id))
                return;

            _settings.EnsureInitialized();
            if (!_settings.DismissedAnnouncementIds.Any(existing =>
                    string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.DismissedAnnouncementIds.Add(id);
                AppSettings.Save(_settings);
            }

            // Drop focus chrome if the close button was selected.
            if (AnnouncementBannerCloseButton != null)
                ClearFocusIfOnControls([AnnouncementBannerCloseButton]);

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.AnnouncementBanner && IsGamepadFocusActive)
            {
                ClearAnnouncementBannerGamepadFocus();
                ApplyTopBarGamepadSelection(Math.Max(0, _gamepadNavigation.TopBarSelectedIndex));
            }
            else if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.TopBar && IsGamepadFocusActive)
            {
                ApplyTopBarGamepadSelection(Math.Max(0, _gamepadNavigation.TopBarSelectedIndex));
            }
        }

        private bool IsGamepadFocusActive =>
            GamepadFocusChrome.ShouldShowGamepadChrome(
                _settings.EnableGamepadInput,
                _inputService?.HasConnectedGamepad == true,
                GamepadFocusChrome.KeyboardNavigationActive);

        private void UpdateGamepadChromeClass()
        {
            var active = IsGamepadFocusActive;
            GamepadFocusChrome.SetActive(active, this);
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
                UpdateGamepadChromeClass();

                if (!_settings.EnableGamepadInput)
                {
                    ClearGamepadFocus();
                    RefreshConnectedGamepadsList();
                    return;
                }

                if (hasConnected)
                {
                    if (isSettingsPanelOpen)
                        ApplySettingsGamepadSelection(_settingsGamepadFocusIndex < 0 ? 0 : _settingsGamepadFocusIndex);
                    else
                        SelectInitialGamepadItemForCurrentView();
                }
                else
                {
                    ClearGamepadFocus();
                }

                RefreshConnectedGamepadsList();
            });
        }

        private void SetInitialFocus()
        {
            // Small delay to ensure UI is fully rendered
            Dispatcher.UIThread.Post(() =>
            {
                if (IsGamepadFocusActive &&
                    _mainViewMode == MainViewMode.Library &&
                    Games.Count > 0)
                {
                    SelectInitialLibraryGamepadItem();
                    return;
                }

                if (IsGamepadFocusActive &&
                    _mainViewMode == MainViewMode.AppCatalog &&
                    _appCatalogSubView == AppCatalogSubView.Sources &&
                    CatalogSources.Count > 0)
                {
                    SelectInitialCatalogGamepadItem();
                    return;
                }

                // Try to focus the Continue button if visible
                if (IsContinueVisible && this.FindControl<Button>("ContinueButton") is Button continueBtn)
                {
                    continueBtn.Focus();
                    return;
                }

                // Try Library nav button
                if (this.FindControl<Button>("LibraryNavButton") is Button libraryBtn)
                {
                    libraryBtn.Focus();
                    return;
                }

                // Fallback to first focusable control
                var firstFocusable = this.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable);

                firstFocusable?.Focus();
            }, DispatcherPriority.Loaded);
        }

        public void CloseLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (CloseSettingsPanel())
                return;

            // Close changelog if open
            if (_isChangelogOpen)
            {
                CloseChangelog();
                return;
            }

            if (_isModDetailsOpen)
            {
                CloseModDetails();
                return;
            }

            if (_isEntryFormOpen)
            {
                CloseEntryFormOverlay();
                return;
            }

            if (_isTagEditOpen)
            {
                CloseTagEditOverlay();
                return;
            }

            Close();
        }

        public void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
        {
            IsFullscreen = !IsFullscreen;
            if (IsFullscreen)
            {
                WindowState = WindowState.FullScreen;
            }
            else
            {
                WindowState = WindowState.Normal;
            }
            OnPropertyChanged(nameof(WindowBackground));
        }

        public void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState != WindowState.Minimized)
            {
                // Avalonia/Win32 drops FullScreen across minimize (restores as Normal). Remember intent here.
                // IsFullscreen can outlive WindowState when the platform briefly transitions through Normal.
                _windowStateBeforeMinimize = (IsFullscreen || WindowState == WindowState.FullScreen)
                    ? WindowState.FullScreen
                    : WindowState;
                _restoreWindowStateAfterMinimize =
                    _windowStateBeforeMinimize is WindowState.Maximized or WindowState.FullScreen;
            }

            WindowState = WindowState.Minimized;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property != WindowStateProperty)
                return;

            var oldState = change.GetOldValue<WindowState>();
            var newState = change.GetNewValue<WindowState>();

            // Platform may hop FullScreen -> Normal -> Minimized while minimizing; keep FullScreen intent.
            if (newState == WindowState.Minimized && oldState != WindowState.Minimized)
            {
                if (IsFullscreen || oldState == WindowState.FullScreen
                    || _windowStateBeforeMinimize == WindowState.FullScreen)
                {
                    _windowStateBeforeMinimize = WindowState.FullScreen;
                    _restoreWindowStateAfterMinimize = true;
                }
                else if (oldState == WindowState.Maximized)
                {
                    _windowStateBeforeMinimize = WindowState.Maximized;
                    _restoreWindowStateAfterMinimize = true;
                }
            }
            else if (oldState == WindowState.Minimized && newState != WindowState.Minimized)
            {
                // Known Avalonia issue: fullscreen/maximize often restores as Normal after taskbar un-minimize.
                ScheduleRestoreWindowStateAfterMinimize();
            }
        }

        private void RestoreWindowStateAfterMinimizeIfNeeded()
        {
            ScheduleRestoreWindowStateAfterMinimize();
        }

        private void ScheduleRestoreWindowStateAfterMinimize()
        {
            if (!_restoreWindowStateAfterMinimize)
                return;

            var desired = _windowStateBeforeMinimize;
            if (desired is not (WindowState.Maximized or WindowState.FullScreen))
            {
                _restoreWindowStateAfterMinimize = false;
                return;
            }

            // Do not clear the flag until applied — Activated can fire while still Minimized.
            Dispatcher.UIThread.Post(() => TryApplyRestoredWindowState(desired, retry: true), DispatcherPriority.Input);
        }

        private void TryApplyRestoredWindowState(WindowState desired, bool retry)
        {
            if (!_restoreWindowStateAfterMinimize)
                return;

            if (WindowState == WindowState.Minimized)
            {
                if (retry)
                    Dispatcher.UIThread.Post(() => TryApplyRestoredWindowState(desired, retry: false), DispatcherPriority.Background);
                return;
            }

            if (desired == WindowState.FullScreen)
                IsFullscreen = true;

            if (WindowState != desired)
                WindowState = desired;

            // Second kick: Win32/Avalonia sometimes applies Normal after our first FullScreen set.
            if (retry)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (WindowState == WindowState.Minimized)
                        return;
                    if (desired == WindowState.FullScreen)
                        IsFullscreen = true;
                    if (WindowState != desired)
                        WindowState = desired;
                    _restoreWindowStateAfterMinimize = false;
                }, DispatcherPriority.Background);
            }
            else
            {
                _restoreWindowStateAfterMinimize = false;
            }
        }

        public void LayoutPreset_Landscape_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = false;
            _settings.UseGridView = true;
            _settings.GridCompactCards = false;
            _settings.SlotSize = 304;
            _settings.IconSize = 220;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "90";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_Portrait_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = false;
            _settings.UseGridView = true;
            _settings.GridCompactCards = false;
            _settings.SlotSize = 144;
            _settings.IconSize = 200;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "90";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_Square_Click(object sender, RoutedEventArgs e)
        {
            ApplySquareLayoutPreset();
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_Grid_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = true;
            _settings.UseGridView = true;
            _settings.GridCompactCards = true;
            _settings.SlotSize = 272;
            _settings.IconSize = 200;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "*";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_List_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = false;
            _settings.UseGridView = false;
            _settings.SlotSize = 120;
            _settings.IconSize = 116;
            _settings.IconMargin = 8;
            _settings.SlotTextMargin = 112;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "*";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        private void ApplySquareLayoutPreset()
        {
            _settings.IconFill = false;
            _settings.UseGridView = true;
            _settings.GridCompactCards = false;
            _settings.SlotSize = 152;
            _settings.IconSize = 124;
            _settings.ActionButtonSize = 36;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "*";
        }

        private async void GameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is GameInfo game)
            {
                var launched = false;
                try
                {
                    if (game.Status == GameStatus.UpdateAvailable)
                    {
                        ShowUpdateActionMenu(ResolveDownloadMenuAnchor(game, button) ?? button, game);
                        return;
                    }

                    if (game.Status == GameStatus.NotInstalled)
                        game.ClearDownloadSelection();

                    launched = await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);

                    // Re-resolve after async: the action button can be recycled when Status
                    // briefly becomes Downloading, which breaks Gamescope popup parenting.
                    var anchor = ResolveDownloadMenuAnchor(game, button);
                    if (anchor != null && TryShowPendingSelectionMenus(anchor, game))
                        return;

                    UpdateContinueButtonState();
                }
                catch (Exception ex)
                {
                    await ShowMessageBoxAsync($"Failed to perform action for {game.Name}: {ex.Message}", "Action Error");
                }

                CloseAfterLaunchIfNeeded(launched);
            }
        }

        private bool TryShowPendingSelectionMenus(Control anchor, GameInfo game)
        {
            if ((game.Status == GameStatus.NotInstalled || game.Status == GameStatus.UpdateAvailable) &&
                game.HasMultipleDownloads && game.SelectedDownload == null)
            {
                ShowDownloadSelectionMenu(anchor, game);
                return true;
            }

            if (game.Status == GameStatus.Installed && game.HasMultipleExecutables &&
                string.IsNullOrEmpty(game.SelectedExecutable))
            {
                ShowExecutableSelectionMenu(anchor, game);
                return true;
            }

            return false;
        }

        private void ShowUpdateActionMenu(Control anchor, GameInfo game)
        {
            var contextMenu = new ContextMenu();

            contextMenu.Items.Add(new MenuItem
            {
                Header = $"Update options for {game.Name}:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            });
            contextMenu.Items.Add(new Separator());

            var updateNowItem = new MenuItem { Header = "Update Now" };
            updateNowItem.Click += async (_, _) => await HandleUpdateNowAsync(anchor, game);
            contextMenu.Items.Add(updateNowItem);

            var skipItem = new MenuItem { Header = "Skip Update" };
            skipItem.Click += async (_, _) => await HandleSkipUpdateAsync(game);
            contextMenu.Items.Add(skipItem);

            var changeVersionItem = new MenuItem { Header = "Change Version" };
            changeVersionItem.Click += async (_, _) => await HandleChangeVersionAsync(anchor, game);
            contextMenu.Items.Add(changeVersionItem);

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(new MenuItem { Header = "Cancel" });

            OpenContextMenu(anchor, contextMenu);
        }

        private void ShowDownloadSelectionMenu(Control anchor, GameInfo game)
        {
            if (game.AvailableDownloads == null || game.AvailableDownloads.Count == 0)
                return;

            var contextMenu = new ContextMenu();

            // Add header
            var headerItem = new MenuItem
            {
                Header = "Select download file:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            };
            contextMenu.Items.Add(headerItem);
            contextMenu.Items.Add(new Separator());

            // Get platform identifier for matching
            string platformIdentifier = GameInfo.GetPlatformIdentifier(_settings);

            // Sort downloads: preferred platform first, then others
            var sortedDownloads = game.AvailableDownloads
                .OrderByDescending(asset => GameInfo.MatchesPlatform(asset.name, platformIdentifier))
                .ToList();

            // Add download options
            foreach (var asset in sortedDownloads)
            {
                bool isPreferred = GameInfo.MatchesPlatform(asset.name, platformIdentifier);

                // Detect platform icon
                string? iconPath = GameInfo.GetPlatformIcon(asset.name);

                var displayName = asset.name + (isPreferred ? " (Recommended)" : "");
                var menuItem = new MenuItem
                {
                    Header = CreateDownloadAssetMenuHeader(displayName, iconPath),
                    Tag = asset
                };

                if (isPreferred)
                {
                    menuItem.Classes.Add("accent");
                }

                menuItem.Click += async (s, e) =>
                {
                    var selectedAsset = (s as MenuItem)?.Tag as GitHubAsset;
                    game.SelectedDownload = selectedAsset;
                    try
                    {
                        await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Failed to download {game.Name}: {ex.Message}", "Download Error");
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.Items.Add(new Separator());

            // Add cancel option
            var cancelItem = new MenuItem
            {
                Header = "Cancel"
            };
            cancelItem.Click += (s, e) =>
            {
                game.ClearDownloadSelection();
            };
            contextMenu.Items.Add(cancelItem);

            ApplyDownloadSelectionMenuWidth(contextMenu);
            OpenContextMenu(anchor, contextMenu);
        }

        private void ApplyDownloadSelectionMenuWidth(ContextMenu contextMenu)
        {
            var availableWidth = Bounds.Width > 0 ? Bounds.Width - 80 : 720;
            var minWidth = Math.Min(720, availableWidth);
            contextMenu.MinWidth = minWidth;
            contextMenu.MaxWidth = Math.Max(minWidth, availableWidth);
        }

        private static Grid CreateDownloadAssetMenuHeader(string displayName, string? iconPath)
        {
            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            if (!string.IsNullOrEmpty(iconPath))
            {
                var icon = new Avalonia.Controls.Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(
                        Avalonia.Platform.AssetLoader.Open(new Uri(iconPath))),
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 0);
                contentGrid.Children.Add(icon);
            }

            var textBlock = new TextBlock
            {
                Text = displayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(textBlock, 1);
            contentGrid.Children.Add(textBlock);

            return contentGrid;
        }

        private Control? ResolveMenuAnchor(Control sourceControl)
        {
            if (sourceControl is MenuItem menuItem)
            {
                var parent = menuItem.Parent as Control;
                while (parent != null)
                {
                    if (parent is ContextMenu parentMenu && parentMenu.PlacementTarget is Control parentTarget)
                    {
                        return parentTarget;
                    }

                    parent = parent.Parent as Control;
                }

                var visualMenu = menuItem.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
                if (visualMenu?.PlacementTarget is Control visualTarget)
                {
                    return visualTarget;
                }
            }

            return sourceControl;
        }

        private Control? FindGameMenuAnchor(GameInfo game)
        {
            return this.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button =>
                    ReferenceEquals(button.DataContext, game) ||
                    ReferenceEquals(button.Tag, game));
        }

        private void OpenContextMenu(Control anchor, ContextMenu contextMenu)
        {
            if (double.IsNaN(contextMenu.MaxHeight) || contextMenu.MaxHeight <= 0)
            {
                var availableHeight = Bounds.Height > 0 ? Bounds.Height - 120 : 560;
                contextMenu.MaxHeight = Math.Max(240, availableHeight);
            }

            GamepadContextMenuNavigation.Attach(contextMenu);
            PreserveLibraryGamepadFocusWhileOpeningMenu();

            contextMenu.PlacementTarget = anchor;
            contextMenu.Placement = PlacementMode.BottomEdgeAlignedRight;
            AttachOptionsMenuClosedHandler(contextMenu, anchor);
            contextMenu.Open(anchor);
        }

        private void AttachOptionsMenuClosedHandler(ContextMenu contextMenu, Control anchor)
        {
            void OnClosed(object? sender, EventArgs e)
            {
                contextMenu.Closed -= OnClosed;
                var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
                if (ReferenceEquals(focusManager?.GetFocusedElement(), anchor))
                    focusManager.ClearFocus();

                RestoreLibraryGamepadFocusAfterMenu();
            }

            contextMenu.Closed -= OnClosed;
            contextMenu.Closed += OnClosed;
        }

        private void GameCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                return;
            }

            if (sender is not Control card)
            {
                return;
            }

            Control? placementTarget = null;
            ContextMenu? contextMenu = card.ContextMenu;

            if (contextMenu != null)
            {
                placementTarget = card;
            }
            else
            {
                var optionsButton = card.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(button => button.ContextMenu != null);

                contextMenu = optionsButton?.ContextMenu;
                placementTarget = optionsButton;
            }

            if (contextMenu == null || placementTarget == null)
            {
                return;
            }

            OpenContextMenu(placementTarget, contextMenu);
            e.Handled = true;
        }

        /// <returns>True when an install was started/completed; false when cancelled or deferred to a picker.</returns>
        private async Task<bool> HandleUpdateNowAsync(
            Control anchor,
            GameInfo game,
            bool preferAutoPlatform = false,
            bool allowAssetPicker = true)
        {
            try
            {
                game.IsLoading = true;
                var releases = await game.FetchReleasesAsync(_gameManager.HttpClient);
                var latestRelease = releases.FirstOrDefault();
                if (latestRelease == null)
                {
                    if (allowAssetPicker)
                        await ShowMessageBoxAsync($"No downloadable releases were found for {game.Name}.", "No Releases");
                    return false;
                }

                var availableAssets = latestRelease.assets?
                    .Where(asset => !asset.name.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? [];

                if (availableAssets.Count == 0)
                {
                    if (allowAssetPicker)
                        await ShowMessageBoxAsync($"No downloadable files were found for {game.Name}.", "No Assets");
                    return false;
                }

                if (availableAssets.Count == 1)
                {
                    await game.InstallReleaseAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings, latestRelease, availableAssets[0]);
                    await PersistGameVersionPreferencesAsync(game, null, latestRelease.tag_name);
                    ApplySorting();
                    UpdateContinueButtonState();
                    if (_isAppUpdatesReviewOpen)
                        CloseAppUpdatesReviewIfEmpty();
                    return true;
                }

                if (preferAutoPlatform)
                {
                    var platformIdentifier = GameInfo.GetPlatformIdentifier(_settings);
                    var preferredAsset = availableAssets
                        .FirstOrDefault(asset => GameInfo.MatchesPlatform(asset.name, platformIdentifier));
                    if (preferredAsset != null)
                    {
                        await game.InstallReleaseAsync(
                            _gameManager.HttpClient,
                            _gameManager.GamesFolder,
                            _settings,
                            latestRelease,
                            preferredAsset);
                        await PersistGameVersionPreferencesAsync(game, null, latestRelease.tag_name);
                        ApplySorting();
                        UpdateContinueButtonState();
                        if (_isAppUpdatesReviewOpen)
                            CloseAppUpdatesReviewIfEmpty();
                        return true;
                    }
                }

                if (!allowAssetPicker)
                    return false;

                await ShowReleaseDownloadSelectionMenuAsync(anchor, game, latestRelease, null, latestRelease.tag_name);
                if (_isAppUpdatesReviewOpen)
                    CloseAppUpdatesReviewIfEmpty();
                return false;
            }
            catch (Exception ex)
            {
                if (allowAssetPicker)
                    await ShowMessageBoxAsync($"Failed to update {game.Name}: {ex.Message}", "Update Error");
                else
                    throw;
                return false;
            }
            finally
            {
                game.IsLoading = false;
            }
        }

        private async Task HandleSkipUpdateAsync(GameInfo game)
        {
            game.SkipLatestUpdate();
            await PersistGameVersionPreferencesAsync(game, game.PreferredVersion, game.SkippedUpdateVersion);
            ApplySorting();
            UpdateContinueButtonState();
            if (_isAppUpdatesReviewOpen)
                CloseAppUpdatesReviewIfEmpty();
        }

        private void AppUpdatesBackToLibrary_Click(object? sender, RoutedEventArgs e) =>
            ShowLibraryView();

        private async void AppUpdateReviewRowUpdate_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not GameInfo game)
                return;

            button.IsEnabled = false;
            try
            {
                await HandleUpdateNowAsync(ResolveDownloadMenuAnchor(game, button) ?? button, game);
                RefreshUpdateCheckStatus();
                NotifyUpdateCheckUiProperties();
                CloseAppUpdatesReviewIfEmpty();
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async void AppUpdateReviewRowSkip_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not GameInfo game)
                return;

            await HandleSkipUpdateAsync(game);
            RefreshUpdateCheckStatus();
            NotifyUpdateCheckUiProperties();
            CloseAppUpdatesReviewIfEmpty();
        }

        private async void AppUpdateReviewRowVersions_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not GameInfo game)
                return;

            ShowUpdateActionMenu(button, game);
            await Task.CompletedTask;
        }

        private async void AppUpdatesUpdateAll_Click(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<Button>("AppUpdatesUpdateAllButton") is not Button updateAllButton)
                return;

            updateAllButton.IsEnabled = false;
            if (this.FindControl<Button>("AppUpdatesSkipAllButton") is Button skipAllButton)
                skipAllButton.IsEnabled = false;

            try
            {
                foreach (var game in GetPendingAppUpdates().ToList())
                {
                    if (!_isAppUpdatesReviewOpen)
                        break;
                    if (game.Status != GameStatus.UpdateAvailable)
                        continue;

                    var anchor = FindAppUpdateReviewActionButton(game, "Update") ?? updateAllButton;
                    await HandleUpdateNowAsync(anchor, game, preferAutoPlatform: true);
                    RefreshUpdateCheckStatus();
                    NotifyUpdateCheckUiProperties();
                    RefreshAppUpdateReviewRows();

                    if (AppUpdateReviewRows.Count == 0)
                    {
                        ShowLibraryView();
                        break;
                    }
                }
            }
            finally
            {
                if (_isAppUpdatesReviewOpen)
                {
                    var availableCount = GetPendingAppUpdates().Count;
                    updateAllButton.IsEnabled = availableCount > 0;
                    if (this.FindControl<Button>("AppUpdatesSkipAllButton") is Button skipAll)
                        skipAll.IsEnabled = availableCount > 0;
                }
            }
        }

        private async void AppUpdatesSkipAll_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var game in GetPendingAppUpdates().ToList())
                await HandleSkipUpdateAsync(game);

            RefreshUpdateCheckStatus();
            NotifyUpdateCheckUiProperties();
            ShowLibraryView();
        }

        private Button? FindAppUpdateReviewActionButton(GameInfo game, string content)
        {
            var itemsControl = this.FindControl<ItemsControl>("AppUpdatesReviewItemsControl");
            if (itemsControl == null)
                return null;

            return itemsControl.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b =>
                    ReferenceEquals(b.DataContext, game) &&
                    string.Equals(b.Content?.ToString(), content, StringComparison.Ordinal));
        }

        private async Task HandleChangeVersionAsync(Control anchor, GameInfo game)
        {
            try
            {
                game.IsLoading = true;
                var releases = await game.FetchReleasesAsync(_gameManager.HttpClient);
                if (releases.Count == 0)
                {
                    await ShowMessageBoxAsync($"No downloadable releases were found for {game.Name}.", "No Releases");
                    return;
                }

                ShowVersionSelectionMenu(anchor, game, releases);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to load versions for {game.Name}: {ex.Message}", "Version Selection Error");
            }
            finally
            {
                game.IsLoading = false;
            }
        }

        private void ShowVersionSelectionMenu(Control anchor, GameInfo game, IReadOnlyList<GitHubRelease> releases)
        {
            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(new MenuItem
            {
                Header = $"Choose a version for {game.Name}:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            });
            contextMenu.Items.Add(new Separator());

            foreach (var release in releases)
            {
                var tags = new List<string>();
                if (!string.IsNullOrWhiteSpace(game.LatestVersion) &&
                    release.tag_name.Equals(game.LatestVersion, StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("Latest");
                }
                if (!string.IsNullOrWhiteSpace(game.InstalledVersion) &&
                    release.tag_name.Equals(game.InstalledVersion, StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("Installed");
                }
                if (!string.IsNullOrWhiteSpace(game.PreferredVersion) &&
                    release.tag_name.Equals(game.PreferredVersion, StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("Preferred");
                }
                if (release.prerelease)
                {
                    tags.Add("Pre-release");
                }

                var header = release.tag_name;
                if (tags.Count > 0)
                {
                    header += $" ({string.Join(", ", tags)})";
                }

                var versionItem = new MenuItem { Header = header };
                versionItem.Click += (_, _) =>
                {
                    var acknowledgedVersion = string.IsNullOrWhiteSpace(game.LatestVersion)
                        ? release.tag_name
                        : game.LatestVersion;
                    ShowReleaseDownloadSelectionMenu(anchor, game, release, release.tag_name, acknowledgedVersion);
                };

                if (!string.IsNullOrWhiteSpace(game.LatestVersion) &&
                    release.tag_name.Equals(game.LatestVersion, StringComparison.OrdinalIgnoreCase))
                {
                    versionItem.Classes.Add("accent");
                }

                contextMenu.Items.Add(versionItem);
            }

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(new MenuItem { Header = "Cancel" });
            OpenContextMenu(anchor, contextMenu);
        }

        private void ShowReleaseDownloadSelectionMenu(Control anchor, GameInfo game, GitHubRelease release, string? preferredVersion, string? skippedUpdateVersion) =>
            _ = ShowReleaseDownloadSelectionMenuAsync(anchor, game, release, preferredVersion, skippedUpdateVersion);

        private Task ShowReleaseDownloadSelectionMenuAsync(
            Control anchor,
            GameInfo game,
            GitHubRelease release,
            string? preferredVersion,
            string? skippedUpdateVersion)
        {
            var availableAssets = release.assets?
                .Where(asset => !asset.name.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(asset => GameInfo.MatchesPlatform(asset.name, GameInfo.GetPlatformIdentifier(_settings)))
                .ToList() ?? [];

            if (availableAssets.Count == 0)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(new MenuItem
            {
                Header = $"Choose a download for {release.tag_name}:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            });
            contextMenu.Items.Add(new Separator());

            string platformIdentifier = GameInfo.GetPlatformIdentifier(_settings);

            foreach (var asset in availableAssets)
            {
                bool isPreferred = GameInfo.MatchesPlatform(asset.name, platformIdentifier);
                string? iconPath = GameInfo.GetPlatformIcon(asset.name);

                var displayName = asset.name + (isPreferred ? " (Recommended)" : "");
                var menuItem = new MenuItem
                {
                    Header = CreateDownloadAssetMenuHeader(displayName, iconPath)
                };

                if (isPreferred)
                {
                    menuItem.Classes.Add("accent");
                }

                menuItem.Click += async (_, _) =>
                {
                    try
                    {
                        await game.InstallReleaseAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings, release, asset);
                        if (!string.IsNullOrWhiteSpace(skippedUpdateVersion))
                        {
                            game.LatestVersion = skippedUpdateVersion;
                        }
                        await PersistGameVersionPreferencesAsync(game, preferredVersion, skippedUpdateVersion);
                        ApplySorting();
                        UpdateContinueButtonState();
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Failed to download {game.Name}: {ex.Message}", "Download Error");
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(new MenuItem { Header = "Cancel" });
            ApplyDownloadSelectionMenuWidth(contextMenu);

            void OnClosed(object? sender, EventArgs e)
            {
                contextMenu.Closed -= OnClosed;
                tcs.TrySetResult();
            }

            contextMenu.Closed += OnClosed;
            OpenContextMenu(anchor, contextMenu);
            return tcs.Task;
        }

        private void ShowExecutableSelectionMenu(Control anchor, GameInfo game)
        {
            if (game.AvailableExecutables == null || game.AvailableExecutables.Count == 0)
                return;

            var contextMenu = new ContextMenu();

            // Add header
            var headerItem = new MenuItem
            {
                Header = "Select executable to launch:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            };
            contextMenu.Items.Add(headerItem);
            contextMenu.Items.Add(new Separator());

            // Add executable options
            foreach (var exe in game.AvailableExecutables)
            {
                var displayName = Path.GetFileName(exe);
                var menuItem = new MenuItem
                {
                    Header = displayName,
                    Tag = exe
                };

                menuItem.Click += async (s, e) =>
                {
                    var selectedExe = (s as MenuItem)?.Tag as string;
                    game.SelectedExecutable = selectedExe;

                    // Save the selection
                    if (!string.IsNullOrEmpty(selectedExe))
                    {
                        game.SaveSelectedExecutable(selectedExe, _gameManager.GamesFolder);
                    }

                    try
                    {
                        await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Failed to launch {game.Name}: {ex.Message}", "Launch Error");
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.Items.Add(new Separator());

            // Add cancel option
            var cancelItem = new MenuItem
            {
                Header = "Cancel"
            };
            cancelItem.Click += (s, e) =>
            {
                game.SelectedExecutable = null;
            };
            contextMenu.Items.Add(cancelItem);

            // Focus first executable item when opened
            contextMenu.Opened += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var firstExecutableItem = contextMenu.Items.OfType<MenuItem>()
                        .Skip(1)
                        .FirstOrDefault(item => item is MenuItem mi && mi.IsEnabled);
                    firstExecutableItem?.Focus();
                }, DispatcherPriority.Loaded);
            };

            OpenContextMenu(anchor, contextMenu);
        }

        private async void SelectDifferentExecutable_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var anchor = (menuItem != null ? ResolveMenuAnchor(menuItem) : null) ?? FindGameMenuAnchor(game);
            if (anchor == null)
            {
                _ = ShowMessageBoxAsync("Unable to open the executable menu for this game.", "Error");
                return;
            }

            game.ClearSelectedExecutable(_gameManager.GamesFolder);
            game.AvailableExecutables = null;

            try
            {
                if (string.IsNullOrWhiteSpace(game.FolderName))
                {
                    await ShowMessageBoxAsync("This game is missing its install folder information.", "Executable Error");
                    return;
                }

                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                if (!Directory.Exists(gamePath))
                {
                    await ShowMessageBoxAsync($"Could not find the install folder for {game.Name}.", "Executable Error");
                    return;
                }

                var executables = GameInfo.GetExecutableCandidates(gamePath, SearchOption.TopDirectoryOnly, out _);
                if (executables.Count == 0)
                {
                    executables = GameInfo.GetExecutableCandidates(gamePath, SearchOption.AllDirectories, out _);
                }

                if (executables.Count == 0)
                {
                    await ShowMessageBoxAsync($"No executable files were found for {game.Name}.", "Executable Not Found");
                    return;
                }

                game.AvailableExecutables = executables;

                if (executables.Count == 1)
                {
                    await ShowMessageBoxAsync($"Only one executable was found for {game.Name}, so there is nothing else to choose.", "Single Executable");
                    return;
                }

                var menuAnchor = ResolveDownloadMenuAnchor(game, anchor) ?? anchor;
                ShowExecutableSelectionMenu(menuAnchor, game);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to load executables for {game.Name}: {ex.Message}", "Executable Error");
            }
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.ContextMenu != null)
                OpenContextMenu(button, button.ContextMenu);
        }

        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            var latestGame = _gameManager.GetLatestPlayedInstalledGame();
            if (latestGame != null)
            {
                var launched = false;
                try
                {
                    launched = await latestGame.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                    UpdateContinueButtonState();
                }
                catch (Exception ex)
                {
                    await ShowMessageBoxAsync($"Failed to launch {latestGame.Name}: {ex.Message}", "Launch Error");
                }

                CloseAfterLaunchIfNeeded(launched);
            }
            else
            {
                await ShowMessageBoxAsync("No installed apps found to continue.", "No App Found");
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel == null)
                return;

            if (_isEntryFormOpen)
                CloseEntryFormOverlay();
            if (_isTagEditOpen)
                CloseTagEditOverlay();

            isSettingsPanelOpen = !isSettingsPanelOpen;
            SettingsPanel.IsVisible = isSettingsPanelOpen;

            if (isSettingsPanelOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;
                NotifyGamepadUiChanged();
                ClearCatalogSourcesToolbarGamepadFocus();
                ClearGamepadFocus();
                RefreshConnectedGamepadsList();
                RefreshGamepadBindingsPanel();
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsGamepadFocusActive)
                        ApplySettingsGamepadSelection(0);
                    else
                        ClearSettingsGamepadFocusClasses(CollectSettingsFocusableControls());
                }, DispatcherPriority.Loaded);
            }
            else
            {
                CancelGamepadRebindListen();
                ClearSettingsGamepadFocusClasses(CollectSettingsFocusableControls());
                _settingsGamepadFocusIndex = -1;
            }
        }

        public void OpenGitHubApiTokenSettings() => OpenAdvancedApiTokenSettings(focusGitLab: false);

        public void OpenGitLabApiTokenSettings() => OpenAdvancedApiTokenSettings(focusGitLab: true);

        private void OpenAdvancedApiTokenSettings(bool focusGitLab)
        {
            if (SettingsPanel == null)
                return;

            if (_isEntryFormOpen)
                CloseEntryFormOverlay();
            if (_isTagEditOpen)
                CloseTagEditOverlay();

            isSettingsPanelOpen = true;
            SettingsPanel.IsVisible = true;
            RefreshCatalogSourcesList();

            if (SettingsTabControl != null)
                SettingsTabControl.SelectedIndex = 4;

            Dispatcher.UIThread.Post(() =>
            {
                var target = focusGitLab ? GitLabTokenTextBox : GitHubTokenTextBox;
                if (target != null)
                {
                    target.BringIntoView();
                    GamepadControlActivation.ActivateTextBox(target);
                }
            }, DispatcherPriority.Loaded);
        }

        private bool CloseSettingsPanel()
        {
            if (!isSettingsPanelOpen || SettingsPanel == null)
                return false;

            isSettingsPanelOpen = false;
            SettingsPanel.IsVisible = false;
            CancelGamepadRebindListen();
            ClearSettingsGamepadFocusClasses(CollectSettingsFocusableControls());
            _settingsGamepadFocusIndex = -1;
            _gamepadNavigation.ActiveZone = _mainViewMode == MainViewMode.Library
                ? GamepadNavigationZone.Library
                : GamepadNavigationZone.CatalogSources;
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
                var firstFocusable = this.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable && !IsInsideSettingsPanel(c));
                firstFocusable?.Focus();
            }

            return true;
        }

        private void CloseSettingsPanel_Click(object? sender, RoutedEventArgs e)
        {
            CloseSettingsPanel();
        }

        private void UpdateSettingsUI()
        {
            if (_settings == null)
                return;

            _suppressSettingsUiEvents = true;
            try
            {
                if (IconOpacitySlider != null)
                    IconOpacitySlider.Value = _settings.IconOpacity;

                if (IconSizeSlider != null)
                    IconSizeSlider.Value = _settings.IconSize;

                if (IconFillCheckBox != null)
                    IconFillCheckBox.IsChecked = _settings.IconFill;

                if (TextMarginSlider != null)
                    TextMarginSlider.Value = _settings.SlotTextMargin;

                if (IconMarginSlider != null)
                    IconMarginSlider.Value = _settings.IconMargin;

                if (SlotSizeSlider != null)
                    SlotSizeSlider.Value = _settings.SlotSize;

                if (ActionButtonSizeSlider != null)
                    ActionButtonSizeSlider.Value = _settings.ActionButtonSize;

                if (UseGridViewCheckBox != null)
                    UseGridViewCheckBox.IsChecked = _settings.UseGridView;

                if (GridCompactCardsCheckBox != null)
                    GridCompactCardsCheckBox.IsChecked = _settings.GridCompactCards;

                UpdateGridLayoutVisibility();

                if (BackgroundOpacitySlider != null)
                    BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;

                if (ShowOSTopBarCheckBox != null)
                    ShowOSTopBarCheckBox.IsChecked = _settings.ShowOSTopBar;

                if (SortByComboBox != null)
                {
                    var savedSort = _settings.SortBy ?? "Name";
                    _currentSortBy = savedSort;

                    foreach (var entry in SortByComboBox.Items)
                    {
                        if (entry is ComboBoxItem item && item.Tag as string == savedSort)
                        {
                            SortByComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                ApplyCatalogReviewSortSelection(_settings.CatalogReviewSortBy ?? "Name");

                if (GitHubTokenTextBox != null)
                    GitHubTokenTextBox.Text = _settings.GitHubApiToken;

                if (GitLabTokenTextBox != null)
                    GitLabTokenTextBox.Text = _settings.GitLabApiToken;

                if (GamePathTextBox != null)
                    GamePathTextBox.Text = _settings.AppsPath;

                if (LinuxWindowsLaunchCommandTextBox != null)
                    LinuxWindowsLaunchCommandTextBox.Text = _settings.LinuxWindowsLaunchCommand;

                if (StartFullscreenCheckBox != null)
                    StartFullscreenCheckBox.IsChecked = _settings.StartFullscreen;

                if (EnableGamepadCheckBox != null)
                    EnableGamepadCheckBox.IsChecked = _settings.EnableGamepadInput;

                if (CloseAfterLaunchCheckBox != null)
                    CloseAfterLaunchCheckBox.IsChecked = _settings.CloseAfterLaunch;

                if (CloseToTrayCheckBox != null)
                    CloseToTrayCheckBox.IsChecked = _settings.CloseToTray;

                if (BackgroundUpdateCheckCheckBox != null)
                    BackgroundUpdateCheckCheckBox.IsChecked = _settings.BackgroundUpdateCheckEnabled;

                if (PromptCatalogUpdatesCheckBox != null)
                    PromptCatalogUpdatesCheckBox.IsChecked = _settings.PromptCatalogUpdates;

                if (PromptAppUpdateReviewsCheckBox != null)
                    PromptAppUpdateReviewsCheckBox.IsChecked = _settings.PromptAppUpdateReviews;

                if (ShowLibraryAppUpdateBadgesCheckBox != null)
                    ShowLibraryAppUpdateBadgesCheckBox.IsChecked = _settings.ShowLibraryAppUpdateBadges;

                if (AllowPrereleaseLauncherUpdatesCheckBox != null)
                    AllowPrereleaseLauncherUpdatesCheckBox.IsChecked = _settings.AllowPrereleaseLauncherUpdates;

                if (AutoUpdateNewlyAddedAppsCheckBox != null)
                    AutoUpdateNewlyAddedAppsCheckBox.IsChecked = _settings.AutoUpdateNewlyAddedApps;

                if (BackgroundUpdateIntervalComboBox != null)
                {
                    var interval = BackgroundUpdateCheckIntervals.Normalize(
                        _settings.BackgroundUpdateCheckIntervalMinutes);
                    foreach (var entry in BackgroundUpdateIntervalComboBox.Items)
                    {
                        if (entry is ComboBoxItem item &&
                            item.Tag as string == interval.ToString())
                        {
                            BackgroundUpdateIntervalComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (IgnoreArticlesWhenSortingCheckBox != null)
                    IgnoreArticlesWhenSortingCheckBox.IsChecked = _settings.IgnoreArticlesWhenSorting;

                SelectLibraryNameStyleComboBox(_settings.LibraryNameStyle);
                SelectLibraryTagDisplayModeComboBox(_settings.LibraryTagDisplayMode);
                SelectLibraryCardTagMaxLinesComboBox(_settings.LibraryCardTagMaxLines);

                RefreshConnectedGamepadsList();
                RefreshGamepadBindingsPanel();
                UpdateGamepadHintsBar();

                PlatformString = _settings.Platform switch
                {
                    TargetOS.Auto => "Automatic",
                    TargetOS.Windows => "Windows",
                    TargetOS.MacOS => "macOS",
                    TargetOS.LinuxX64 => "Linux x64",
                    TargetOS.LinuxARM64 => "Linux ARM64",
                    _ => "Unknown"
                };

                // Initialize theme
                ThemeColorBrush = new SolidColorBrush(Color.Parse(_settings?.PrimaryColor ?? "#18181b"));
                SecondaryColorBrush = new SolidColorBrush(Color.Parse(_settings?.SecondaryColor ?? "#404040"));
                UpdateThemeColors();
                RefreshCatalogSourcesList();
                RefreshTagDisplayFiltersUI();
                RefreshSidebarFilterSelection();
            }
            finally
            {
                _suppressSettingsUiEvents = false;
            }
        }

        private void RefreshTagDisplayFiltersUI()
        {
            _settings.EnsureInitialized();

            TagDisplayFilters.Clear();
            foreach (var filter in _settings.TagDisplayFilters)
            {
                var isSelected = string.Equals(
                    filter.Id,
                    _settings.ActiveTagDisplayFilterId,
                    StringComparison.OrdinalIgnoreCase);
                TagDisplayFilters.Add(TagDisplayFilterListItem.FromFilter(filter, isSelected));
            }
        }

        private void RefreshSidebarFilterSelection()
        {
            _settings.EnsureInitialized();

            UnhideAllGamesButton?.Classes.Set(
                "selected",
                _settings.ListScope == AppListScope.AllApps);
            HideNonInstalledButton?.Classes.Set(
                "selected",
                _settings.ListScope == AppListScope.InstalledOnly);
            ShowHiddenGamesButton?.Classes.Set(
                "selected",
                _settings.ListScope == AppListScope.HiddenOnly);
        }

        private async Task RefreshCatalogSourcesListAsync()
        {
            if (_isRefreshingCatalogSources)
                return;

            _isRefreshingCatalogSources = true;
            _suppressCatalogSourceUiEvents = true;
            try
            {
                try
                {
                    await _gameManager.CatalogService.RefreshAllSourcesUsageStatsAsync(_settings);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Catalog source usage stats refresh failed: {ex.Message}");
                }

                // Always refresh the visible list even if stats fail (e.g. path/parse errors).
                _catalogViewModel.RefreshSourceList(CatalogSources, _settings);
            }
            finally
            {
                _suppressCatalogSourceUiEvents = false;
                _isRefreshingCatalogSources = false;
            }

            RefreshCatalogBadgeCounts();
            UpdateCatalogSourcesEmptyState();
            SyncCatalogGamepadSelection();
        }

        private void RefreshCatalogSourcesList() => _ = RefreshCatalogSourcesListAsync();

        private void RefreshCatalogBadgeCounts()
        {
            OnPropertyChanged(nameof(CatalogReviewBadgeCount));
            OnPropertyChanged(nameof(CatalogReviewBadgeVisible));
        }

        private void UpdateCatalogSourcesEmptyState()
        {
            if (this.FindControl<TextBlock>("CatalogSourcesEmptyText") is not TextBlock emptyText)
                return;

            emptyText.IsVisible = CatalogSources.Count == 0;
            if (CatalogSources.Count > 0)
                return;

            emptyText.Text = _catalogViewModel.SourceListFilter switch
            {
                CatalogSourceListFilter.Enabled => "No enabled catalog sources.",
                CatalogSourceListFilter.Disabled => "No disabled catalog sources.",
                _ when _settings.AppCatalogSources.Count == 0 =>
                    "No catalog sources yet. Add a source to subscribe to an external app list.",
                _ => "No catalog sources match this filter.",
            };
        }

        private static readonly (string Tag, CatalogSourceListFilter Filter)[] CatalogSourceListFilters =
        [
            ("All", CatalogSourceListFilter.All),
            ("Enabled", CatalogSourceListFilter.Enabled),
            ("Disabled", CatalogSourceListFilter.Disabled),
        ];

        private void CatalogSourceListFilter_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
                return;

            var filter = CatalogSourceListFilters.FirstOrDefault(f => f.Tag == tag).Filter;
            _catalogViewModel.SourceListFilter = filter;
            RefreshCatalogSourceListFilterButtons(tag);
            RefreshCatalogSourcesList();
        }

        private void RefreshCatalogSourceListFilterButtons(string selectedTag)
        {
            foreach (var (tag, _) in CatalogSourceListFilters)
            {
                var buttonName = tag switch
                {
                    "All" => "CatalogSourceFilterAllButton",
                    "Enabled" => "CatalogSourceFilterEnabledButton",
                    "Disabled" => "CatalogSourceFilterDisabledButton",
                    _ => null,
                };

                if (buttonName != null && this.FindControl<Button>(buttonName) is Button button)
                    button.Classes.Set("selected", tag == selectedTag);
            }
        }

        private void LibraryNavButton_Click(object? sender, RoutedEventArgs e) => ShowLibraryView();

        private void AppCatalogNavButton_Click(object? sender, RoutedEventArgs e) =>
            ShowAppCatalogSourcesView();

        private void CatalogReviewBack_Click(object? sender, RoutedEventArgs e) =>
            ShowAppCatalogSourcesView();

        private void ShowLibraryView()
        {
            _mainViewMode = MainViewMode.Library;
            _appCatalogSubView = AppCatalogSubView.Sources;
            _isAppUpdatesReviewOpen = false;
            if (_isModsOverlayOpen)
                CloseModsOverlay();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();
            if (IsGamepadFocusActive)
                SelectInitialLibraryGamepadItem();
            else
                ClearGamepadFocus();
        }

        private void ShowAppCatalogSourcesView()
        {
            _mainViewMode = MainViewMode.AppCatalog;
            _appCatalogSubView = AppCatalogSubView.Sources;
            _isAppUpdatesReviewOpen = false;
            if (_isModsOverlayOpen)
                CloseModsOverlay();
            _activeCatalogSyncSource = null;
            CatalogSyncRows.Clear();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();
            if (IsGamepadFocusActive)
                SelectInitialCatalogGamepadItem();
            else
                ClearGamepadFocus();
        }

        private void ShowAppCatalogReviewView(AppCatalogSource source)
        {
            _mainViewMode = MainViewMode.AppCatalog;
            _appCatalogSubView = AppCatalogSubView.Review;
            _isAppUpdatesReviewOpen = false;
            _activeCatalogSyncSource = source;
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();

            if (this.FindControl<TextBlock>("HeaderTitleText") is TextBlock headerTitle)
                headerTitle.Text = $"Review: {source.Name}";

            if (IsGamepadFocusActive)
                SelectInitialCatalogReviewGamepadItem();
            else
                ClearGamepadFocus();
        }

        private void OpenAppUpdatesReview()
        {
            if (_isModsOverlayOpen)
                CloseModsOverlay();

            _mainViewMode = MainViewMode.Library;
            _appCatalogSubView = AppCatalogSubView.Sources;
            _isAppUpdatesReviewOpen = true;
            RefreshAppUpdateReviewRows();
            ResetGamepadNavigationIndices();
            UpdateMainViewUi();

            if (IsGamepadFocusActive)
                SelectInitialAppUpdatesReviewGamepadItem();
            else
                ClearGamepadFocus();
        }

        private void RefreshAppUpdateReviewRows()
        {
            var reviewRows = GetAppUpdateReviewRows();
            var availableCount = reviewRows.Count(g => g.Status == GameStatus.UpdateAvailable);
            var inProgressCount = reviewRows.Count - availableCount;

            AppUpdateReviewRows.Clear();
            foreach (var game in reviewRows)
                AppUpdateReviewRows.Add(game);

            if (this.FindControl<TextBlock>("AppUpdatesReviewHeaderText") is TextBlock header)
            {
                header.Text = reviewRows.Count == 0
                    ? "No app updates pending."
                    : inProgressCount > 0 && availableCount == 0
                        ? inProgressCount == 1
                            ? "1 app update in progress"
                            : $"{inProgressCount} app updates in progress"
                        : inProgressCount > 0
                            ? availableCount == 1
                                ? $"1 app update available ({inProgressCount} in progress)"
                                : $"{availableCount} app updates available ({inProgressCount} in progress)"
                            : availableCount == 1
                                ? "1 app update is available"
                                : $"{availableCount} app updates are available";
            }

            if (this.FindControl<TextBlock>("AppUpdatesReviewEmptyText") is TextBlock emptyText)
                emptyText.IsVisible = reviewRows.Count == 0;

            if (this.FindControl<Button>("AppUpdatesUpdateAllButton") is Button updateAll)
                updateAll.IsEnabled = availableCount > 0;

            if (this.FindControl<Button>("AppUpdatesSkipAllButton") is Button skipAll)
                skipAll.IsEnabled = availableCount > 0;
        }

        private void CloseAppUpdatesReviewIfEmpty()
        {
            if (!_isAppUpdatesReviewOpen)
                return;

            RefreshAppUpdateReviewRows();
            if (AppUpdateReviewRows.Count == 0)
                ShowLibraryView();
        }

        private void UpdateMainViewUi()
        {
            var isLibrary = _mainViewMode == MainViewMode.Library;
            var isCatalog = _mainViewMode == MainViewMode.AppCatalog;
            var isReview = isCatalog && _appCatalogSubView == AppCatalogSubView.Review;
            var isAppUpdatesReview = isLibrary && _isAppUpdatesReviewOpen;
            var isModsOverlay = isLibrary && _isModsOverlayOpen && !_isAppUpdatesReviewOpen;

            if (this.FindControl<Grid>("LibraryViewContainer") is Grid libraryView)
                libraryView.IsVisible = isLibrary;

            if (this.FindControl<Grid>("CatalogContentPanel") is Grid catalogPanel)
                catalogPanel.IsVisible = isCatalog;

            if (this.FindControl<ScrollViewer>("CatalogSourcesPanel") is ScrollViewer sourcesPanel)
                sourcesPanel.IsVisible = isCatalog && !isReview;

            if (this.FindControl<Grid>("CatalogReviewPanel") is Grid reviewPanel)
                reviewPanel.IsVisible = isReview;

            if (this.FindControl<Grid>("AppUpdatesReviewPanel") is Grid appUpdatesPanel)
                appUpdatesPanel.IsVisible = isAppUpdatesReview;

            if (this.FindControl<Grid>("ModsPanel") is Grid modsPanel)
                modsPanel.IsVisible = isModsOverlay;

            if (this.FindControl<StackPanel>("LibraryTopBarPanel") is StackPanel libraryTopBar)
                libraryTopBar.IsVisible = isLibrary && !isAppUpdatesReview && !isModsOverlay;

            if (this.FindControl<StackPanel>("CatalogReviewTopBarPanel") is StackPanel catalogReviewTopBar)
                catalogReviewTopBar.IsVisible = isReview;

            if (this.FindControl<Button>("CatalogReviewBackButton") is Button backButton)
                backButton.IsVisible = isReview;

            if (this.FindControl<Grid>("LibraryFiltersPanel") is Grid libraryFilters)
                libraryFilters.IsVisible = isLibrary && !isAppUpdatesReview && !isModsOverlay;

            LibraryNavButton?.Classes.Set("selected", isLibrary);
            AppCatalogNavButton?.Classes.Set("selected", isCatalog);

            if (this.FindControl<TextBlock>("HeaderTitleText") is TextBlock headerTitle)
            {
                headerTitle.Text = isModsOverlay
                    ? "Mods"
                    : isAppUpdatesReview
                        ? "App Updates"
                        : isLibrary
                            ? "Library"
                            : isReview && _activeCatalogSyncSource != null
                                ? $"Review: {_activeCatalogSyncSource.Name}"
                                : "App Catalog";
            }

            UpdateLibraryEmptyState();
            NotifyGamepadUiChanged();
        }

        private void UpdateLibraryEmptyState()
        {
            var showLibrary = _mainViewMode == MainViewMode.Library && !_isAppUpdatesReviewOpen && !_isModsOverlayOpen;
            var showEmptyState = showLibrary && IsLibraryEmpty;

            if (EmptyLibraryPanel != null)
                EmptyLibraryPanel.IsVisible = showEmptyState;

            if (LibraryContentPanel != null)
                LibraryContentPanel.IsVisible = showLibrary && !showEmptyState;
        }

        private void EmptyLibraryAddApp_Click(object? sender, RoutedEventArgs e) =>
            ShowEntryFormOverlay(forCreate: true);

        private async void EmptyLibraryBrowseCatalog_Click(object? sender, RoutedEventArgs e) =>
            await OpenCommunityCatalogFromLibraryAsync();

        private Task OpenCommunityCatalogFromLibraryAsync()
        {
            AppCatalogService.MigrateLegacyCatalogSources(_settings);
            OnSettingChanged();

            ShowAppCatalogSourcesView();
            return Task.CompletedTask;
        }

        private static readonly (string Tag, CatalogReviewFilter Filter)[] CatalogReviewFilters =
        [
            ("All", CatalogReviewFilter.All),
            ("NeedsReview", CatalogReviewFilter.NeedsReview),
            ("New", CatalogReviewFilter.New),
            ("NotInLibrary", CatalogReviewFilter.NotInLibrary),
            ("Changed", CatalogReviewFilter.Changed),
            ("UpToDate", CatalogReviewFilter.UpToDate),
            ("Hidden", CatalogReviewFilter.Hidden),
        ];

        private void CatalogTagChip_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
                return;

            _catalogSyncViewModel.CycleTagChip(tag);
            ApplyCatalogSyncFilter();
        }

        private void CatalogSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _catalogSyncViewModel.SearchText = CatalogSearchTextBox?.Text ?? "";
            if (_activeCatalogSyncSource != null)
                ApplyCatalogSyncFilter();
        }

        private void CatalogReviewFilter_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
                return;

            var filter = CatalogReviewFilters.FirstOrDefault(f => f.Tag == tag).Filter;
            _catalogSyncViewModel.ReviewFilter = filter;
            RefreshCatalogReviewFilterButtons(tag);
            ApplyCatalogSyncFilter();
        }

        private void RefreshCatalogReviewFilterButtons(string selectedTag)
        {
            foreach (var (tag, _) in CatalogReviewFilters)
            {
                var buttonName = tag switch
                {
                    "All" => "CatalogFilterAllButton",
                    "NeedsReview" => "CatalogFilterNeedsReviewButton",
                    "New" => "CatalogFilterNewButton",
                    "NotInLibrary" => "CatalogFilterNotInLibraryButton",
                    "Changed" => "CatalogFilterChangedButton",
                    "UpToDate" => "CatalogFilterUpToDateButton",
                    "Hidden" => "CatalogFilterHiddenButton",
                    _ => null,
                };

                if (buttonName != null && this.FindControl<Button>(buttonName) is Button button)
                    button.Classes.Set("selected", tag == selectedTag);
            }

            UpdateCatalogReviewFilterChipLabels();
        }

        private void UpdateCatalogReviewFilterChipLabels()
        {
            if (this.FindControl<Button>("CatalogFilterNeedsReviewButton") is Button needsReviewButton)
            {
                var count = _catalogSyncViewModel.NeedsReviewCount;
                needsReviewButton.Content = count > 0 ? $"Needs review ({count})" : "Needs review";
            }

            if (this.FindControl<Button>("CatalogFilterNotInLibraryButton") is Button notInLibraryButton)
            {
                var count = _catalogSyncViewModel.NotInLibraryCount;
                notInLibraryButton.Content = count > 0 ? $"Not in library ({count})" : "Not in library";
            }

            if (this.FindControl<Button>("CatalogFilterHiddenButton") is Button hiddenButton)
            {
                var count = _catalogSyncViewModel.HiddenCount;
                hiddenButton.Content = count > 0 ? $"Hidden ({count})" : "Hidden";
            }
        }

        private async Task RefreshAllCatalogPendingCountsAsync()
        {
            foreach (var source in _settings.AppCatalogSources.Where(s => s.Enabled))
                await _gameManager.CatalogService.RefreshUpdateAvailableAsync(source);

            RefreshCatalogSourcesList();
            await ApplyLibraryCatalogPendingBadgesAsync();
        }

        private async Task ApplyLibraryCatalogPendingBadgesAsync()
        {
            if (_gameManager?.Games == null || _settings == null)
                return;

            await _gameManager.CatalogService.ApplyPendingCatalogChangeFlagsAsync(
                _gameManager.Games,
                _settings);
        }

        private async void AddCatalogSource_Click(object? sender, RoutedEventArgs e)
        {
            var location = await ShowAddCatalogSourceDialogAsync();
            if (location == null)
                return;

            var (apps, version, error) = await _gameManager.CatalogService.TryLoadSourceAsync(_gameManager.HttpClient, location);
            if (error != null)
            {
                await ShowMessageBoxAsync($"Could not load catalog source:\n{error}", "Invalid Source");
                return;
            }

            if (apps.Count == 0)
            {
                await ShowMessageBoxAsync("The catalog source loaded successfully but contains no apps.", "Empty Catalog");
                return;
            }

            var source = _catalogViewModel.CreateSource(location);
            string? rawJson = null;
            try
            {
                rawJson = await CatalogLocationReader.Default.ReadAsync(_gameManager.HttpClient, location);
            }
            catch
            {
                // RegisterNewSourceAsync will re-fetch if needed.
            }

            await _gameManager.CatalogService.RegisterNewSourceAsync(_gameManager.HttpClient, source, rawJson);

            _settings.AppCatalogSources.Add(source);
            OnSettingChanged();
            RefreshCatalogSourcesList();

            var versionLabel = string.IsNullOrWhiteSpace(version) ? "unknown" : version;
            await ShowMessageBoxAsync($"Added \"{source.Name}\" ({apps.Count} app(s), v{versionLabel}). Use App Catalog → Review to add apps to your library.", "Source Added");
        }

        private async void RemoveCatalogSource_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string sourceId })
                return;

            var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
                return;

            var confirmed = await ShowMessageBoxAsync(
                $"Remove catalog source \"{source.Name}\"?\n\nApps already in your local apps.json will stay in your library.",
                "Remove Source",
                true);

            if (!confirmed)
                return;

            _gameManager.CatalogService.DeleteSourceCache(sourceId);
            _settings.AppCatalogSources.RemoveAll(s => s.Id == sourceId);
            OnSettingChanged();
            RefreshCatalogSourcesList();
        }

        private async void CatalogSourceEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_suppressCatalogSourceUiEvents)
                return;

            if (sender is not CheckBox checkBox || checkBox.Tag is not string sourceId)
                return;

            _settings.EnsureInitialized();
            var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
                return;

            source.Enabled = checkBox.IsChecked == true;
            OnSettingChanged();

            await _gameManager.CatalogService.RefreshUpdateAvailableAsync(source);
            RefreshCatalogSourcesList();

            await _gameManager.LoadGamesAsync();
            ApplySorting();
            await ApplyLibraryCatalogPendingBadgesAsync();
        }

        private async void RefreshCatalogSources_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Stay on Refresh while the list rebuilds and any Catalog Updates prompt
                // is shown — SyncCatalogGamepadSelection must not jump to a source card.
                if (_settings.EnableGamepadInput && RefreshCatalogSourcesButton != null)
                {
                    var toolbar = CollectCatalogSourcesToolbarControls();
                    var refreshIndex = toolbar.FindIndex(c => ReferenceEquals(c, RefreshCatalogSourcesButton));
                    if (refreshIndex >= 0)
                        ApplyCatalogSourcesToolbarSelection(refreshIndex);
                }

                await _gameManager.CatalogService.RefreshAllSourcesAsync(_gameManager.HttpClient, _settings);
                OnSettingChanged();
                _settings = AppSettings.Load();
                _settings.EnsureInitialized();
                await RefreshCatalogSourcesListAsync();
                await ApplyLibraryCatalogPendingBadgesAsync();

                if (UpdatePromptPolicy.ShouldPromptCatalogUpdates(_settings))
                    await TryPromptCatalogReviewAsync();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to refresh catalog sources: {ex.Message}", "Refresh Error");
            }
        }

        private async Task<bool> TryPromptCatalogReviewAsync()
        {
            if (!UpdatePromptPolicy.ShouldPromptCatalogUpdates(_settings))
                return false;

            if (!_settings.AppCatalogSources.Any(s => s.Enabled && s.UpdateAvailable))
                return false;

            var alreadyInAppCatalog = _mainViewMode == MainViewMode.AppCatalog;
            var openCatalog = await ShowMessageBoxAsync(
                FormatPendingReviewSourcesMessage(
                    _settings,
                    includeOpenPrompt: true,
                    alreadyInAppCatalog: alreadyInAppCatalog),
                "Catalog Updates",
                true);

            if (openCatalog)
                await OpenAppCatalogForReviewAsync();

            return true;
        }

        private async Task NotifyCatalogUpdatesIfNeededAsync()
        {
            if (_app != null)
                await _app.StartupSelfUpdatePromptCompleted;

            if (!_settings.LocalFirstCatalogMigrationComplete)
            {
                await RunLocalFirstCatalogMigrationAsync();
                return;
            }

            if (!UpdatePromptPolicy.ShouldPromptCatalogUpdates(_settings))
                return;

            await TryPromptCatalogReviewAsync();
        }

        private List<GameInfo> GetPendingAppUpdates() =>
            Games
                .Where(g => g.Status == GameStatus.UpdateAvailable)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private List<GameInfo> GetAppUpdateReviewRows() =>
            Games
                .Where(g => g.Status is GameStatus.UpdateAvailable
                    or GameStatus.Updating
                    or GameStatus.Installing)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private async Task<bool> TryPromptAppUpdatesReviewAsync()
        {
            if (!UpdatePromptPolicy.ShouldPromptAppUpdateReviews(_settings))
                return false;

            // After auto-updates run, anything still pending needs review (including auto apps
            // that could not resolve a platform asset).
            var pendingGames = GetPendingAppUpdates();
            if (pendingGames.Count == 0)
                return false;

            var openReview = await ShowMessageBoxAsync(
                AppUpdateReviewMessages.FormatPendingAppUpdatesMessage(pendingGames, includeOpenPrompt: true),
                "App Updates",
                true);

            if (openReview)
                OpenAppUpdatesReview();

            return true;
        }

        /// <summary>
        /// Silently installs updates for apps with AutoUpdate enabled.
        /// Returns how many installs completed. Unresolved multi-asset apps are left pending.
        /// </summary>
        private async Task<int> ApplyAutoUpdatesAsync(bool showFailureSummary)
        {
            var autoGames = AppUpdateSelection.GetAutoPendingUpdates(Games);
            if (autoGames.Count == 0)
                return 0;

            var updated = 0;
            var failures = new List<string>();

            foreach (var game in autoGames)
            {
                if (game.Status != GameStatus.UpdateAvailable)
                    continue;

                try
                {
                    var installed = await HandleUpdateNowAsync(
                        this,
                        game,
                        preferAutoPlatform: true,
                        allowAssetPicker: false);
                    if (installed)
                        updated++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{game.Name}: {ex.Message}");
                }
            }

            if (updated > 0)
            {
                ApplySorting();
                UpdateContinueButtonState();
            }

            RefreshUpdateCheckStatus();
            NotifyUpdateCheckUiProperties();

            if (showFailureSummary &&
                failures.Count > 0 &&
                IsVisible &&
                WindowState != WindowState.Minimized)
            {
                await ShowMessageBoxAsync(
                    "Some automatic updates could not be completed:\n\n" + string.Join('\n', failures),
                    "Auto Update");
            }

            return updated;
        }

        private enum CombinedUpdateChoice
        {
            Dismiss,
            UpdateQuiver,
            UpdateApps,
        }

        private async Task<CombinedUpdateChoice> PromptCombinedUpdatesAsync(
            string? launcherVersion,
            IReadOnlyList<GameInfo> pendingApps)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                    desktop.MainWindow == null)
                {
                    return CombinedUpdateChoice.Dismiss;
                }

                var message = AppUpdateReviewMessages.FormatCombinedUpdatesMessage(launcherVersion, pendingApps);
                var choice = CombinedUpdateChoice.Dismiss;

                var messageBox = new Window
                {
                    Title = "Updates Available",
                    MinWidth = 420,
                    MaxWidth = 520,
                    MaxHeight = 520,
                    CanResize = true,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                var scrollViewer = new ScrollViewer
                {
                    MaxHeight = 360,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                    },
                };

                var updateQuiverButton = new Button
                {
                    Content = "Update Quiver Launcher",
                    MinWidth = 110,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                var updateAppsButton = new Button
                {
                    Content = "Update Apps",
                    MinWidth = 110,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                var dismissButton = new Button
                {
                    Content = "Not now",
                    MinWidth = 80,
                };

                updateQuiverButton.Click += (_, _) =>
                {
                    choice = CombinedUpdateChoice.UpdateQuiver;
                    messageBox.Close();
                };
                updateAppsButton.Click += (_, _) =>
                {
                    choice = CombinedUpdateChoice.UpdateApps;
                    messageBox.Close();
                };
                dismissButton.Click += (_, _) =>
                {
                    choice = CombinedUpdateChoice.Dismiss;
                    messageBox.Close();
                };

                messageBox.Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 16,
                    Children =
                    {
                        scrollViewer,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children = { updateQuiverButton, updateAppsButton, dismissButton },
                        },
                    },
                };

                GamepadModalDialogNavigation.Attach(messageBox);
                await messageBox.ShowDialog(desktop.MainWindow);
                return choice;
            });
        }

        private static string FormatPendingReviewSourcesMessage(
            AppSettings settings,
            bool includeOpenPrompt,
            bool alreadyInAppCatalog = false)
        {
            var pendingSources = settings.AppCatalogSources
                .Where(s => s.Enabled && s.PendingReviewCount > 0)
                .OrderByDescending(s => s.PendingReviewCount)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pendingSources.Count == 0)
            {
                if (!includeOpenPrompt)
                    return "Catalog updates are available. Open App Catalog to review and sync apps.";

                return alreadyInAppCatalog
                    ? "Catalog updates are available. Review changes now?"
                    : "Catalog updates are available. Open App Catalog now to review changes?";
            }

            var lines = pendingSources
                .Select(s => $"• {s.Name} ({s.PendingReviewCount})");
            var body = "Catalog updates are available:\n\n" + string.Join("\n", lines);
            if (!includeOpenPrompt)
                return body + "\n\nOpen App Catalog to review and sync apps.";

            return alreadyInAppCatalog
                ? body + "\n\nReview these changes now?"
                : body + "\n\nOpen App Catalog to review these sources?";
        }

        private async Task OpenAppCatalogForReviewAsync()
        {
            ShowAppCatalogSourcesView();

            var source = _settings.AppCatalogSources
                .Where(s => s.Enabled && s.PendingReviewCount > 0)
                .OrderByDescending(s => s.PendingReviewCount)
                .FirstOrDefault();

            if (source != null)
                await OpenCatalogReviewAsync(source.Id, CatalogReviewFilter.NeedsReview);
        }

        private async Task RunLocalFirstCatalogMigrationAsync()
        {
            AppCatalogService.MigrateLegacyCatalogSources(_settings);
            OnSettingChanged();

            await ShowFirstRunWelcomeAsync();

            ShowAppCatalogSourcesView();

            _settings.LocalFirstCatalogMigrationComplete = true;
            OnSettingChanged();
        }

        private Task ShowFirstRunWelcomeAsync() =>
            ShowWelcomeMessageBoxAsync(
                CommunityCatalogDefaults.FirstRunWelcomeMessage,
                CommunityCatalogDefaults.FirstRunWelcomeTitle);

        private static string GetCatalogReviewFilterTag(CatalogReviewFilter filter)
        {
            foreach (var (tag, value) in CatalogReviewFilters)
            {
                if (value == filter)
                    return tag;
            }

            return "All";
        }

        private async void ReviewCatalogChanges_Click(object? sender, RoutedEventArgs e)
        {
            var game = (sender as MenuItem)?.CommandParameter as GameInfo
                       ?? (sender as Button)?.CommandParameter as GameInfo
                       ?? (sender as Control)?.DataContext as GameInfo;
            if (game == null)
                return;

            await OpenCatalogReviewForLibraryAppAsync(game);
        }

        private async Task OpenCatalogReviewForLibraryAppAsync(GameInfo game)
        {
            if (_settings == null || _gameManager == null)
                return;

            var sourceId = await _gameManager.CatalogService.FindPendingCatalogSourceIdAsync(game, _settings);
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                await ShowMessageBoxAsync(
                    "Could not find this app in a catalog source with pending changes.",
                    "Catalog Review");
                return;
            }

            await OpenCatalogReviewAsync(sourceId, CatalogReviewFilter.NeedsReview);

            var index = -1;
            for (var i = 0; i < CatalogSyncRows.Count; i++)
            {
                var row = CatalogSyncRows[i];
                if (string.Equals(row.IdentityKey, game.IdentityKey, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(game.Repository) &&
                     string.Equals(row.Repository, game.Repository, StringComparison.OrdinalIgnoreCase)))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                ApplyCatalogReviewRowSelection(index);
        }

        private async void ReviewCatalogSource_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string sourceId })
                return;

            var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == sourceId);
            var filter = source is { PendingReviewCount: > 0 }
                ? CatalogReviewFilter.NeedsReview
                : CatalogReviewFilter.All;
            await OpenCatalogReviewAsync(sourceId, filter);
        }

        private async Task OpenCatalogReviewAsync(
            string sourceId,
            CatalogReviewFilter? initialFilter = null)
        {
            var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
                return;

            await _gameManager.CatalogService.FetchSourceAsync(_gameManager.HttpClient, source);
            OnSettingChanged();

            var localApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var externalApps = await _gameManager.CatalogService.LoadCachedAppsAsync(source.Id);

            var filter = initialFilter ?? (
                source.PendingReviewCount > 0 || source.UpdateAvailable
                    ? CatalogReviewFilter.NeedsReview
                    : CatalogReviewFilter.All);

            _activeCatalogSyncSource = source;
            _catalogSyncViewModel.ReviewFilter = filter;
            _catalogSyncViewModel.SortBy = _currentCatalogReviewSortBy;
            _catalogSyncViewModel.IgnoreArticlesWhenSorting = _settings.IgnoreArticlesWhenSorting;
            _catalogSyncViewModel.SearchText = "";
            if (CatalogSearchTextBox != null)
                CatalogSearchTextBox.Text = "";
            _catalogSyncViewModel.Refresh(source, localApps, externalApps, _settings);

            RefreshCatalogReviewFilterButtons(GetCatalogReviewFilterTag(filter));
            ApplyCatalogSyncFilter();
            UpdateCatalogSyncBulkButtons();

            ShowAppCatalogReviewView(source);
        }

        private void ApplyCatalogSyncFilter()
        {
            _catalogSyncViewModel.IgnoreArticlesWhenSorting = _settings.IgnoreArticlesWhenSorting;
            CatalogSyncRows.Clear();
            var isHiddenFilter = _catalogSyncViewModel.ReviewFilter == CatalogReviewFilter.Hidden;
            foreach (var row in _catalogSyncViewModel.GetFilteredRows())
            {
                row.ShowHideButton = !isHiddenFilter &&
                                     _activeCatalogSyncSource != null &&
                                     !CatalogCompareService.IsHiddenFromReview(_activeCatalogSyncSource, row.Repository);
                row.ShowUnhideButton = isHiddenFilter;
                CatalogSyncRows.Add(row);
            }

            OnPropertyChanged(nameof(HasCatalogTagChips));

            if (this.FindControl<TextBlock>("CatalogReviewVersionText") is TextBlock versionText)
            {
                var summary = _catalogSyncViewModel.VersionBannerText;
                versionText.Text = summary;
                var unreviewed = summary.Contains("not yet", StringComparison.OrdinalIgnoreCase);
                versionText.Classes.Set("catalog-review-version-unreviewed", unreviewed);
            }

            if (this.FindControl<Border>("CatalogReviewVersionBanner") is Border versionBanner)
            {
                versionBanner.IsVisible = !string.IsNullOrWhiteSpace(_catalogSyncViewModel.VersionBannerText);
            }

            if (this.FindControl<TextBlock>("CatalogSyncEmptyText") is TextBlock emptyText)
            {
                var showNeedsReviewComplete = _catalogSyncViewModel.ShowNeedsReviewCompleteState;
                emptyText.Text = isHiddenFilter
                    ? "No hidden apps for this source."
                    : "No apps match this filter.";
                emptyText.IsVisible = CatalogSyncRows.Count == 0 && !showNeedsReviewComplete;
            }

            if (this.FindControl<StackPanel>("CatalogSyncNeedsReviewEmptyPanel") is StackPanel needsReviewEmptyPanel)
                needsReviewEmptyPanel.IsVisible = _catalogSyncViewModel.ShowNeedsReviewCompleteState;

            UpdateCatalogReviewFilterChipLabels();
            UpdateCatalogSyncBulkButtons();

            if (_settings.EnableGamepadInput &&
                _mainViewMode == MainViewMode.AppCatalog &&
                _appCatalogSubView == AppCatalogSubView.Review &&
                (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewList ||
                 _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions))
            {
                SyncCatalogReviewGamepadSelection();
            }
        }

        private void CatalogSyncBackToLibrary_Click(object? sender, RoutedEventArgs e) =>
            ShowLibraryView();

        private void CatalogSyncBackToSources_Click(object? sender, RoutedEventArgs e) =>
            ShowAppCatalogSourcesView();

        private void UpdateCatalogSyncBulkButtons()
        {
            var addCount = _catalogSyncViewModel.FilteredBulkAddCount;
            var replaceCount = _catalogSyncViewModel.FilteredBulkReplaceCount;

            if (this.FindControl<Button>("CatalogSyncAddAllButton") is Button addAllButton)
            {
                addAllButton.Content = $"Add all new ({addCount})";
                addAllButton.IsEnabled = addCount > 0;
            }

            if (this.FindControl<Button>("CatalogSyncReplaceAllButton") is Button replaceAllButton)
            {
                replaceAllButton.Content = $"Replace all changed ({replaceCount})";
                replaceAllButton.IsEnabled = replaceCount > 0;
            }

            if (this.FindControl<Button>("CatalogSyncAcknowledgeButton") is Button skipReviewButton)
                skipReviewButton.IsVisible = _catalogSyncViewModel.ShowSkipReviewButton;
        }

        private CatalogSyncRowItem? FindCatalogSyncRow(string repository) =>
            _catalogSyncViewModel.AllRows.FirstOrDefault(r =>
                r.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));

        private async Task ApplyCatalogSyncLocalAppsAsync(List<GameInfo> localApps)
        {
            var previousApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var previousByIdentity = CatalogCompareService.IndexByIdentityKey(previousApps);

            await _gameManager.CatalogService.SaveLocalAppsAsync(localApps);

            foreach (var app in localApps.Where(a => !string.IsNullOrWhiteSpace(a.Repository)))
            {
                previousByIdentity.TryGetValue(app.IdentityKey, out var previous);
                AppFilesToAddService.SyncForGame(
                    app,
                    _gameManager.GamesFolder,
                    previous?.FilesToAdd);
            }

            await _gameManager.LoadGamesAsync();
            ApplySorting();

            if (_activeCatalogSyncSource != null)
            {
                await _gameManager.CatalogService.RefreshUpdateAvailableAsync(_activeCatalogSyncSource);
                OnSettingChanged();
                RefreshCatalogSourcesList();
            }

            await ApplyLibraryCatalogPendingBadgesAsync();
            await RefreshActiveCatalogSyncRowsAsync(localApps);
        }

        private async Task RefreshActiveCatalogSyncRowsAsync(List<GameInfo>? localApps = null)
        {
            if (_activeCatalogSyncSource == null)
                return;

            localApps ??= await _gameManager.CatalogService.LoadLocalAppsAsync();
            var externalApps = await _gameManager.CatalogService.LoadCachedAppsAsync(_activeCatalogSyncSource.Id);
            _catalogSyncViewModel.Refresh(_activeCatalogSyncSource, localApps, externalApps, _settings);
            ApplyCatalogSyncFilter();
            UpdateCatalogSyncBulkButtons();
        }

        private async Task AfterCatalogSyncMutationAsync()
        {
            if (_activeCatalogSyncSource == null)
                return;

            await _gameManager.CatalogService.RefreshUpdateAvailableAsync(_activeCatalogSyncSource);
            OnSettingChanged();
            RefreshCatalogSourcesList();
            await ApplyLibraryCatalogPendingBadgesAsync();
            await RefreshActiveCatalogSyncRowsAsync();
        }

        private async void CatalogSyncAddAll_Click(object? sender, RoutedEventArgs e)
        {
            var rows = _catalogSyncViewModel.GetFilteredBulkAddRows();
            if (rows.Count == 0)
                return;

            var localApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var updated = CatalogCompareService.ApplyAddAllExternalOnly(
                localApps,
                rows,
                _settings.AutoUpdateNewlyAddedApps);
            await ApplyCatalogSyncLocalAppsAsync(updated);
        }

        private async void CatalogSyncReplaceAll_Click(object? sender, RoutedEventArgs e)
        {
            var rows = _catalogSyncViewModel.GetFilteredBulkReplaceRows();
            if (rows.Count == 0)
                return;

            var localApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var updated = CatalogCompareService.ApplyReplaceAllChanged(localApps, rows);
            await ApplyCatalogSyncLocalAppsAsync(updated);
        }

        private async void CatalogSyncAcknowledge_Click(object? sender, RoutedEventArgs e)
        {
            if (_activeCatalogSyncSource == null)
                return;

            _gameManager.CatalogService.AcknowledgeSourceVersion(_activeCatalogSyncSource);
            OnSettingChanged();
            RefreshCatalogSourcesList();
            ApplyCatalogSyncFilter();
            UpdateCatalogSyncBulkButtons();
        }

        private async void CatalogSyncRowIgnore_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string repository } || _activeCatalogSyncSource == null)
                return;

            var row = FindCatalogSyncRow(repository);
            if (row == null || !row.CanIgnore)
                return;

            CatalogCompareService.IgnoreChangesForCurrentVersion(_activeCatalogSyncSource, repository);
            await AfterCatalogSyncMutationAsync();
        }

        private async void CatalogSyncRowHide_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string repository } || _activeCatalogSyncSource == null)
                return;

            var row = FindCatalogSyncRow(repository);
            if (row == null)
                return;

            CatalogCompareService.HideFromReview(_activeCatalogSyncSource, repository);
            await AfterCatalogSyncMutationAsync();
        }

        private async void CatalogSyncRowUnhide_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string repository } || _activeCatalogSyncSource == null)
                return;

            var row = FindCatalogSyncRow(repository);
            if (row == null)
                return;

            CatalogCompareService.UnhideFromReview(_activeCatalogSyncSource, repository);
            await AfterCatalogSyncMutationAsync();
        }

        private async void CatalogSyncRowAdd_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string repository })
                return;

            var row = FindCatalogSyncRow(repository);
            if (row == null)
                return;

            var localApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var updated = CatalogCompareService.ApplyRowAdd(
                localApps,
                row,
                _settings.AutoUpdateNewlyAddedApps);
            CatalogCompareService.ClearIgnoredChange(_activeCatalogSyncSource!, row.Repository);
            await ApplyCatalogSyncLocalAppsAsync(updated);
        }

        private async void CatalogSyncRowReplace_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string repository })
                return;

            var row = FindCatalogSyncRow(repository);
            if (row == null)
                return;

            var localApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var updated = CatalogCompareService.ApplyRowReplace(localApps, row);
            CatalogCompareService.ClearIgnoredChange(_activeCatalogSyncSource!, row.Repository);
            await ApplyCatalogSyncLocalAppsAsync(updated);
        }

        private async void CatalogSyncRowMerge_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string repository })
                return;

            var row = FindCatalogSyncRow(repository);
            if (row == null)
                return;

            var localApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var updated = CatalogCompareService.ApplyRowMerge(localApps, row);
            CatalogCompareService.ClearIgnoredChange(_activeCatalogSyncSource!, row.Repository);
            await ApplyCatalogSyncLocalAppsAsync(updated);
        }

        private async void CatalogSyncRowRemoveFromLibrary_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string repository } || _activeCatalogSyncSource == null)
                return;

            var row = FindCatalogSyncRow(repository);
            if (row == null || !row.CanRemoveFromLibrary)
                return;

            var confirm = await ShowMessageBoxAsync(
                $"Are you sure you want to remove '{row.DisplayName}' from your apps list?\n\nThis will only remove it from the launcher, not delete your files.",
                "Confirm Removal",
                true);

            if (!confirm)
                return;

            var localApps = await _gameManager.CatalogService.LoadLocalAppsAsync();
            var updated = CatalogCompareService.ApplyRowRemove(localApps, row);
            CatalogCompareService.IgnoreChangesForCurrentVersion(_activeCatalogSyncSource, row.Repository);
            await ApplyCatalogSyncLocalAppsAsync(updated);
        }

        private async Task<string?> ShowAddCatalogSourceDialogAsync()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow == null)
                return null;

            var locationBox = new TextBox
            {
                Watermark = "URL or file path to apps.json",
                Margin = new Thickness(0, 0, 0, 8),
            };
            locationBox.GotFocus += (_, e) =>
            {
                // Pointer focus keeps click position / selection; gamepad/tab append at end.
                if (e.NavigationMethod != NavigationMethod.Pointer)
                    GamepadControlActivation.MoveCaretToEnd(locationBox);
                SteamOnScreenKeyboard.TryOpen();
            };

            var browseButton = new Button
            {
                Content = "Browse…",
                Classes = { "options" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 12),
            };

            browseButton.Click += async (_, _) =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select apps.json",
                    FileTypeFilter = new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } },
                    AllowMultiple = false,
                });

                if (files?.Count > 0)
                    locationBox.Text = files[0].Path.LocalPath;
            };

            string? result = null;
            var addButton = new Button { Content = "Add", MinWidth = 80, Classes = { "options" } };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 80, Classes = { "options" } };

            var dialog = new Window
            {
                Title = "Add Catalog Source",
                Width = 420,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Location",
                            FontWeight = FontWeight.SemiBold,
                            Margin = new Thickness(0, 0, 0, 4),
                        },
                        locationBox,
                        browseButton,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Spacing = 10,
                            Children = { addButton, cancelButton },
                        },
                    },
                },
            };

            addButton.Click += (_, _) =>
            {
                var location = locationBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(location))
                    return;

                result = location;
                dialog.Close();
            };

            cancelButton.Click += (_, _) => dialog.Close();

            GamepadModalDialogNavigation.Attach(dialog);

            await dialog.ShowDialog(desktop.MainWindow);
            return result;
        }

        private void OnSettingChanged()
        {
            if (_suppressSettingsUiEvents)
                return;

            try
            {
                AppSettings.Save(_settings);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to save settings: {ex.Message}", "Save Error");
            }
        }

        private void StartFullscreenCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.StartFullscreen = true;
                OnSettingChanged();
            }
        }

        private void StartFullscreenCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.StartFullscreen = false;
                OnSettingChanged();
            }
        }

        private void ShowOSTopBarCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ShowOSTopBar = true;
                OnPropertyChanged(nameof(ExtendClientAreaEnabled));
                OnPropertyChanged(nameof(ChromeHints));
                OnPropertyChanged(nameof(WindowBackground));
                OnSettingChanged();
            }
        }

        private void ShowOSTopBarCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ShowOSTopBar = false;
                OnPropertyChanged(nameof(ExtendClientAreaEnabled));
                OnPropertyChanged(nameof(ChromeHints));
                OnPropertyChanged(nameof(WindowBackground));
                OnSettingChanged();
            }
        }

        private void UpdateGridLayoutVisibility()
        {
            if (_settings == null)
                return;

            var useGrid = _settings.UseGridView;
            var compact = _settings.GridCompactCards;

            if (ClassicGridViewControl != null)
                ClassicGridViewControl.IsVisible = useGrid && !compact;

            if (CompactGridViewControl != null)
                CompactGridViewControl.IsVisible = useGrid && compact;

            // Don't restore library card focus while Settings is open — App Cards
            // toggles/presets change layout and would steal the orange focus ring.
            if (_settings.EnableGamepadInput &&
                !isSettingsPanelOpen &&
                _mainViewMode == MainViewMode.Library)
            {
                SyncGamepadLibrarySelection();
            }
        }

        private void UseGridViewCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _settings.UseGridView = true;
            UpdateGridLayoutVisibility();
            OnSettingChanged();
        }

        private void UseGridViewCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _settings.UseGridView = false;
            UpdateGridLayoutVisibility();
            OnSettingChanged();
        }

        private void GridCompactCardsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _settings.GridCompactCards = true;
            UpdateGridLayoutVisibility();
            OnSettingChanged();
        }

        private void GridCompactCardsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _settings.GridCompactCards = false;
            UpdateGridLayoutVisibility();
            OnSettingChanged();
        }

        private void SlotSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.SlotSize = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void ActionButtonSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ActionButtonSize = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconOpacity = (float)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconSize = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconMarginSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconMargin = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void TextMarginSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.SlotTextMargin = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconFillCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconFill = true;
                OnSettingChanged();
            }
        }

        private void IconFillCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconFill = false;
                OnSettingChanged();
            }
        }

        private void PlatformAuto_Click(object sender, RoutedEventArgs e)
        {             
            if (_settings != null)
            {
                _settings.Platform = TargetOS.Auto;
                PlatformString = "Automatic";
                OnSettingChanged();
            }
        }

        private void PlatformWindows_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.Windows;
                PlatformString = "Windows";
                OnSettingChanged();
            }
        }

        private void PlatformMacOS_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.MacOS;
                PlatformString = "macOS";
                OnSettingChanged();
            }
        }

        private void PlatformLinuxX64_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.LinuxX64;
                PlatformString = "Linux x64";
                OnSettingChanged();
            }
        }

        private void PlatformLinuxARM64_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.LinuxARM64;
                PlatformString = "Linux ARM64";
                OnSettingChanged();
            }
        }

        private async void CheckforUpdates_Click(object sender, RoutedEventArgs e) =>
            await RunUpdateCheckAsync(promptForReview: true, isManualCheck: true);

        /// <summary>
        /// Shared update check used by the toolbar button, tray menu, and background timer.
        /// </summary>
        public async Task RunUpdateCheckAsync(bool promptForReview, bool isManualCheck)
        {
            if (IsCheckingUpdates)
                return;

            IsCheckingUpdates = true;
            try
            {
                ManualLauncherCheckResult launcherResult;
                if (isManualCheck && _app != null)
                {
                    launcherResult = await _app.CheckForAppUpdatesManually();
                }
                else
                {
                    var pending = (_app?.IsLauncherUpdatePending() ?? false)
                        || _velopackUpdateService.IsUpdatePendingRestart;
                    launcherResult = new ManualLauncherCheckResult
                    {
                        InstalledVersion = _velopackUpdateService.CurrentVersion
                            ?? LauncherVersionService.ReadInstalledVersion(
                                AppDomain.CurrentDomain.BaseDirectory),
                        CheckSucceeded = true,
                        LauncherUpdatePending = pending,
                        AvailableLauncherVersion = _velopackUpdateService.LastUpdateInfo?
                            .TargetFullRelease.Version.ToString(),
                    };
                }

                await _gameManager.CheckAllUpdatesAsync();
                await RefreshAllModUpdateBadgesAsync();
                ApplySorting();

                var autoUpdated = await ApplyAutoUpdatesAsync(showFailureSummary: promptForReview && IsVisible);
                RefreshUpdateCheckStatus(DateTime.Now);

                _lastLauncherCheckNote = launcherResult.CheckSucceeded ? null : "Could not check Quiver Launcher";
                NotifyUpdateCheckUiProperties();

                if (!promptForReview || !IsVisible)
                    return;

                var pendingApps = GetPendingAppUpdates();
                var launcherApp = _app;
                var launcherPending = launcherResult.LauncherUpdatePending && launcherApp != null;
                var promptApps = UpdatePromptPolicy.ShouldPromptAppUpdateReviews(_settings);
                var reviewableApps = promptApps ? pendingApps : new List<GameInfo>();

                if (launcherPending && launcherApp != null && reviewableApps.Count > 0)
                {
                    var choice = await PromptCombinedUpdatesAsync(
                        launcherResult.AvailableLauncherVersion,
                        reviewableApps);

                    if (choice == CombinedUpdateChoice.UpdateQuiver)
                        await launcherApp.ApplyPendingLauncherUpdateAsync();
                    else if (choice == CombinedUpdateChoice.UpdateApps)
                        OpenAppUpdatesReview();
                }
                else if (launcherPending && launcherApp != null)
                {
                    await launcherApp.PromptForPendingLauncherUpdateAsync();
                }
                else if (reviewableApps.Count > 0)
                {
                    await TryPromptAppUpdatesReviewAsync();
                }
                else if (autoUpdated > 0 && isManualCheck)
                {
                    await ShowMessageBoxAsync(
                        AppUpdateReviewMessages.FormatAutoUpdatedSummary(autoUpdated),
                        "App Updates");
                }
            }
            catch (Exception ex)
            {
                if (promptForReview && IsVisible)
                    await ShowMessageBoxAsync($"Failed to check for updates: {ex.Message}", "Error");
                RefreshUpdateCheckStatus();
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        private string GetLastCheckedText()
        {
            if (_lastUpdateCheckTime == null)
                return string.Empty;

            var timeSince = DateTime.Now - _lastUpdateCheckTime.Value;

            if (timeSince.TotalMinutes < 1)
                return "checked just now";
            if (timeSince.TotalMinutes < 2)
                return "checked 1 minute ago";
            if (timeSince.TotalMinutes < 60)
                return $"checked {timeSince.Minutes} minutes ago";
            if (timeSince.TotalHours < 2)
                return "checked 1 hour ago";
            if (timeSince.TotalHours < 24)
                return $"checked {timeSince.Hours} hours ago";
            if (timeSince.TotalDays < 2)
                return "checked 1 day ago";

            return $"checked {timeSince.Days} days ago";
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;
            if (game != null && !string.IsNullOrEmpty(game.FolderName))
            {
                try
                {
                    string folderPath = game.GetInstallPath(_gameManager.GamesFolder);
                    OpenUrl(folderPath);
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Failed to open folder: {ex.Message}", "Action Error");
                }
            }
            else
            {
                _ = ShowMessageBoxAsync("Unable to identify the game folder.", "Action Error");
            }
        }

        private async void ForceUpdate_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                await PersistGameVersionPreferencesAsync(game, null, null);
                await game.ForceUpdateAsync(_gameManager.HttpClient, _gameManager.GamesFolder);
                ApplySorting();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to force update {game.Name}: {ex.Message}", "Force Update Failed");
            }
        }

        private void GithubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://github.com/tgeorgiadis/quiver-launcher/";
                OpenUrl(url);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open Github link: {ex.Message}", "Action Error");
            }
        }

        private async void ConfigureWindowsRunner_Click(object? sender, RoutedEventArgs e)
        {
            var game = (sender as MenuItem)?.CommandParameter as GameInfo;
            if (game == null || string.IsNullOrWhiteSpace(game.FolderName))
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await ShowMessageBoxAsync("Windows runner settings are only used on Linux.", "Windows Runner");
                return;
            }

            try
            {
                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                var config = await GameDialogService.ShowLinuxWindowsRunnerDialogAsync(
                    gamePath,
                    LinuxWindowsRunnerConfig.FromGame(game),
                    isInstall: false);

                if (config == null)
                    return;

                config.ApplyTo(game);

                var allGames = await LoadGamesFromJsonAsync();
                var matchingGame = allGames.FirstOrDefault(g =>
                    !string.IsNullOrWhiteSpace(g.Repository) &&
                    g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

                if (matchingGame != null)
                {
                    matchingGame.LinuxRunner = game.LinuxRunner;
                    matchingGame.LinuxPrefixPath = game.LinuxPrefixPath;
                    matchingGame.LinuxProtonPath = game.LinuxProtonPath;
                    matchingGame.LinuxCustomLaunchCommand = game.LinuxCustomLaunchCommand;
                    await SaveGamesToJsonAsync(allGames);
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to save Windows runner settings: {ex.Message}", "Windows Runner");
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
                _ = ShowMessageBoxAsync($"Failed to open Discord link: {ex.Message}", "Action Error");
            }
        }

        private async void LaunchGameMenu_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                if (game.Status == GameStatus.NotInstalled)
                    game.ClearDownloadSelection();

                var launched = await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);

                var anchor = ResolveDownloadMenuAnchor(
                    game,
                    menuItem != null ? ResolveMenuAnchor(menuItem) : null);

                if (anchor != null && TryShowPendingSelectionMenus(anchor, game))
                    return;

                UpdateContinueButtonState();

                CloseAfterLaunchIfNeeded(launched);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to launch {game.Name}: {ex.Message}", "Launch Error");
            }
        }

        private async void LocateExistingInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Select existing install folder for {game.Name}",
                AllowMultiple = false
            });

            if (folders == null || folders.Count == 0)
                return;

            var selectedPath = folders[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
            {
                await ShowMessageBoxAsync("The selected install folder could not be found.", "Install Folder Not Found");
                return;
            }

            var folderName = Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                await ShowMessageBoxAsync("The selected folder does not have a valid folder name.", "Invalid Folder");
                return;
            }

            game.InstallPath = selectedPath;
            game.FolderName = folderName;

            await PersistGameInstallLocationAsync(game);
            await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder, forceUpdateCheck: true);

            ApplySorting();
            UpdateContinueButtonState();
        }

        private async void UpdateNowMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var anchor = ResolveMenuAnchor(menuItem) ?? FindGameMenuAnchor(game);
            if (anchor == null)
            {
                await ShowMessageBoxAsync("Unable to open the update menu for this game.", "Error");
                return;
            }

            await HandleUpdateNowAsync(anchor, game);
        }

        private async void AutoUpdateMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
                return;

            game.AutoUpdate = !game.AutoUpdate;
            await PersistAutoUpdatePreferenceAsync(game);
            RefreshUpdateCheckStatus();
            NotifyUpdateCheckUiProperties();

            if (game.AutoUpdate && game.Status == GameStatus.UpdateAvailable)
                await ApplyAutoUpdatesAsync(showFailureSummary: true);
        }

        private async void SkipUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            await HandleSkipUpdateAsync(game);
        }

        private async void ChangeVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var anchor = ResolveMenuAnchor(menuItem) ?? FindGameMenuAnchor(game);
            if (anchor == null)
            {
                await ShowMessageBoxAsync("Unable to open the version menu for this game.", "Error");
                return;
            }

            await HandleChangeVersionAsync(anchor, game);
        }

        private void OpenGitHubPage_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game != null && !string.IsNullOrEmpty(game.Repository))
            {
                try
                {
                    var url = RepositorySourceHelper.GetRepositoryPageUrl(game.RepositorySource, game.Repository);
                    OpenUrl(url);
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Failed to open repository page: {ex.Message}", "Error");
                }
            }
            else _ = ShowMessageBoxAsync("Failed to open repository page", "Error");
        }

        private async void SetCustomIcon_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var selectedGame = menuItem?.CommandParameter as GameInfo;
            if (selectedGame == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            bool hasExistingCustomIcon = !string.IsNullOrEmpty(selectedGame.CustomIconPath);
            string confirmMessage = hasExistingCustomIcon
                ? $"Replace the existing custom icon for {selectedGame.Name}?"
                : $"Set custom icon for {selectedGame.Name}?";

            if (hasExistingCustomIcon)
            {
                var confirmResult = await ShowMessageBoxAsync(confirmMessage, "Confirm Icon Replacement", true);
                if (!confirmResult)
                    return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Select Custom Icon for {selectedGame.Name}",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Image Files")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.ico" }
                    },
                    new FilePickerFileType("PNG Files") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG Files") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                    new FilePickerFileType("WebP Files") { Patterns = new[] { "*.webp" } },
                    new FilePickerFileType("Bitmap Files") { Patterns = new[] { "*.bmp" } },
                    new FilePickerFileType("Icon Files") { Patterns = new[] { "*.ico" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                },
                AllowMultiple = false
            });

            if (files?.Count > 0)
            {
                try
                {
                    var filePath = files[0].Path.LocalPath;
                    selectedGame.SetCustomIcon(filePath, _gameManager.CacheFolder);
                }
                catch (Exception ex)
                {
                    string errorMessage = hasExistingCustomIcon
                        ? $"Failed to replace custom icon: {ex.Message}"
                        : $"Failed to set custom icon: {ex.Message}";
                    _ = ShowMessageBoxAsync(errorMessage, "Error");
                }
            }
        }

        private async void RemoveCustomIcon_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var selectedGame = menuItem?.CommandParameter as GameInfo;
            if (selectedGame == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            if (string.IsNullOrEmpty(selectedGame.CustomIconPath))
            {
                _ = ShowMessageBoxAsync($"{selectedGame.Name} is already using the default icon.", "No Custom Icon");
                return;
            }

            var result = await ShowMessageBoxAsync($"Remove custom icon for {selectedGame.Name}?", "Confirm Removal", true);
            if (result)
            {
                try
                {
                    selectedGame.RemoveCustomIcon();
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Failed to remove custom icon: {ex.Message}", "Error");
                }
            }
        }

        private async void DeleteGameFromLibrary_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null) return;

            if (game.Status == GameStatus.NotInstalled)
            {
                _ = ShowMessageBoxAsync($"{game.Name} is not installed.", "Nothing to Delete");
                return;
            }

            var result = await ShowMessageBoxAsync(
                $"Are you sure you want to delete {game.Name}?\n\n" +
                "Quiver Launcher will attempt to move game files to your system's Recycle Bin / Trash so you can restore them if needed. " +
                "Portable installs may include save data in the same folder.",
                "Confirm Deletion",
                isQuestion: true,
                preferCancelDefault: true);

            if (result)
            {
                try
                {
                    if (string.IsNullOrEmpty(game.FolderName))
                    {
                        await ShowMessageBoxAsync($"Failed to delete {game.Name}: game folder is not configured.", "Deletion Failed");
                        return;
                    }

                    var gamePath = game.GetInstallPath(_gameManager.GamesFolder);

                    if (Directory.Exists(gamePath))
                    {
                        game.Status = GameStatus.Installing;
                        game.IsLoading = true;

                        await Task.Run(() => RecycleBinHelper.MoveToRecycleBin(gamePath));

                        game.IsLoading = false;
                        await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder);

                        UpdateContinueButtonState();
                    }
                    else
                    {
                        await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder);
                    }
                }
                catch (Exception ex)
                {
                    game.IsLoading = false;
                    _ = ShowMessageBoxAsync($"Failed to delete {game.Name}: {ex.Message}", "Deletion Failed");
                }
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", $"\"{url}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", $"\"{url}\"");
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open URL: {ex.Message}", "Error");
            }
        }

        private async Task ShowWelcomeMessageBoxAsync(string message, string title)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var messageBox = new Window
                    {
                        Title = title,
                        Width = 480,
                        Height = 260,
                        CanResize = false,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20),
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = message,
                                    TextWrapping = TextWrapping.Wrap,
                                    Margin = new Thickness(0, 0, 0, 20),
                                },
                                new Button
                                {
                                    Content = "OK",
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    MinWidth = 80,
                                },
                            },
                        },
                    };

                    if (((StackPanel)messageBox.Content).Children[1] is Button okButton)
                        okButton.Click += (_, _) => messageBox.Close();

                    GamepadModalDialogNavigation.Attach(messageBox);

                    await messageBox.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private static Window CreateScrollableMessageBoxWindow(
            string message,
            string title,
            bool isQuestion,
            Action<bool>? onQuestionResult = null,
            bool preferCancelDefault = false)
        {
            var messageBox = new Window
            {
                Title = title,
                MinWidth = 420,
                MaxWidth = 520,
                MaxHeight = 520,
                CanResize = true,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                },
            };

            Control buttonRow;
            if (isQuestion)
            {
                messageBox.Tag = false;
                var yesButton = new Button
                {
                    Content = "Yes",
                    Margin = new Thickness(0, 0, 10, 0),
                    MinWidth = 80,
                };
                var noButton = new Button
                {
                    Content = "No",
                    MinWidth = 80,
                };

                if (preferCancelDefault)
                {
                    noButton.IsDefault = true;
                    noButton.IsCancel = true;
                }
                else
                {
                    yesButton.IsDefault = true;
                    noButton.IsCancel = true;
                }

                yesButton.Click += (_, _) =>
                {
                    messageBox.Tag = true;
                    onQuestionResult?.Invoke(true);
                    messageBox.Close();
                };
                noButton.Click += (_, _) =>
                {
                    messageBox.Tag = false;
                    onQuestionResult?.Invoke(false);
                    messageBox.Close();
                };

                buttonRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { yesButton, noButton },
                };
            }
            else
            {
                var okButton = new Button
                {
                    Content = "OK",
                    Width = 80,
                    Padding = new Thickness(12, 6),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                okButton.Click += (_, _) => messageBox.Close();
                buttonRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { okButton },
                };
            }

            messageBox.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children = { scrollViewer, buttonRow },
            };

            if (isQuestion)
            {
                GamepadModalDialogNavigation.Attach(messageBox, accepted =>
                {
                    messageBox.Tag = accepted;
                    onQuestionResult?.Invoke(accepted);
                });
            }
            else
            {
                GamepadModalDialogNavigation.Attach(messageBox);
            }

            return messageBox;
        }

        private async Task ShowMessageBoxAsync(string message, string title)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var messageBox = CreateScrollableMessageBoxWindow(message, title, isQuestion: false);
                    await messageBox.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private void UnhideAllGamesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.ListScope = AppListScope.AllApps;
                OnSettingChanged();
                _gameManager.SetListScope(AppListScope.AllApps, _settings);
                RefreshSidebarFilterSelection();
                UpdateContinueButtonState();
                ApplySorting();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to show all apps: {ex.Message}", "Error");
            }
        }

        private void HideNonInstalledButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.ListScope = AppListScope.InstalledOnly;
                OnSettingChanged();
                _gameManager.SetListScope(AppListScope.InstalledOnly, _settings);
                RefreshSidebarFilterSelection();
                UpdateContinueButtonState();
                ApplySorting();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to filter installed apps: {ex.Message}", "Error");
            }
        }

        private void ShowHiddenGamesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.ListScope = AppListScope.HiddenOnly;
                OnSettingChanged();
                _gameManager.SetListScope(AppListScope.HiddenOnly, _settings);
                RefreshSidebarFilterSelection();
                UpdateContinueButtonState();
                ApplySorting();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to show hidden apps: {ex.Message}", "Error");
            }
        }

        private void AddDisplayFilter_Click(object? sender, RoutedEventArgs e)
        {
            ShowDisplayFilterOverlay(null);
        }

        private void ToggleTagDisplayFilter(string filterId)
        {
            if (string.Equals(_settings.ActiveTagDisplayFilterId, filterId, StringComparison.OrdinalIgnoreCase))
                _settings.ActiveTagDisplayFilterId = null;
            else
                _settings.ActiveTagDisplayFilterId = filterId;

            OnSettingChanged();
            _gameManager.ApplyTagDisplayFilter(_settings);
            RefreshTagDisplayFiltersUI();
            RefreshSidebarFilterSelection();
            UpdateContinueButtonState();
            ApplySorting();
        }

        private void TagDisplayFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tagFilterSuppressRowClick)
            {
                _tagFilterSuppressRowClick = false;
                return;
            }

            if (sender is not Button button || button.Tag is not string filterId)
                return;

            ToggleTagDisplayFilter(filterId);
        }

        private void TagDisplayFilterReorder_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border handle || handle.Tag is not string filterId)
                return;

            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
                return;

            if (TagDisplayFiltersItemsControl == null)
                return;

            _tagFilterSuppressRowClick = true;
            _tagFilterDragId = filterId;
            _tagFilterDragStartY = e.GetPosition(TagDisplayFiltersItemsControl).Y;
            _tagFilterDragActive = false;
            _tagFilterDragPointer = e.Pointer;
            _tagFilterDragRowButton = handle.GetVisualAncestors().OfType<Button>()
                .FirstOrDefault(b => b.Classes.Contains("display-filter-row"));

            if (_tagFilterDragRowButton != null)
                BeginTagDisplayFilterDragVisuals(_tagFilterDragRowButton);

            TagDisplayFiltersItemsControl.AddHandler(
                InputElement.PointerMovedEvent,
                TagDisplayFilterDragPointerMoved,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            TagDisplayFiltersItemsControl.AddHandler(
                InputElement.PointerReleasedEvent,
                TagDisplayFilterDragPointerReleased,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            e.Pointer.Capture(TagDisplayFiltersItemsControl);
            e.Handled = true;
        }

        private void TagDisplayFilterDragPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_tagFilterDragId == null || TagDisplayFiltersItemsControl == null)
                return;

            if (!e.GetCurrentPoint(TagDisplayFiltersItemsControl).Properties.IsLeftButtonPressed)
                return;

            var currentY = e.GetPosition(TagDisplayFiltersItemsControl).Y;
            if (!_tagFilterDragActive && Math.Abs(currentY - _tagFilterDragStartY) >= 3)
            {
                _tagFilterDragActive = true;
                _tagFilterOrderAtDragStart = _settings.TagDisplayFilters.Select(f => f.Id).ToList();
                if (TryMeasureTagFilterDragMetrics(_tagFilterDragId, out var listTop, out var rowStride))
                {
                    _tagFilterDragListTop = listTop;
                    _tagFilterDragRowStride = rowStride;
                }

                ReapplyTagDisplayFilterDragVisuals();
                _tagFilterDragPointer?.Capture(TagDisplayFiltersItemsControl);
            }

            if (!_tagFilterDragActive)
                return;

            var currentIndex = GetTagDisplayFilterIndex(_tagFilterDragId);
            if (currentIndex < 0 || _tagFilterDragRowStride <= 0)
                return;

            var targetIndex = TagDisplayFilterDragDrop.ResolveInsertIndex(
                TagDisplayFilters.Count,
                currentIndex,
                currentY,
                _tagFilterDragListTop,
                _tagFilterDragRowStride);

            if (targetIndex != currentIndex)
                PreviewMoveTagDisplayFilter(_tagFilterDragId, targetIndex);

            e.Handled = true;
        }

        private void TagDisplayFilterDragPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_tagFilterDragId == null)
                return;

            var didDrag = _tagFilterDragActive;
            var orderAtStart = _tagFilterOrderAtDragStart;

            EndTagFilterDragSession();

            if (e.Pointer.Captured != null)
                e.Pointer.Capture(null);

            if (didDrag)
            {
                if (orderAtStart != null &&
                    !orderAtStart.SequenceEqual(_settings.TagDisplayFilters.Select(f => f.Id)))
                {
                    OnSettingChanged();
                }
            }
            else
            {
                _tagFilterSuppressRowClick = true;
            }

            e.Handled = true;
        }

        private void BeginTagDisplayFilterDragVisuals(Button row)
        {
            row.Classes.Add("dragging");
            row.ZIndex = 10;
            _tagFilterDragRowButton = row;
            SetTagDisplayFilterTooltipsEnabled(false);
        }

        private void SetTagDisplayFilterTooltipsEnabled(bool enabled)
        {
            if (TagDisplayFiltersItemsControl == null)
                return;

            foreach (var row in TagDisplayFiltersItemsControl.GetVisualDescendants()
                         .OfType<Button>()
                         .Where(b => b.Classes.Contains("display-filter-row")))
            {
                ToolTip.SetServiceEnabled(row, enabled);
                if (!enabled)
                    ToolTip.SetIsOpen(row, false);
            }
        }

        private void ReapplyTagDisplayFilterDragVisuals()
        {
            if (_tagFilterDragId == null)
                return;

            var row = GetTagDisplayFilterRow(_tagFilterDragId);
            if (row == null)
                return;

            row.Classes.Add("dragging");
            row.ZIndex = 10;
            _tagFilterDragRowButton = row;
        }

        private Button? GetTagDisplayFilterRow(string filterId)
        {
            if (TagDisplayFiltersItemsControl == null)
                return null;

            return TagDisplayFiltersItemsControl.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b =>
                    b.Classes.Contains("display-filter-row") &&
                    b.Tag is string id &&
                    string.Equals(id, filterId, StringComparison.OrdinalIgnoreCase));
        }

        private int GetTagDisplayFilterIndex(string? filterId)
        {
            if (filterId == null)
                return -1;

            return _settings.TagDisplayFilters.FindIndex(f =>
                string.Equals(f.Id, filterId, StringComparison.OrdinalIgnoreCase));
        }

        private void PreviewMoveTagDisplayFilter(string filterId, int targetIndex)
        {
            _settings.EnsureInitialized();

            var fromIndex = GetTagDisplayFilterIndex(filterId);
            if (fromIndex < 0 || fromIndex == targetIndex)
                return;

            TagDisplayFilterReorder.Move(_settings.TagDisplayFilters, fromIndex, targetIndex);

            var uiFromIndex = TagDisplayFilters
                .Select((item, index) => new { item, index })
                .FirstOrDefault(x => string.Equals(x.item.Id, filterId, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;

            if (uiFromIndex >= 0)
            {
                var item = TagDisplayFilters[uiFromIndex];
                TagDisplayFilters.RemoveAt(uiFromIndex);
                TagDisplayFilters.Insert(targetIndex, item);
            }

            _tagFilterDragRowButton = GetTagDisplayFilterRow(filterId);
        }

        private void ClearTagDisplayFilterDragVisuals()
        {
            SetTagDisplayFilterTooltipsEnabled(true);

            if (_tagFilterDragId != null)
            {
                var row = GetTagDisplayFilterRow(_tagFilterDragId);
                if (row != null)
                {
                    row.Classes.Remove("dragging");
                    row.ZIndex = 0;
                }
            }

            _tagFilterDragRowButton?.Classes.Remove("dragging");
        }

        private void EndTagFilterDragSession()
        {
            if (TagDisplayFiltersItemsControl != null)
            {
                TagDisplayFiltersItemsControl.RemoveHandler(
                    InputElement.PointerMovedEvent,
                    TagDisplayFilterDragPointerMoved);
                TagDisplayFiltersItemsControl.RemoveHandler(
                    InputElement.PointerReleasedEvent,
                    TagDisplayFilterDragPointerReleased);
            }

            ClearTagDisplayFilterDragVisuals();

            _tagFilterDragId = null;
            _tagFilterDragActive = false;
            _tagFilterDragRowButton = null;
            _tagFilterDragPointer = null;
            _tagFilterOrderAtDragStart = null;
            _tagFilterDragListTop = 0;
            _tagFilterDragRowStride = 0;
            _tagFilterSuppressRowClick = false;
        }

        private bool TryMeasureTagFilterDragMetrics(string filterId, out double listTop, out double rowStride)
        {
            listTop = 0;
            rowStride = 0;

            if (TagDisplayFiltersItemsControl == null)
                return false;

            var row = GetTagDisplayFilterRow(filterId);
            if (row == null)
                return false;

            var transform = row.TransformToVisual(TagDisplayFiltersItemsControl);
            if (transform == null)
                return false;

            var rowHeight = row.Bounds.Height;
            if (rowHeight <= 0)
                return false;

            rowStride = rowHeight + 4;
            var dragIndex = GetTagDisplayFilterIndex(filterId);
            if (dragIndex < 0)
                return false;

            var rowTop = transform.Value.Transform(new Point(0, 0)).Y;
            listTop = rowTop - dragIndex * rowStride;
            return true;
        }

        private void DisplayFilterOverflow_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is Button button)
                OpenDisplayFilterOverflowMenu(button);
        }

        private void OpenDisplayFilterOverflowMenu(Button overflowButton)
        {
            if (overflowButton.ContextMenu == null)
                return;

            UpdateDisplayFilterOverflowMenuState(overflowButton);
            overflowButton.ContextMenu.PlacementTarget = overflowButton;
            overflowButton.ContextMenu.Placement = PlacementMode.Bottom;
            GamepadContextMenuNavigation.Attach(overflowButton.ContextMenu);
            overflowButton.ContextMenu.Open();
        }

        private void UpdateDisplayFilterOverflowMenuState(Button overflowButton)
        {
            if (overflowButton.ContextMenu == null)
                return;

            _settings.EnsureInitialized();
            var filterId = overflowButton.Tag as string;
            var index = string.IsNullOrEmpty(filterId) ? -1 : GetTagDisplayFilterIndex(filterId);
            var count = _settings.TagDisplayFilters.Count;

            foreach (var item in overflowButton.ContextMenu.Items.OfType<MenuItem>())
            {
                if (item.Header is not string header)
                    continue;

                if (string.Equals(header, "Move Up", StringComparison.Ordinal))
                    item.IsEnabled = index > 0;
                else if (string.Equals(header, "Move Down", StringComparison.Ordinal))
                    item.IsEnabled = index >= 0 && index < count - 1;
            }
        }

        private static Button? FindDisplayFilterOverflowButton(Button row) =>
            row.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Classes.Contains("display-filter-overflow") && b.ContextMenu != null);

        private bool TryOpenFocusedDisplayFilterOverflowMenu()
        {
            var controls = CollectSidebarFocusableControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.SidebarSelectedIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return false;

            if (controls[index] is not Button row || !row.Classes.Contains("display-filter-row"))
                return false;

            var overflow = FindDisplayFilterOverflowButton(row);
            if (overflow == null)
                return false;

            OpenDisplayFilterOverflowMenu(overflow);
            return true;
        }

        private static string? GetFilterIdFromSender(object? sender) =>
            sender switch
            {
                Button { Tag: string id } => id,
                MenuItem { Tag: string id } => id,
                _ => null
            };

        private void MoveTagDisplayFilterUp_Click(object? sender, RoutedEventArgs e) =>
            MoveTagDisplayFilterByOffset(GetFilterIdFromSender(sender), -1);

        private void MoveTagDisplayFilterDown_Click(object? sender, RoutedEventArgs e) =>
            MoveTagDisplayFilterByOffset(GetFilterIdFromSender(sender), 1);

        private void MoveTagDisplayFilterByOffset(string? filterId, int offset)
        {
            if (string.IsNullOrEmpty(filterId))
                return;

            _settings.EnsureInitialized();
            var fromIndex = GetTagDisplayFilterIndex(filterId);
            if (fromIndex < 0)
                return;

            var toIndex = fromIndex + offset;
            if (toIndex < 0 || toIndex >= _settings.TagDisplayFilters.Count)
                return;

            PreviewMoveTagDisplayFilter(filterId, toIndex);
            OnSettingChanged();

            if (IsGamepadFocusActive && _gamepadNavigation.ActiveZone == GamepadNavigationZone.Sidebar)
                SelectSidebarDisplayFilterRow(filterId);
        }

        private void SelectSidebarDisplayFilterRow(string filterId)
        {
            var controls = CollectSidebarFocusableControls();
            var index = controls.FindIndex(c =>
                c is Button b &&
                b.Classes.Contains("display-filter-row") &&
                b.Tag is string id &&
                string.Equals(id, filterId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                ApplySidebarGamepadSelection(index);
        }

        private void EditTagDisplayFilter_Click(object? sender, RoutedEventArgs e)
        {
            var filterId = GetFilterIdFromSender(sender);
            if (filterId == null)
                return;

            ShowDisplayFilterOverlay(filterId);
        }

        private void ShowDisplayFilterOverlay(string? filterId)
        {
            _editingDisplayFilterId = filterId;
            _settings.EnsureInitialized();

            if (filterId == null)
            {
                DisplayFilterOverlayTitle.Text = "Add Display Filter";
                DisplayFilterNameTextBox.Text = string.Empty;
                DisplayFilterTagsTextBox.Text = string.Empty;
                DisplayFilterExcludeTagsTextBox.Text = string.Empty;
                SetDisplayFilterMatchModeComboBox(TagFilterMatchMode.Any);
                SetDisplayFilterExcludeMatchModeComboBox(TagFilterMatchMode.Any);
            }
            else
            {
                var filter = _settings.TagDisplayFilters.FirstOrDefault(f => f.Id == filterId);
                if (filter == null)
                    return;

                DisplayFilterOverlayTitle.Text = "Edit Display Filter";
                DisplayFilterNameTextBox.Text = filter.Name;
                DisplayFilterTagsTextBox.Text = TagHelper.FormatTagsForDisplay(filter.Tags);
                DisplayFilterExcludeTagsTextBox.Text = TagHelper.FormatTagsForDisplay(filter.ExcludeTags);
                SetDisplayFilterMatchModeComboBox(filter.MatchMode);
                SetDisplayFilterExcludeMatchModeComboBox(filter.ExcludeMatchMode);
            }

            DisplayFilterOverlay.IsVisible = true;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.DisplayFilterOverlay;
            OnPropertyChanged(nameof(GamepadHintsVisible));
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsGamepadFocusActive)
                {
                    ClearDisplayFilterGamepadFocus();
                    DismissTextInputFocus();
                    return;
                }

                ApplyDisplayFilterGamepadSelection(0);
            }, DispatcherPriority.Loaded);
        }

        private bool IsDisplayFilterOverlayOpen => DisplayFilterOverlay?.IsVisible == true;

        private void SetDisplayFilterMatchModeComboBox(TagFilterMatchMode matchMode)
        {
            DisplayFilterMatchModeComboBox.SelectedIndex = matchMode == TagFilterMatchMode.All ? 1 : 0;
            UpdateDisplayFilterMatchModeHelpText();
        }

        private TagFilterMatchMode GetSelectedDisplayFilterMatchMode() =>
            DisplayFilterMatchModeComboBox.SelectedIndex == 1
                ? TagFilterMatchMode.All
                : TagFilterMatchMode.Any;

        private void UpdateDisplayFilterMatchModeHelpText()
        {
            DisplayFilterMatchModeHelpText.Text = GetSelectedDisplayFilterMatchMode() == TagFilterMatchMode.All
                ? "Show apps that have every include tag listed. Exclude rules are applied next."
                : "Show apps that have at least one include tag listed. Exclude rules are applied next.";
        }

        private void DisplayFilterMatchModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DisplayFilterMatchModeHelpText == null)
                return;

            UpdateDisplayFilterMatchModeHelpText();
        }

        private void SetDisplayFilterExcludeMatchModeComboBox(TagFilterMatchMode matchMode)
        {
            DisplayFilterExcludeMatchModeComboBox.SelectedIndex = matchMode == TagFilterMatchMode.All ? 1 : 0;
            UpdateDisplayFilterExcludeMatchModeHelpText();
        }

        private TagFilterMatchMode GetSelectedDisplayFilterExcludeMatchMode() =>
            DisplayFilterExcludeMatchModeComboBox.SelectedIndex == 1
                ? TagFilterMatchMode.All
                : TagFilterMatchMode.Any;

        private void UpdateDisplayFilterExcludeMatchModeHelpText()
        {
            DisplayFilterExcludeMatchModeHelpText.Text = GetSelectedDisplayFilterExcludeMatchMode() == TagFilterMatchMode.All
                ? "Hide apps that have every exclude tag listed."
                : "Hide apps that have at least one of these tags.";
        }

        private void DisplayFilterExcludeMatchModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DisplayFilterExcludeMatchModeHelpText == null)
                return;

            UpdateDisplayFilterExcludeMatchModeHelpText();
        }

        private void CancelDisplayFilterEdit_Click(object? sender, RoutedEventArgs e)
        {
            CloseDisplayFilterOverlay();
        }

        private void CloseDisplayFilterOverlay()
        {
            ClearDisplayFilterGamepadFocus();
            _displayFilterGamepadFocusIndex = -1;

            _editingDisplayFilterId = null;
            if (DisplayFilterOverlay != null)
                DisplayFilterOverlay.IsVisible = false;

            if (DisplayFilterNameTextBox != null)
                DisplayFilterNameTextBox.Text = string.Empty;
            if (DisplayFilterTagsTextBox != null)
                DisplayFilterTagsTextBox.Text = string.Empty;
            if (DisplayFilterExcludeTagsTextBox != null)
                DisplayFilterExcludeTagsTextBox.Text = string.Empty;

            SetDisplayFilterMatchModeComboBox(TagFilterMatchMode.Any);
            SetDisplayFilterExcludeMatchModeComboBox(TagFilterMatchMode.Any);

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.DisplayFilterOverlay)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Sidebar;
                ApplySidebarGamepadSelection(
                    _gamepadNavigation.SidebarSelectedIndex < 0 ? 0 : _gamepadNavigation.SidebarSelectedIndex);
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        private void SaveDisplayFilter_Click(object? sender, RoutedEventArgs e)
        {
            var name = DisplayFilterNameTextBox.Text?.Trim();
            var tags = TagHelper.ParseCommaSeparatedTags(DisplayFilterTagsTextBox.Text);
            var excludeTags = TagHelper.ParseCommaSeparatedTags(DisplayFilterExcludeTagsTextBox.Text);

            if (string.IsNullOrWhiteSpace(name))
            {
                _ = ShowMessageBoxAsync("Please enter a filter name.", "Validation Error");
                return;
            }

            if (tags.Count == 0 && excludeTags.Count == 0)
            {
                _ = ShowMessageBoxAsync("Please enter at least one include or exclude tag.", "Validation Error");
                return;
            }

            _settings.EnsureInitialized();

            var duplicate = _settings.TagDisplayFilters.Any(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(f.Id, _editingDisplayFilterId, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                _ = ShowMessageBoxAsync("A filter with this name already exists.", "Duplicate Filter");
                return;
            }

            var isEdit = !string.IsNullOrWhiteSpace(_editingDisplayFilterId);
            var matchMode = GetSelectedDisplayFilterMatchMode();
            var excludeMatchMode = GetSelectedDisplayFilterExcludeMatchMode();
            if (isEdit)
            {
                var filter = _settings.TagDisplayFilters.FirstOrDefault(f => f.Id == _editingDisplayFilterId);
                if (filter == null)
                    return;

                filter.Name = name;
                filter.Tags = tags;
                filter.MatchMode = matchMode;
                filter.ExcludeTags = excludeTags;
                filter.ExcludeMatchMode = excludeMatchMode;
            }
            else
            {
                _settings.TagDisplayFilters.Add(new TagDisplayFilter
                {
                    Name = name,
                    Tags = tags,
                    MatchMode = matchMode,
                    ExcludeTags = excludeTags,
                    ExcludeMatchMode = excludeMatchMode,
                });
            }

            CancelDisplayFilterEdit_Click(null, new RoutedEventArgs());
            OnSettingChanged();
            RefreshTagDisplayFiltersUI();
            RefreshSidebarFilterSelection();

            if (isEdit)
            {
                _gameManager.ApplyTagDisplayFilter(_settings);
                ApplySorting();
            }
        }

        private void DeleteTagDisplayFilter_Click(object? sender, RoutedEventArgs e)
        {
            var filterId = GetFilterIdFromSender(sender);
            if (filterId == null)
                return;

            var filter = _settings.TagDisplayFilters.FirstOrDefault(f => f.Id == filterId);
            if (filter == null)
                return;

            _settings.TagDisplayFilters.Remove(filter);
            if (string.Equals(_settings.ActiveTagDisplayFilterId, filterId, StringComparison.OrdinalIgnoreCase))
                _settings.ActiveTagDisplayFilterId = null;

            OnSettingChanged();
            RefreshTagDisplayFiltersUI();
            RefreshSidebarFilterSelection();
            _gameManager.ApplyTagDisplayFilter(_settings);
            ApplySorting();
        }

        private async Task<string?> PromptForTextAsync(string title, string initialValue)
        {
            var inputBox = new TextBox
            {
                Text = initialValue,
                Width = 320,
                Foreground = Resources["ThemeText"] as IBrush,
                Background = Resources["ThemeBase"] as IBrush,
                BorderBrush = Resources["ThemeBorder"] as IBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
            };

            var dialog = new Window
            {
                Title = title,
                Width = 380,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Resources["ThemeDarker"] as IBrush,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        inputBox,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button { Classes = { "options" }, Content = "Cancel", Tag = false },
                                new Button { Classes = { "options" }, Content = "OK", Tag = true },
                            }
                        }
                    }
                }
            };

            string? result = null;
            var buttonsPanel = (StackPanel)((StackPanel)dialog.Content!).Children[1];
            foreach (var child in buttonsPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.Click += (_, _) =>
                    {
                        if (btn.Tag is true)
                            result = inputBox.Text;
                        dialog.Close();
                    };
                }
            }

            GamepadModalDialogNavigation.Attach(dialog);
            dialog.Opened += (_, _) => GamepadControlActivation.ActivateTextBox(inputBox);

            await dialog.ShowDialog(this);
            return result;
        }

        private static void OnTextBoxGotFocusForSteamOsk(object? sender, GotFocusEventArgs e)
        {
            if (e.Source is not TextBox textBox || !SteamOnScreenKeyboard.ShouldOffer())
                return;

            // Pointer focus keeps click position / selection for copy-paste.
            // Gamepad/tab: caret at end so OSK typing appends instead of inserting at index 0.
            if (e.NavigationMethod != NavigationMethod.Pointer)
                GamepadControlActivation.MoveCaretToEnd(textBox);
            SteamOnScreenKeyboard.TryOpen();
        }

        private void SortByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;

            if (SortByComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string sortMode)
            {
                _currentSortBy = sortMode;
                _settings.SortBy = sortMode;
                OnSettingChanged();
                ApplySorting();
            }
        }

        private void CatalogReviewSortByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0)
                return;

            if (CatalogReviewSortByComboBox?.SelectedItem is not ComboBoxItem item ||
                item.Tag is not string sortMode)
                return;

            _currentCatalogReviewSortBy = sortMode;
            _catalogSyncViewModel.SortBy = sortMode;
            _catalogSyncViewModel.IgnoreArticlesWhenSorting = _settings.IgnoreArticlesWhenSorting;
            _settings.CatalogReviewSortBy = sortMode;
            OnSettingChanged();
            ApplyCatalogSyncFilter();
        }

        private void ApplyCatalogReviewSortSelection(string sortMode)
        {
            _currentCatalogReviewSortBy = sortMode;
            _catalogSyncViewModel.SortBy = sortMode;
            _catalogSyncViewModel.IgnoreArticlesWhenSorting = _settings.IgnoreArticlesWhenSorting;

            if (CatalogReviewSortByComboBox == null)
                return;

            foreach (var entry in CatalogReviewSortByComboBox.Items)
            {
                if (entry is ComboBoxItem item && item.Tag as string == sortMode)
                {
                    CatalogReviewSortByComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void ApplySorting()
        {
            if (_gameManager?.Games == null || _gameManager.Games.Count == 0)
            {
                Debug.WriteLine("ApplySorting: No apps to sort");
                return;
            }

            Debug.WriteLine($"ApplySorting: Sorting {_gameManager.Games.Count} apps by {_currentSortBy}");
            _gameGridViewModel.ApplySort(
                _gameManager.Games,
                _currentSortBy,
                _gameManager.GamesFolder,
                _settings.IgnoreArticlesWhenSorting);
            Debug.WriteLine("ApplySorting: Completed sorting");
        }

        private DateTime GetLastPlayedTime(GameInfo game) =>
            GameGridViewModel.GetLastPlayedTime(game, _gameManager.GamesFolder);

        private async void HideGame_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                var wasHidden = _gameManager.IsManuallyHidden(game);
                _gameManager.ToggleUserHide(game);
                await _gameManager.LoadGamesAsync();
                ApplySorting();

                if (!wasHidden)
                {
                    await ShowMessageBoxAsync(
                        "App hidden from the library.\n\nYou can find it again under Show → Hidden, then choose Customize → Unhide App.",
                        "App Hidden");
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to update app visibility: {ex.Message}", "Error");
            }
        }

        private async Task<bool> ShowMessageBoxAsync(
            string message,
            string title,
            bool isQuestion = false,
            bool preferCancelDefault = false)
        {
            if (!isQuestion)
            {
                await ShowMessageBoxAsync(message, title);
                return true;
            }

            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var result = false;
                    var messageBox = CreateScrollableMessageBoxWindow(
                        message,
                        title,
                        isQuestion: true,
                        onQuestionResult: value => result = value,
                        preferCancelDefault: preferCancelDefault);

                    await messageBox.ShowDialog(desktop.MainWindow);
                    if (messageBox.Tag is bool tagResult)
                        return tagResult;
                    return result;
                }
                return false;
            });
        }
        private void EnableGamepadCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.EnableGamepadInput = true;
                _inputService?.SetGamepadEnabled(true);
                UpdateGamepadChromeClass();
                SelectInitialGamepadItemForCurrentView();
                NotifyGamepadUiChanged();
                UpdateGamepadHintsBar();
                OnSettingChanged();
            }
        }

        private void EnableGamepadCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.EnableGamepadInput = false;
                CancelGamepadRebindListen();
                _inputService?.SetGamepadEnabled(false);
                GamepadFocusChrome.SetKeyboardNavigationActive(false);
                UpdateGamepadChromeClass();
                ClearGamepadFocus();
                NotifyGamepadUiChanged();
                UpdateGamepadHintsBar();
                OnSettingChanged();
            }
        }

        private void RefreshConnectedGamepads_Click(object? sender, RoutedEventArgs e)
        {
            RefreshConnectedGamepadsList();
        }

        private void ResetGamepadBindings_Click(object? sender, RoutedEventArgs e)
        {
            CancelGamepadRebindListen();
            CancelKeyboardRebindListen();
            _settings.GamepadBindings = GamepadBindingDefaults.Create();
            _settings.KeyboardBindings = KeyboardBindingDefaults.Create();
            _inputService?.ApplyBindings(_settings.GamepadBindings);
            RefreshGamepadBindingsPanel();
            UpdateGamepadHintsBar();
            OnSettingChanged();
        }

        private void RefreshConnectedGamepadsList()
        {
            if (ConnectedGamepadsText == null)
                return;

            var pads = _inputService?.GetConnectedGamepads() ?? Array.Empty<ConnectedGamepadInfo>();
            if (pads.Count == 0)
            {
                ConnectedGamepadsText.Text = "No controllers detected";
                return;
            }

            ConnectedGamepadsText.Text = string.Join(
                Environment.NewLine,
                pads.Select(p => $"{p.Index + 1}. {p.Name}"));
        }

        private void UpdateGamepadHintsBar()
        {
            if (GamepadHintsBar == null || _settings == null)
                return;

            _settings.EnsureInitialized();
            var padHints = GamepadBindingLabels.FormatHints(_settings.GamepadBindings);
            var keyHints = KeyboardBindingLabels.FormatHints(_settings.KeyboardBindings);
            GamepadHintsBar.Text = _settings.EnableGamepadInput
                ? $"{padHints}  |  {keyHints}"
                : keyHints;
        }

        private static string GetGamepadActionDisplayName(GamepadAction action) => action switch
        {
            GamepadAction.Confirm => "Confirm / Select",
            GamepadAction.Cancel => "Cancel / Back",
            GamepadAction.Options => "Options",
            GamepadAction.NavUp => "Navigate Up",
            GamepadAction.NavDown => "Navigate Down",
            GamepadAction.NavLeft => "Navigate Left",
            GamepadAction.NavRight => "Navigate Right",
            _ => action.ToString(),
        };

        private void RefreshGamepadBindingsPanel()
        {
            if (GamepadBindingsPanel == null || _settings == null)
                return;

            _settings.EnsureInitialized();
            GamepadBindingsPanel.Children.Clear();

            var themeText = (IBrush?)Application.Current?.FindResource("ThemeText") ?? Brushes.White;
            var themeSecondary = (IBrush?)Application.Current?.FindResource("ThemeTextSecondary") ?? Brushes.Gray;
            var anyListening = _rebindListeningAction.HasValue || _keyboardRebindListeningAction.HasValue;

            foreach (var action in Enum.GetValues<GamepadAction>())
            {
                var block = new StackPanel { Spacing = 4 };

                block.Children.Add(new TextBlock
                {
                    Text = GetGamepadActionDisplayName(action),
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = themeText,
                });

                block.Children.Add(CreateBindingDeviceRow(
                    action,
                    deviceLabel: "Gamepad",
                    bindingLabel: _rebindListeningAction == action
                        ? "Press a control…"
                        : GamepadBindingLabels.FormatActionBindings(_settings.GamepadBindings, action),
                    rebindContent: _rebindListeningAction == action ? "Listening…" : "Rebind",
                    listeningThis: _rebindListeningAction == action,
                    anyListening: anyListening,
                    themeSecondary,
                    GamepadRebindButton_Click));

                block.Children.Add(CreateBindingDeviceRow(
                    action,
                    deviceLabel: "Keyboard",
                    bindingLabel: _keyboardRebindListeningAction == action
                        ? "Press a key…"
                        : KeyboardBindingLabels.FormatActionBindings(_settings.KeyboardBindings, action),
                    rebindContent: _keyboardRebindListeningAction == action ? "Listening…" : "Rebind",
                    listeningThis: _keyboardRebindListeningAction == action,
                    anyListening: anyListening,
                    themeSecondary,
                    KeyboardRebindButton_Click));

                GamepadBindingsPanel.Children.Add(block);
            }

            if (GamepadRebindStatusText != null)
            {
                if (_rebindListeningAction.HasValue)
                {
                    GamepadRebindStatusText.IsVisible = true;
                    GamepadRebindStatusText.Text =
                        $"Listening for {GetGamepadActionDisplayName(_rebindListeningAction.Value)} (gamepad). Press a control, or Esc to cancel.";
                }
                else if (_keyboardRebindListeningAction.HasValue)
                {
                    GamepadRebindStatusText.IsVisible = true;
                    GamepadRebindStatusText.Text =
                        $"Listening for {GetGamepadActionDisplayName(_keyboardRebindListeningAction.Value)} (keyboard). Press a key, or Esc to cancel.";
                }
                else
                {
                    GamepadRebindStatusText.IsVisible = false;
                    GamepadRebindStatusText.Text = string.Empty;
                }
            }
        }

        private static Grid CreateBindingDeviceRow(
            GamepadAction action,
            string deviceLabel,
            string bindingLabel,
            string rebindContent,
            bool listeningThis,
            bool anyListening,
            IBrush themeSecondary,
            EventHandler<RoutedEventArgs> onRebindClick)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };

            var labels = new StackPanel { Spacing = 1 };
            labels.Children.Add(new TextBlock
            {
                Text = deviceLabel,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = themeSecondary,
            });
            labels.Children.Add(new TextBlock
            {
                Text = bindingLabel,
                FontSize = 11,
                Foreground = themeSecondary,
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(labels, 0);
            row.Children.Add(labels);

            var rebindButton = new Button
            {
                Content = rebindContent,
                Classes = { "options" },
                FontSize = 12,
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0),
                Tag = action,
                IsEnabled = !anyListening || listeningThis,
            };
            rebindButton.Click += onRebindClick;
            Grid.SetColumn(rebindButton, 1);
            row.Children.Add(rebindButton);

            return row;
        }

        private void GamepadRebindButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not GamepadAction action)
                return;

            if (_rebindListeningAction == action)
            {
                CancelGamepadRebindListen();
                return;
            }

            StartGamepadRebindListen(action);
        }

        private void KeyboardRebindButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not GamepadAction action)
                return;

            if (_keyboardRebindListeningAction == action)
            {
                CancelKeyboardRebindListen();
                return;
            }

            StartKeyboardRebindListen(action);
        }

        private void StartGamepadRebindListen(GamepadAction action)
        {
            if (_inputService == null || _settings == null)
                return;

            if (!_settings.EnableGamepadInput)
            {
                _ = ShowMessageBoxAsync(
                    "Enable Gamepad Input before rebinding gamepad controls.",
                    "Controls");
                return;
            }

            CancelKeyboardRebindListen();
            CancelGamepadRebindListen();

            _rebindListeningAction = action;
            _inputService.SetCaptureMode(true);
            // Ensure polling is on while capturing even if somehow disabled mid-session.
            _inputService.SetGamepadEnabled(true);

            _rawInputHandler = binding =>
            {
                Dispatcher.UIThread.Post(() => ApplyCapturedGamepadBinding(binding));
            };
            _inputService.OnRawInput += _rawInputHandler;

            RefreshGamepadBindingsPanel();
        }

        private void StartKeyboardRebindListen(GamepadAction action)
        {
            if (_settings == null)
                return;

            CancelGamepadRebindListen();
            CancelKeyboardRebindListen();

            _keyboardRebindListeningAction = action;
            RefreshGamepadBindingsPanel();
        }

        private void ApplyCapturedGamepadBinding(GamepadBinding binding)
        {
            if (_settings == null || !_rebindListeningAction.HasValue)
                return;

            var action = _rebindListeningAction.Value;
            CancelGamepadRebindListen();

            _settings.EnsureInitialized();
            GamepadBindingDefaults.AssignExclusive(_settings.GamepadBindings, action, binding);
            _inputService?.ApplyBindings(_settings.GamepadBindings);
            RefreshGamepadBindingsPanel();
            UpdateGamepadHintsBar();
            OnSettingChanged();
        }

        private void ApplyCapturedKeyboardBinding(KeyboardBinding binding)
        {
            if (_settings == null || !_keyboardRebindListeningAction.HasValue)
                return;

            var action = _keyboardRebindListeningAction.Value;
            CancelKeyboardRebindListen();

            _settings.EnsureInitialized();
            KeyboardBindingDefaults.AssignExclusive(_settings.KeyboardBindings, action, binding);
            RefreshGamepadBindingsPanel();
            UpdateGamepadHintsBar();
            OnSettingChanged();
        }

        private void CancelGamepadRebindListen()
        {
            if (_inputService != null)
            {
                if (_rawInputHandler != null)
                {
                    _inputService.OnRawInput -= _rawInputHandler;
                    _rawInputHandler = null;
                }

                _inputService.SetCaptureMode(false);
                _inputService.SetGamepadEnabled(_settings.EnableGamepadInput);
            }

            var wasListening = _rebindListeningAction.HasValue;
            _rebindListeningAction = null;
            if (wasListening)
                RefreshGamepadBindingsPanel();
        }

        private void CancelKeyboardRebindListen()
        {
            var wasListening = _keyboardRebindListeningAction.HasValue;
            _keyboardRebindListeningAction = null;
            if (wasListening)
                RefreshGamepadBindingsPanel();
        }

        private void CloseAfterLaunchCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.CloseAfterLaunch = true;
                OnPropertyChanged(nameof(CloseAfterLaunch));
                OnSettingChanged();
            }
        }

        private void CloseAfterLaunchCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.CloseAfterLaunch = false;
                OnPropertyChanged(nameof(CloseAfterLaunch));
                OnSettingChanged();
            }
        }

        private void CloseToTrayCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.CloseToTray = true;
            OnSettingChanged();
            ApplyTrayAndBackgroundUpdateSettings();
        }

        private void CloseToTrayCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.CloseToTray = false;
            OnSettingChanged();
            ApplyTrayAndBackgroundUpdateSettings();
        }

        private void AutoUpdateNewlyAddedAppsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.AutoUpdateNewlyAddedApps = true;
            OnSettingChanged();
        }

        private void AutoUpdateNewlyAddedAppsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.AutoUpdateNewlyAddedApps = false;
            OnSettingChanged();
        }

        private void BackgroundUpdateCheckCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.BackgroundUpdateCheckEnabled = true;
            OnSettingChanged();
            ApplyTrayAndBackgroundUpdateSettings();
        }

        private void BackgroundUpdateCheckCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.BackgroundUpdateCheckEnabled = false;
            OnSettingChanged();
            ApplyTrayAndBackgroundUpdateSettings();
        }

        private void PromptCatalogUpdatesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.PromptCatalogUpdates = true;
            OnSettingChanged();
        }

        private void PromptCatalogUpdatesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.PromptCatalogUpdates = false;
            OnSettingChanged();
        }

        private void PromptAppUpdateReviewsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.PromptAppUpdateReviews = true;
            OnSettingChanged();
        }

        private void PromptAppUpdateReviewsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.PromptAppUpdateReviews = false;
            OnSettingChanged();
        }

        private async void ShowLibraryAppUpdateBadgesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.ShowLibraryAppUpdateBadges = true;
            OnSettingChanged();
            ApplyLibraryDisplaySettingsToGames();
            await ApplyLibraryCatalogPendingBadgesAsync();
        }

        private void ShowLibraryAppUpdateBadgesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.ShowLibraryAppUpdateBadges = false;
            OnSettingChanged();
            ApplyLibraryDisplaySettingsToGames();
        }

        private void AllowPrereleaseLauncherUpdatesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.AllowPrereleaseLauncherUpdates = true;
            OnSettingChanged();
        }

        private void AllowPrereleaseLauncherUpdatesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            _settings.AllowPrereleaseLauncherUpdates = false;
            OnSettingChanged();
        }

        private void BackgroundUpdateIntervalComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null)
                return;

            if (BackgroundUpdateIntervalComboBox?.SelectedItem is not ComboBoxItem item ||
                item.Tag is not string tag ||
                !int.TryParse(tag, out var minutes))
            {
                return;
            }

            _settings.BackgroundUpdateCheckIntervalMinutes = BackgroundUpdateCheckIntervals.Normalize(minutes);
            OnSettingChanged();
            ApplyTrayAndBackgroundUpdateSettings();
        }

        private void IgnoreArticlesWhenSortingCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SetIgnoreArticlesWhenSorting(true);
        }

        private void IgnoreArticlesWhenSortingCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SetIgnoreArticlesWhenSorting(false);
        }

        private void SetIgnoreArticlesWhenSorting(bool enabled)
        {
            if (_settings == null)
                return;

            _settings.IgnoreArticlesWhenSorting = enabled;
            OnSettingChanged();
            ApplySorting();

            if (_mainViewMode == MainViewMode.AppCatalog &&
                _appCatalogSubView == AppCatalogSubView.Review)
            {
                _catalogSyncViewModel.IgnoreArticlesWhenSorting = enabled;
                ApplyCatalogSyncFilter();
            }
        }

        private void LibraryNameStyleComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_settings == null || LibraryNameStyleComboBox?.SelectedItem is not ComboBoxItem item)
                return;

            var style = (item.Tag as string) switch
            {
                "NameOnly" => LibraryNameStyle.NameOnly,
                "ProjectOnly" => LibraryNameStyle.ProjectOnly,
                "NameAndProjectInTitle" => LibraryNameStyle.NameAndProjectInTitle,
                _ => LibraryNameStyle.NameAndProject,
            };

            if (_settings.LibraryNameStyle == style)
                return;

            _settings.LibraryNameStyle = style;
            OnSettingChanged();
            ApplyLibraryDisplaySettingsToGames();
            ApplySorting();
        }

        private void LibraryTagDisplayModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_settings == null || LibraryTagDisplayModeComboBox?.SelectedItem is not ComboBoxItem item)
                return;

            var mode = (item.Tag as string) switch
            {
                "All" => LibraryTagDisplayMode.All,
                "Hidden" => LibraryTagDisplayMode.Hidden,
                _ => LibraryTagDisplayMode.Featured,
            };

            if (_settings.LibraryTagDisplayMode == mode)
                return;

            _settings.LibraryTagDisplayMode = mode;
            OnSettingChanged();
            ApplyLibraryDisplaySettingsToGames();
        }

        private void LibraryCardTagMaxLinesComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressSettingsUiEvents || _settings == null ||
                LibraryCardTagMaxLinesComboBox?.SelectedItem is not ComboBoxItem item ||
                item.Tag is not string tag ||
                !int.TryParse(tag, out var maxLines))
            {
                return;
            }

            maxLines = TagChipHelper.NormalizeLibraryCardTagMaxLines(maxLines);
            if (_settings.LibraryCardTagMaxLines == maxLines)
                return;

            _settings.LibraryCardTagMaxLines = maxLines;
            OnSettingChanged();
            ApplyLibraryDisplaySettingsToGames();
        }

        private void SelectLibraryCardTagMaxLinesComboBox(int maxLines)
        {
            if (LibraryCardTagMaxLinesComboBox == null)
                return;

            var tag = TagChipHelper.NormalizeLibraryCardTagMaxLines(maxLines).ToString();
            foreach (var entry in LibraryCardTagMaxLinesComboBox.Items)
            {
                if (entry is ComboBoxItem item && item.Tag as string == tag)
                {
                    LibraryCardTagMaxLinesComboBox.SelectedItem = item;
                    return;
                }
            }

            foreach (var entry in LibraryCardTagMaxLinesComboBox.Items)
            {
                if (entry is ComboBoxItem item &&
                    item.Tag as string == TagChipHelper.DefaultLibraryCardTagMaxLines.ToString())
                {
                    LibraryCardTagMaxLinesComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SelectLibraryNameStyleComboBox(LibraryNameStyle style)
        {
            if (LibraryNameStyleComboBox == null)
                return;

            var tag = style switch
            {
                LibraryNameStyle.NameOnly => "NameOnly",
                LibraryNameStyle.ProjectOnly => "ProjectOnly",
                LibraryNameStyle.NameAndProjectInTitle => "NameAndProjectInTitle",
                _ => "NameAndProject",
            };

            foreach (var entry in LibraryNameStyleComboBox.Items)
            {
                if (entry is ComboBoxItem item && item.Tag as string == tag)
                {
                    LibraryNameStyleComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SelectLibraryTagDisplayModeComboBox(LibraryTagDisplayMode mode)
        {
            if (LibraryTagDisplayModeComboBox == null)
                return;

            var tag = mode switch
            {
                LibraryTagDisplayMode.All => "All",
                LibraryTagDisplayMode.Hidden => "Hidden",
                _ => "Featured",
            };

            foreach (var entry in LibraryTagDisplayModeComboBox.Items)
            {
                if (entry is ComboBoxItem item && item.Tag as string == tag)
                {
                    LibraryTagDisplayModeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void ApplyLibraryDisplaySettingsToGames()
        {
            if (_settings == null || _gameManager?.Games == null)
                return;

            foreach (var game in _gameManager.Games)
            {
                game.LibraryNameStyle = _settings.LibraryNameStyle;
                game.ShowLibraryUpdateBadges = _settings.ShowLibraryAppUpdateBadges;
                game.LibraryCardTagMaxLines = _settings.LibraryCardTagMaxLines;
            }

            AppCatalogService.RefreshLibraryCardTags(_gameManager.Games, _settings);
        }

        private void GitHubTokenTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.GitHubApiToken = textBox.Text ?? string.Empty;
                OnSettingChanged();
            }
        }

        private void GitLabTokenTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.GitLabApiToken = textBox.Text ?? string.Empty;
                OnSettingChanged();
            }
        }

        private void BackgroundPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.BackgroundImagePath = textBox.Text ?? string.Empty;
                OnSettingChanged();
            }
        }

        private void LinuxWindowsLaunchCommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.LinuxWindowsLaunchCommand = textBox.Text?.Trim() ?? string.Empty;
                OnSettingChanged();
            }
        }

        private System.Threading.CancellationTokenSource? _gamePathUpdateCts;

        private async void GamePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings == null || sender is not TextBox textBox)
                return;

            // Cancel previous update
            var oldCts = _gamePathUpdateCts;
            _gamePathUpdateCts = new System.Threading.CancellationTokenSource();

            // Dispose old token after a delay
            if (oldCts != null)
            {
                var tokenToDispose = oldCts;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    tokenToDispose.Dispose();
                });
            }

            try
            {
                await Task.Delay(500, _gamePathUpdateCts.Token);
                await UpdateGamePath(textBox.Text?.Trim() ?? string.Empty, textBox);
            }
            catch (OperationCanceledException)
            {
                // User is still typing
            }
        }

        private async Task UpdateGamePath(string newPath, TextBox textBox)
        {
            if (_settings.AppsPath == newPath)
                return;

            _settings.AppsPath = newPath;
            OnSettingChanged();

            if (_gameManager == null)
                return;

            if (!string.IsNullOrEmpty(newPath) && !Directory.Exists(newPath))
            {
                var result = await ShowMessageBoxAsync(
                    $"The directory '{newPath}' does not exist. Create it?",
                    "Directory Not Found",
                    true);

                if (!result)
                {
                    textBox.Text = _settings.AppsPath;
                    return;
                }
            }

            try
            {
                await _gameManager.UpdateGamesFolderAsync(_settings.AppsPath);
                await _gameManager.LoadGamesAsync();
                ApplySorting();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to update apps path: {ex.Message}", "Error");
                textBox.Text = _settings.AppsPath;
            }
        }

        private void CreateGitHubToken_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string githubTokenUrl = "https://github.com/settings/tokens/new?description=Github-Launcher+Token+for+increased+API+rate+limits";
                OpenUrl(githubTokenUrl);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open GitHub token page: {ex.Message}", "Error");
            }
        }

        private void ClearGitHubToken_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.GitHubApiToken = string.Empty;
                if (GitHubTokenTextBox != null)
                    GitHubTokenTextBox.Text = string.Empty;
                OnSettingChanged();
            }
        }

        private void CreateGitLabToken_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://gitlab.com/-/user_settings/personal_access_tokens";
                OpenUrl(url);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open GitLab token page: {ex.Message}", "Error");
            }
        }

        private void ClearGitLabToken_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.GitLabApiToken = string.Empty;
                if (GitLabTokenTextBox != null)
                    GitLabTokenTextBox.Text = string.Empty;
                OnSettingChanged();
            }
        }

        private async void ClearGamePath_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null)
                return;

            try
            {
                _settings.AppsPath = string.Empty;
                if (GamePathTextBox != null)
                    GamePathTextBox.Text = string.Empty;
                OnSettingChanged();

                if (_gameManager != null)
                {
                    await _gameManager.UpdateGamesFolderAsync(string.Empty);
                    await _gameManager.LoadGamesAsync();
                    ApplySorting();
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to clear games path: {ex.Message}", "Error");
            }
        }

        private async void BrowseGamePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Apps Folder",
                    AllowMultiple = false
                });

                if (folders?.Count > 0)
                {
                    var selectedPath = folders[0].Path.LocalPath;

                    if (!Directory.Exists(selectedPath))
                    {
                        _ = ShowMessageBoxAsync("Selected folder does not exist.", "Invalid Selection");
                        return;
                    }

                    if (_settings != null)
                    {
                        _settings.AppsPath = selectedPath;

                        if (GamePathTextBox != null)
                            GamePathTextBox.Text = selectedPath;

                        OnSettingChanged();

                        if (_gameManager != null)
                        {
                            await _gameManager.UpdateGamesFolderAsync(selectedPath);
                            await _gameManager.LoadGamesAsync();
                            ApplySorting();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to select folder: {ex.Message}", "Error");
            }
        }

        private async void ClearIconCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _gameManager.ClearIconCacheAsync();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to clear icon cache: {ex.Message}", "Error");
            }
        }

        private async void SelectBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Background Image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
            new FilePickerFileType("Image Files")
            {
                Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" }
            }
        }
            });

            if (file.Count > 0)
            {
                var selectedFile = file[0];
                BackgroundImagePath = selectedFile.Path.LocalPath;
                _settings.BackgroundImagePath = BackgroundImagePath;

                AppSettings.Save(_settings);
            }
        }

        private void ClearBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            BackgroundImagePath = string.Empty;
            _settings.BackgroundImagePath = string.Empty;
            AppSettings.Save(_settings);
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider)
            {
                BackgroundOpacity = (float)slider.Value;
                _settings.BackgroundOpacity = BackgroundOpacity;
                AppSettings.Save(_settings);
            }
        }

        private async void SelectLauncherMusic_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Launcher Music",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
            new FilePickerFileType("Audio Files")
            {
                Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a", "*.wma", "*.aac" }
            }
        }
            });

            if (file.Count > 0)
            {
                var selectedFile = file[0];
                LauncherMusicPath = selectedFile.Path.LocalPath;
                _settings.LauncherMusicPath = LauncherMusicPath;
                AppSettings.Save(_settings);

                PlayLauncherMusic(LauncherMusicPath);
            }
        }

        private void ClearLauncherMusic_Click(object sender, RoutedEventArgs e)
        {
            StopLauncherMusic();
            LauncherMusicPath = string.Empty;
            _settings.LauncherMusicPath = string.Empty;
            AppSettings.Save(_settings);
        }

        private string? _editingGameName;
        private string? _editingGameRepository;
        private string? _editingGameIdentityKey;
        private string? _editingFolderName;
        private GameInfo? _editingGame;

        private Task<List<GameInfo>> LoadGamesFromJsonAsync() =>
            _gameManager.CatalogService.LoadLocalAppsAsync();

        private async Task SaveGamesToJsonAsync(List<GameInfo> appsToSave)
        {
            foreach (var app in appsToSave)
                app.GameManager ??= _gameManager;

            await _gameManager.CatalogService.SaveLocalAppsAsync(appsToSave);
        }

        private async Task SaveGamesToJsonAsync(Dictionary<string, JsonElement> gamesData)
        {
            var imported = _gameManager.CatalogService.ParseAppsFromDictionary(gamesData);
            foreach (var app in imported)
                app.GameManager = _gameManager;

            await _gameManager.CatalogService.SaveLocalAppsAsync(imported);
        }

        private async Task PersistGameVersionPreferencesAsync(GameInfo game, string? preferredVersion, string? skippedUpdateVersion)
        {
            game.SetVersionPreferences(preferredVersion, skippedUpdateVersion);

            var allGames = await LoadGamesFromJsonAsync();
            var matchingGame = allGames.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.Repository) &&
                g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

            if (matchingGame == null)
                return;

            matchingGame.PreferredVersion = game.PreferredVersion;
            matchingGame.SkippedUpdateVersion = game.SkippedUpdateVersion;
            matchingGame.AutoUpdate = game.AutoUpdate;

            await SaveGamesToJsonAsync(allGames);
        }

        private async Task PersistAutoUpdatePreferenceAsync(GameInfo game)
        {
            var allGames = await LoadGamesFromJsonAsync();
            var matchingGame = allGames.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.Repository) &&
                g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

            if (matchingGame == null)
                return;

            matchingGame.AutoUpdate = game.AutoUpdate;
            await SaveGamesToJsonAsync(allGames);
        }

        private async Task PersistGameInstallLocationAsync(GameInfo game)
        {
            var allGames = await LoadGamesFromJsonAsync();
            var matchingGame = allGames.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.Repository) &&
                g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

            if (matchingGame == null)
                return;

            matchingGame.FolderName = game.FolderName;
            matchingGame.InstallPath = game.InstallPath;

            await SaveGamesToJsonAsync(allGames);
        }

        private void AddNewEntryButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowEntryFormOverlay(forCreate: true);
        }

        private void ShowEntryFormOverlay(bool forCreate, GameInfo? gameToEdit = null)
        {
            SettingsPanel.IsVisible = false;
            ChangelogPanel.IsVisible = false;

            if (forCreate)
                ClearForm();
            else if (gameToEdit != null)
                OpenEditForm(gameToEdit);

            _isEntryFormOpen = true;
            EntryFormOverlay.IsVisible = true;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.EntryFormOverlay;
            OnPropertyChanged(nameof(GamepadHintsVisible));
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsGamepadFocusActive)
                {
                    ClearEntryFormGamepadFocus();
                    DismissTextInputFocus();
                    return;
                }

                ApplyEntryFormGamepadSelection(0);
                // Steam Deck: open OSK on Name when the form appears; D-pad to other
                // fields still uses highlight-only until Confirm (A).
                if (SteamOnScreenKeyboard.ShouldOffer())
                    ActivateEntryFormGamepadSelection();
            }, DispatcherPriority.Loaded);
        }

        private void CloseEntryFormOverlay()
        {
            ClearEntryFormGamepadFocus();
            _entryFormGamepadFocusIndex = -1;

            _isEntryFormOpen = false;
            EntryFormOverlay.IsVisible = false;
            ClearForm();

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.EntryFormOverlay)
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                SelectInitialGamepadItemForCurrentView();
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        private void OpenEditForm(GameInfo game)
        {
            ClearForm();

            FormTitleText.Text = "Edit App Entry";
            NewGameNameTextBox.Text = game.Name ?? "";
            SetRepositorySourceSelection(game.EffectiveRepositorySource);
            NewGameRepoTextBox.Text = game.Repository ?? "";
            NewGameRepoTextBox.IsReadOnly = false;
            NewGameFolderTextBox.Text = game.FolderName ?? "";
            NewGameIconTextBox.Text = game.GameIconUrl ?? "";
            if (NewGameTagsTextBox != null)
                NewGameTagsTextBox.Text = TagHelper.FormatTagsForDisplay(game.Tags);
            if (NewGameProjectTextBox != null)
                NewGameProjectTextBox.Text = game.Project ?? "";
            if (NewGameCustomDisplayNameTextBox != null)
                NewGameCustomDisplayNameTextBox.Text = game.CustomDisplayName ?? "";
            if (NewGameFilesToAddTextBox != null)
                NewGameFilesToAddTextBox.Text = AppFilesToAddService.FormatForDisplay(game.FilesToAdd);
            if (NewGameModsPathTextBox != null)
                NewGameModsPathTextBox.Text = GameModsConfig.NormalizePath(game.ModsPath);
            if (NewGameModsFolderPerModCheckBox != null)
                NewGameModsFolderPerModCheckBox.IsChecked = GameModsConfig.IsFolderPerMod(game.ModsLayout);
            if (NewGameModsSourcesTextBox != null)
                NewGameModsSourcesTextBox.Text = GameModsFormHelper.FormatSourcesForEditor(game.ModsSources);
            CreateEditButton.Content = "Update Entry";
            CancelButton.IsVisible = true;

            _editingGameName = game.Name;
            _editingGameRepository = game.Repository;
            _editingGameIdentityKey = game.IdentityKey;
            _editingFolderName = game.FolderName;
            _editingGame = game;
        }

        private void GameNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateGameForm();

        private void GameRepoTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateGameForm();

        private void GameFolderTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateGameForm();

        private void GameIconTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateGameForm();

        private void ValidateGameForm()
        {
            if (!_entryFormShowValidation)
            {
                SetValidationStatus("");
                return;
            }

            var name = NewGameNameTextBox?.Text?.Trim();
            var repository = NewGameRepoTextBox?.Text?.Trim();
            var folderName = NewGameFolderTextBox?.Text?.Trim();

            if (string.IsNullOrEmpty(name))
                SetValidationStatus("Error: App name is required");
            else if (string.IsNullOrEmpty(repository))
                SetValidationStatus("Error: Repository is required");
            else if (string.IsNullOrEmpty(folderName))
                SetValidationStatus("Error: Folder name is required");
            else if (!Uri.TryCreate(repository, UriKind.Absolute, out var repoUri) ||
                     (repoUri.Scheme != Uri.UriSchemeHttp && repoUri.Scheme != Uri.UriSchemeHttps))
                SetValidationStatus("Warning: Repository should be a valid URL");
            else if (!IsValidFolderName(folderName))
                SetValidationStatus("Warning: Folder name contains invalid characters");
            else
                SetValidationStatus("All fields are valid");
        }

        private void SetValidationStatus(string message)
        {
            if (ValidationStatusText != null)
                ValidationStatusText.Text = message;

            if (ValidationStatusBorder != null)
                ValidationStatusBorder.IsVisible = !string.IsNullOrEmpty(message);
        }

        private static bool IsValidFolderName(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return false;

            var invalidChars = Path.GetInvalidFileNameChars();
            if (folderName.IndexOfAny(invalidChars) >= 0)
                return false;

            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            return !reservedNames.Contains(folderName.ToUpper());
        }

        private async void CreateNewEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _entryFormShowValidation = true;

                var name = NewGameNameTextBox?.Text?.Trim();
                var repository = NewGameRepoTextBox?.Text?.Trim();
                var repositorySource = GetSelectedRepositorySource(out var unsupportedSource);
                if (unsupportedSource)
                {
                    _ = ShowMessageBoxAsync(
                        $"Unsupported repository source; defaulting to GitHub.",
                        "Repository Source");
                }

                var folderName = NewGameFolderTextBox?.Text?.Trim();
                var iconUrl = NewGameIconTextBox?.Text?.Trim();
                var project = NewGameProjectTextBox?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(project))
                    project = null;
                var customDisplayName = NewGameCustomDisplayNameTextBox?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(customDisplayName))
                    customDisplayName = null;
                var tags = TagHelper.ParseCommaSeparatedTags(NewGameTagsTextBox?.Text);
                var filesToAdd = AppFilesToAddService.ParseCommaSeparated(NewGameFilesToAddTextBox?.Text);
                var modsPath = GameModsConfig.NormalizePath(NewGameModsPathTextBox?.Text);
                var modsSources = GameModsFormHelper.ParseSourcesFromEditor(NewGameModsSourcesTextBox?.Text);
                var modsLayout = NewGameModsFolderPerModCheckBox?.IsChecked == true
                    ? GameModsConfig.LayoutFolderPerMod
                    : null;
                var identityKey = RepositorySourceHelper.GetIdentityKey(repositorySource, repository);

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(folderName))
                {
                    ValidateGameForm();
                    _ = ShowMessageBoxAsync("Please fill in all required fields (Name, Repository, Folder Name)", "Validation Error");
                    return;
                }

                var games = await LoadGamesFromJsonAsync();

                if (!string.IsNullOrEmpty(_editingGameIdentityKey) || !string.IsNullOrEmpty(_editingGameRepository))
                {
                    var appToUpdate = games.FirstOrDefault(g =>
                        (!string.IsNullOrEmpty(_editingGameIdentityKey) &&
                         string.Equals(g.IdentityKey, _editingGameIdentityKey, StringComparison.OrdinalIgnoreCase)) ||
                        (string.IsNullOrEmpty(_editingGameIdentityKey) &&
                         g.Repository == _editingGameRepository));
                    if (appToUpdate == null)
                    {
                        _ = ShowMessageBoxAsync("Could not find the app to update.", "Error");
                        return;
                    }

                    // Name may be shared across ports (distinguished by project / repository).
                    var oldRepository = appToUpdate.Repository;
                    var oldRepositorySource = appToUpdate.RepositorySource;
                    var oldIdentityKey = appToUpdate.IdentityKey;
                    if (!string.Equals(oldIdentityKey, identityKey, StringComparison.OrdinalIgnoreCase) &&
                        games.Any(g =>
                            !ReferenceEquals(g, appToUpdate) &&
                            string.Equals(g.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        _ = ShowMessageBoxAsync(
                            "Another app already uses this repository and repository source.",
                            "Duplicate Repository");
                        return;
                    }

                    if (!string.Equals(appToUpdate.FolderName, folderName, StringComparison.OrdinalIgnoreCase) &&
                        games.Any(g =>
                            !ReferenceEquals(g, appToUpdate) &&
                            string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _ = ShowMessageBoxAsync(
                            "Another app already uses this folder name.",
                            "Duplicate Folder");
                        return;
                    }

                    if (appToUpdate.FolderName != folderName && !string.IsNullOrEmpty(appToUpdate.FolderName))
                    {
                        var oldPath = Path.Combine(_settings.AppsPath, appToUpdate.FolderName);
                        var newPath = Path.Combine(_settings.AppsPath, folderName);

                        if (Directory.Exists(oldPath))
                        {
                            try
                            {
                                Directory.Move(oldPath, newPath);
                            }
                            catch (Exception ex)
                            {
                                _ = ShowMessageBoxAsync($"Failed to rename folder {appToUpdate.FolderName}", ex.Message);
                            }
                        }
                    }

                    var previousFilesToAdd = AppFilesToAddService.Normalize(appToUpdate.FilesToAdd);
                    appToUpdate.Name = name;
                    appToUpdate.Project = project;
                    appToUpdate.CustomDisplayName = customDisplayName;
                    appToUpdate.Repository = repository;
                    appToUpdate.RepositorySource = RepositorySourceHelper.IsGitHub(repositorySource)
                        ? null
                        : repositorySource;
                    appToUpdate.FolderName = folderName;
                    appToUpdate.GameIconUrl = iconUrl;
                    appToUpdate.Tags = tags;
                    appToUpdate.FilesToAdd = filesToAdd;
                    appToUpdate.ModsPath = modsPath.Length > 0 ? modsPath : null;
                    appToUpdate.ModsSources = modsSources;
                    appToUpdate.ModsLayout = modsLayout;

                    if (!string.IsNullOrWhiteSpace(appToUpdate.Repository))
                        _settings.UserAppDisplayNames.Remove(appToUpdate.Repository);

                    if (AppIdentityMigration.MigrateIdentity(
                            _settings,
                            oldRepositorySource,
                            oldRepository,
                            appToUpdate.RepositorySource,
                            appToUpdate.Repository))
                    {
                        OnSettingChanged();
                    }
                    else
                    {
                        OnSettingChanged();
                    }

                    await SaveGamesToJsonAsync(games);
                    AppFilesToAddService.SyncForGame(appToUpdate, _gameManager.GamesFolder, previousFilesToAdd);
                    _ = ShowMessageBoxAsync("App entry updated successfully", "App Updated");
                }
                else
                {
                    // Name may be shared across ports; uniqueness is repository identity + install folder.
                    if (games.Any(g =>
                            string.Equals(g.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        _ = ShowMessageBoxAsync(
                            "An app with this repository already exists.",
                            "Duplicate Repository");
                        return;
                    }

                    if (games.Any(g =>
                            string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _ = ShowMessageBoxAsync(
                            "An app with this folder name already exists.",
                            "Duplicate Folder");
                        return;
                    }

                    var newApp = new GameInfo
                    {
                        Name = name,
                        Project = project,
                        CustomDisplayName = customDisplayName,
                        Repository = repository,
                        RepositorySource = RepositorySourceHelper.IsGitHub(repositorySource)
                            ? null
                            : repositorySource,
                        FolderName = folderName,
                        GameIconUrl = iconUrl,
                        Tags = tags,
                        FilesToAdd = filesToAdd,
                        ModsPath = modsPath.Length > 0 ? modsPath : null,
                        ModsSources = modsSources,
                        ModsLayout = modsLayout,
                        AutoUpdate = _settings.AutoUpdateNewlyAddedApps,
                        IsCustom = true,
                        IsExperimental = false
                    };
                    games.Add(newApp);

                    await SaveGamesToJsonAsync(games);
                    AppFilesToAddService.SyncForGame(newApp, _gameManager.GamesFolder);
                    _ = ShowMessageBoxAsync("New app entry created successfully", "App Added");
                }

                await _gameManager.LoadGamesAsync();
                ApplySorting();
                CloseEntryFormOverlay();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Error saving app entry: {ex.Message}", "Error");
            }
        }

        private void EditGameEntry_Click(object sender, RoutedEventArgs e)
        {
            var game = (sender as MenuItem)?.CommandParameter as GameInfo;
            if (game == null)
                return;

            ShowEntryFormOverlay(forCreate: false, gameToEdit: game);
        }

        private enum TagEditOverlayMode
        {
            Tags,
            CustomDisplayName,
        }

        private TagEditOverlayMode _tagEditOverlayMode = TagEditOverlayMode.Tags;

        private void EditTagsMenu_Click(object sender, RoutedEventArgs e)
        {
            var game = (sender as MenuItem)?.CommandParameter as GameInfo;
            if (game == null)
                return;

            ShowTagEditOverlay(game, TagEditOverlayMode.Tags);
        }

        private void EditCustomDisplayNameMenu_Click(object sender, RoutedEventArgs e)
        {
            var game = (sender as MenuItem)?.CommandParameter as GameInfo;
            if (game == null)
                return;

            ShowTagEditOverlay(game, TagEditOverlayMode.CustomDisplayName);
        }

        private void ShowTagEditOverlay(GameInfo game, TagEditOverlayMode mode = TagEditOverlayMode.Tags)
        {
            _editingTagsGame = game;
            _tagEditOverlayMode = mode;
            TagEditAppNameText.Text = game.DisplayName;
            if (TagEditTitleText != null)
            {
                TagEditTitleText.Text = mode == TagEditOverlayMode.CustomDisplayName
                    ? "Custom Display Name"
                    : "Edit Tags";
            }

            TagEditTextBox.Watermark = mode == TagEditOverlayMode.CustomDisplayName
                ? "Leave blank to use library name style"
                : "Tags (comma-separated)";
            TagEditTextBox.Text = mode == TagEditOverlayMode.CustomDisplayName
                ? (game.CustomDisplayName ?? "")
                : TagHelper.FormatTagsForDisplay(game.Tags);

            var gamingMode = SteamDeckEnvironment.IsGamingMode();
            TagEditOverlayLayout.ApplyDialogPlacement(TagEditDialogPanel, gamingMode);

            _isTagEditOpen = true;
            TagEditOverlay.IsVisible = true;
            ClearGamepadFocus();
            ClearSidebarGamepadFocus();
            ClearTopBarGamepadFocus();
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.TagEditOverlay;
            OnPropertyChanged(nameof(GamepadHintsVisible));
            var initialFocus = TagEditOverlayLayout.GetInitialFocusIndex(gamingMode);
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsGamepadFocusActive)
                {
                    ClearTagEditGamepadFocus();
                    DismissTextInputFocus();
                    return;
                }

                ApplyTagEditGamepadSelection(initialFocus);
            }, DispatcherPriority.Loaded);
        }

        private void CloseTagEditOverlay()
        {
            ClearTagEditGamepadFocus();
            _tagEditGamepadFocusIndex = -1;
            _isTagEditOpen = false;
            TagEditOverlay.IsVisible = false;
            _editingTagsGame = null;
            TagEditOverlayLayout.ApplyDialogPlacement(TagEditDialogPanel, isGamingMode: false);

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.TagEditOverlay)
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                SelectInitialGamepadItemForCurrentView();
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        private void CancelTagEdit_Click(object? sender, RoutedEventArgs e)
        {
            CloseTagEditOverlay();
        }

        private async void SaveTagEdit_Click(object? sender, RoutedEventArgs e)
        {
            if (_editingTagsGame == null)
                return;

            try
            {
                if (_tagEditOverlayMode == TagEditOverlayMode.CustomDisplayName)
                {
                    var customName = TagEditTextBox?.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(customName))
                        customName = null;
                    await SaveCustomDisplayNameForGameAsync(_editingTagsGame, customName);
                }
                else
                {
                    var tags = TagHelper.ParseCommaSeparatedTags(TagEditTextBox?.Text);
                    await SaveTagsForGameAsync(_editingTagsGame, tags);
                }

                CloseTagEditOverlay();
                await _gameManager.LoadGamesAsync();
                ApplySorting();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to save: {ex.Message}", "Error");
            }
        }

        private async Task SaveTagsForGameAsync(GameInfo game, List<string> tags)
        {
            game.Tags = tags;

            if (game.IsInLocalAppsJson && !string.IsNullOrWhiteSpace(game.Repository))
            {
                var games = await LoadGamesFromJsonAsync();
                var appToUpdate = games.FirstOrDefault(g =>
                    !string.IsNullOrWhiteSpace(g.Repository) &&
                    g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

                if (appToUpdate != null)
                {
                    appToUpdate.Tags = tags;
                    await SaveGamesToJsonAsync(games);
                }

                if (!string.IsNullOrWhiteSpace(game.Repository))
                    _settings.UserAppTags.Remove(game.Repository);
            }
            else if (!string.IsNullOrWhiteSpace(game.Repository))
            {
                _settings.UserAppTags[game.Repository] = tags;
            }

            OnSettingChanged();
        }

        private async Task SaveCustomDisplayNameForGameAsync(GameInfo game, string? customDisplayName)
        {
            game.CustomDisplayName = customDisplayName;

            if (game.IsInLocalAppsJson && !string.IsNullOrWhiteSpace(game.Repository))
            {
                var games = await LoadGamesFromJsonAsync();
                var appToUpdate = games.FirstOrDefault(g =>
                    !string.IsNullOrWhiteSpace(g.Repository) &&
                    g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

                if (appToUpdate != null)
                {
                    appToUpdate.CustomDisplayName = customDisplayName;
                    await SaveGamesToJsonAsync(games);
                }

                _settings.UserAppDisplayNames.Remove(game.Repository);
            }
            else if (!string.IsNullOrWhiteSpace(game.Repository))
            {
                if (customDisplayName == null)
                    _settings.UserAppDisplayNames.Remove(game.Repository);
                else
                    _settings.UserAppDisplayNames[game.Repository] = customDisplayName;
            }

            OnSettingChanged();
        }

        private void CancelForm_Click(object? sender, RoutedEventArgs e)
        {
            CloseEntryFormOverlay();
        }

        private void ClearForm()
        {
            if (NewGameNameTextBox != null) NewGameNameTextBox.Text = "";
            SetRepositorySourceSelection(RepositorySourceIds.GitHub);
            if (NewGameRepoTextBox != null)
            {
                NewGameRepoTextBox.Text = "";
                NewGameRepoTextBox.IsReadOnly = false;
            }
            if (NewGameFolderTextBox != null) NewGameFolderTextBox.Text = "";
            if (NewGameIconTextBox != null) NewGameIconTextBox.Text = "";
            if (NewGameTagsTextBox != null) NewGameTagsTextBox.Text = "";
            if (NewGameProjectTextBox != null) NewGameProjectTextBox.Text = "";
            if (NewGameCustomDisplayNameTextBox != null) NewGameCustomDisplayNameTextBox.Text = "";
            if (NewGameFilesToAddTextBox != null) NewGameFilesToAddTextBox.Text = "";
            if (NewGameModsPathTextBox != null) NewGameModsPathTextBox.Text = "";
            if (NewGameModsFolderPerModCheckBox != null) NewGameModsFolderPerModCheckBox.IsChecked = false;
            if (NewGameModsSourcesTextBox != null) NewGameModsSourcesTextBox.Text = "";
            if (CreateEditButton != null) CreateEditButton.Content = "Create Entry";
            if (FormTitleText != null) FormTitleText.Text = "Create New Entry";
            if (ValidationStatusText != null) ValidationStatusText.Text = "";

            _entryFormShowValidation = false;
            _editingGameName = null;
            _editingGameRepository = null;
            _editingGameIdentityKey = null;
            _editingFolderName = null;
            _editingGame = null;
        }

        private void SetRepositorySourceSelection(string? repositorySource)
        {
            if (NewGameRepositorySourceComboBox == null)
                return;

            var normalized = RepositorySourceHelper.Normalize(repositorySource);
            foreach (var item in NewGameRepositorySourceComboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    comboItem.Tag is string tag &&
                    string.Equals(tag, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    NewGameRepositorySourceComboBox.SelectedItem = comboItem;
                    return;
                }
            }

            NewGameRepositorySourceComboBox.SelectedIndex = 0;
        }

        private string GetSelectedRepositorySource(out bool wasUnsupported)
        {
            wasUnsupported = false;
            if (NewGameRepositorySourceComboBox?.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                return RepositorySourceHelper.Normalize(tag, out wasUnsupported);
            }

            return RepositorySourceIds.GitHub;
        }

        private async void RemoveGameEntry_Click(object sender, RoutedEventArgs e)
        {
            var game = (sender as MenuItem)?.CommandParameter as GameInfo;
            if (game == null || string.IsNullOrEmpty(game.Repository) || string.IsNullOrEmpty(game.Name))
                return;

            if (!game.IsInLocalAppsJson)
                return;

            try
            {
                var confirm = await ShowMessageBoxAsync(
                    $"Are you sure you want to remove '{game.Name}' from your apps list?\n\nThis will only remove it from the launcher, not delete your files.",
                    "Confirm Removal",
                    true);

                if (!confirm)
                    return;

                var games = await LoadGamesFromJsonAsync();
                var gameToRemove = games.FirstOrDefault(g =>
                    !string.IsNullOrWhiteSpace(g.Repository) &&
                    g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

                if (gameToRemove == null)
                    return;

                games.Remove(gameToRemove);
                await SaveGamesToJsonAsync(games);
                await _gameManager.LoadGamesAsync();
                ApplySorting();

                await _gameManager.CatalogService.IgnoreRepositoryInMatchingSourcesAsync(
                    _settings,
                    game.Repository);
                OnSettingChanged();
                RefreshCatalogSourcesList();

                if (_activeCatalogSyncSource != null)
                    await RefreshActiveCatalogSyncRowsAsync();

                _ = ShowMessageBoxAsync($"'{game.Name}' was removed successfully.", "Removed");
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Error removing app: {ex.Message}", "Error");
            }
        }

        private void MusicVolumeSlider_ValueChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider)
            {
                MusicVolume = (float)slider.Value;
                _settings.MusicVolume = MusicVolume;
                AppSettings.Save(_settings);
            }
        }

        private void PlayLauncherMusic(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                StopLauncherMusic();

                // Use runtime detection
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    PlayMusicWindows(path);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    PlayMusicLinux(path);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    PlayMusicMac(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to play launcher music: {ex.Message}");
            }
        }

        private void PlayMusicWindows(string path)
        {
            #if WINDOWS
            try
            {
                _audioFileReader = new AudioFileReader(path);
                _audioFileReader.Volume = MusicVolume;

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioFileReader);

                // Enable looping
                _waveOut.PlaybackStopped += (sender, args) =>
                {
                    if (_audioFileReader != null && _waveOut != null)
                    {
                        _audioFileReader.Position = 0;
                        _waveOut.Play();
                    }
                };

                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NAudio playback failed: {ex.Message}");
            }
        #else
            Debug.WriteLine("Windows audio playback not available on this platform");
        #endif
        }

        private void PlayMusicLinux(string path)
        {
            string[] players = { "ffplay", "mpv", "cvlc", "mplayer" };

            foreach (var player in players)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = player,
                        Arguments = player switch
                        {
                            "ffplay" => $"-nodisp -autoexit -loop 0 -volume {(int)(MusicVolume * 100)} \"{path}\"",
                            "mpv" => $"--no-video --loop=inf --volume={MusicVolume * 100} \"{path}\"",
                            "cvlc" => $"--no-video --loop --volume {(int)(MusicVolume * 512)} \"{path}\"",
                            "mplayer" => $"-loop 0 -volume {(int)(MusicVolume * 100)} \"{path}\"",
                            _ => $"\"{path}\""
                        },
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _musicProcess = Process.Start(psi);
                    if (_musicProcess != null)
                    {
                        _musicProcess.EnableRaisingEvents = true;
                        Debug.WriteLine($"Playing music with {player}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to start {player}: {ex.Message}");
                    continue;
                }
            }

            Debug.WriteLine("No suitable audio player found on Linux. Install one of: ffplay, mpv, vlc, mplayer");
        }

        private void PlayMusicMac(string path)
        {
            try
            {
                // afplay volume is 0-255 (0-1 range needs to be converted)
                var volumeValue = MusicVolume * 255f;

                var psi = new ProcessStartInfo
                {
                    FileName = "afplay",
                    Arguments = $"-v {volumeValue} \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _musicProcess = Process.Start(psi);

                if (_musicProcess != null)
                {
                    _musicProcess.EnableRaisingEvents = true;
                    _musicProcess.Exited += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
                        {
                            try
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    if (!string.IsNullOrEmpty(LauncherMusicPath))
                                    {
                                        PlayMusicMac(LauncherMusicPath);
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to restart music: {ex.Message}");
                            }
                        }
                    };

                    Debug.WriteLine($"Playing music with afplay at volume {volumeValue}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"afplay failed: {ex.Message}");
            }
        }

        private void StopLauncherMusic()
        {
            try
            {
                #if WINDOWS
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }

                if (_audioFileReader != null)
                {
                    _audioFileReader.Dispose();
                    _audioFileReader = null;
                }
                #endif

                if (_musicProcess != null)
                {
                    try
                    {
                        if (!_musicProcess.HasExited)
                        {
                            _musicProcess.Kill();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited, ignore
                    }

                    _musicProcess.Dispose();
                    _musicProcess = null;
                }

                _musicPausedByDeactivation = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop launcher music: {ex.Message}");
            }
        }

        private async Task FadeMusicAsync(float targetVolume, int durationMs)
        {
        #if WINDOWS
            if (_audioFileReader == null)
                return;

        // Cancel any ongoing fade
        _fadeTaskCts?.Cancel();
        _fadeTaskCts = new System.Threading.CancellationTokenSource();
        var token = _fadeTaskCts.Token;

        try
        {
            float currentVolume = _audioFileReader.Volume;
            float targetVol = targetVolume;

            if (Math.Abs(currentVolume - targetVol) < 0.001f)
                return;

            int steps = 20;
            int stepDelay = durationMs / steps;
            float volumeStep = (targetVol - currentVolume) / steps;

            for (int i = 0; i < steps; i++)
            {
                if (token.IsCancellationRequested || _audioFileReader == null)
                    return;

                currentVolume += volumeStep;
                _audioFileReader.Volume = Math.Clamp(currentVolume, 0f, 1f);

                await Task.Delay(stepDelay, token);
            }

                if (_audioFileReader != null && !token.IsCancellationRequested)
                {
                    _audioFileReader.Volume = targetVol;
                }
            }
            catch (OperationCanceledException)
            {
                // Fade was cancelled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during music fade: {ex.Message}");
            }
            #else
                if (targetVolume < 0.01f)
                {
                    if (_musicProcess != null && !_musicProcess.HasExited)
                    {
                        try
                        {
                            _musicProcess.Kill();
                            Debug.WriteLine("Music paused (process killed)");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to pause music: {ex.Message}");
                        }
                    }
                }
                else if (targetVolume > 0.01f)
                {
                    if (_musicProcess == null || _musicProcess.HasExited)
                    {
                        if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
                        {
                            PlayLauncherMusic(LauncherMusicPath);
                            Debug.WriteLine("Music resumed");
                        }
                    }
                }
    
                await Task.CompletedTask;
            #endif
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_isProcessingInput || !IsActive)
                return;

            _isProcessingInput = true;

            try
            {
                if (_keyboardRebindListeningAction.HasValue)
                {
                    if (e.Key == Key.Escape)
                    {
                        CancelKeyboardRebindListen();
                        e.Handled = true;
                        return;
                    }

                    if (KeyboardBindingDefaults.IsModifierOnlyKey(e.Key))
                        return;

                    ApplyCapturedKeyboardBinding(new KeyboardBinding(e.Key, e.KeyModifiers));
                    e.Handled = true;
                    return;
                }

                // Esc cancels gamepad rebind listen (same as B / Cancel).
                if (_rebindListeningAction.HasValue && e.Key == Key.Escape)
                {
                    CancelGamepadRebindListen();
                    e.Handled = true;
                    return;
                }

                _settings.EnsureInitialized();
                var action = KeyboardBindingDefaults.FindAction(
                    _settings.KeyboardBindings,
                    e.Key,
                    e.KeyModifiers);
                if (action == null)
                    return;

                // While editing a TextBox, let typing through (including Backspace). Escape still
                // dismisses focus / runs cancel even when Cancel is bound to another key.
                var editingText = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
                if (editingText)
                {
                    if (e.Key == Key.Escape)
                    {
                        ActivateKeyboardNavChrome();
                        HandleCancelAction();
                        e.Handled = true;
                    }

                    return;
                }

                switch (action)
                {
                    case GamepadAction.Confirm:
                        ActivateKeyboardNavChrome();
                        HandleConfirmAction();
                        _suppressConfirmKeyUp = true;
                        e.Handled = true;
                        break;

                    case GamepadAction.Cancel:
                        ActivateKeyboardNavChrome();
                        HandleCancelAction();
                        e.Handled = true;
                        break;

                    case GamepadAction.Options:
                        ActivateKeyboardNavChrome();
                        HandleOptionsAction();
                        e.Handled = true;
                        break;

                    case GamepadAction.NavUp:
                        ActivateKeyboardNavChrome();
                        _inputService?.HandleNavigation(Services.NavigationDirection.Up);
                        e.Handled = true;
                        break;

                    case GamepadAction.NavDown:
                        ActivateKeyboardNavChrome();
                        _inputService?.HandleNavigation(Services.NavigationDirection.Down);
                        e.Handled = true;
                        break;

                    case GamepadAction.NavLeft:
                        ActivateKeyboardNavChrome();
                        _inputService?.HandleNavigation(Services.NavigationDirection.Left);
                        e.Handled = true;
                        break;

                    case GamepadAction.NavRight:
                        ActivateKeyboardNavChrome();
                        _inputService?.HandleNavigation(Services.NavigationDirection.Right);
                        e.Handled = true;
                        break;
                }
            }
            finally
            {
                _isProcessingInput = false;
            }
        }

        private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
        {
            if (_suppressConfirmKeyUp && IsKeyboardConfirmBoundKey(e.Key))
            {
                _suppressConfirmKeyUp = false;
                e.Handled = true;
                return;
            }

            if (IsKeyboardNavBoundKey(e.Key))
                _inputService?.ResetNavigationTimer();
        }

        private bool IsKeyboardConfirmBoundKey(Key key)
        {
            _settings.EnsureInitialized();
            return _settings.KeyboardBindings.TryGetValue(GamepadAction.Confirm, out var list) &&
                   list != null &&
                   list.Any(b => b.Key == key);
        }

        private bool IsKeyboardNavBoundKey(Key key)
        {
            _settings.EnsureInitialized();
            foreach (var action in new[]
                     {
                         GamepadAction.NavUp,
                         GamepadAction.NavDown,
                         GamepadAction.NavLeft,
                         GamepadAction.NavRight,
                     })
            {
                if (_settings.KeyboardBindings.TryGetValue(action, out var list) &&
                    list != null &&
                    list.Any(b => b.Key == key))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HandleGamepadNavigation(Services.NavigationDirection direction)
        {
            if (_launchedGameOwnsInput)
                return false;

            if (!_settings.EnableGamepadInput && !GamepadFocusChrome.KeyboardNavigationActive)
                return false;

            if (IsDisplayFilterOverlayOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.DisplayFilterOverlay;
                return HandleDisplayFilterGamepadNavigation(direction);
            }

            if (_isChangelogOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.ChangelogOverlay;
                return HandleChangelogGamepadNavigation(direction);
            }

            if (_isModDetailsOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsDetailsOverlay;
                return HandleModDetailsGamepadNavigation(direction);
            }

            if (_isEntryFormOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.EntryFormOverlay;
                return HandleEntryFormGamepadNavigation(direction);
            }

            if (_isTagEditOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.TagEditOverlay;
                return HandleTagEditGamepadNavigation(direction);
            }

            if (isSettingsPanelOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;
                return HandleSettingsGamepadNavigation(direction);
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.Sidebar)
                return HandleSidebarGamepadNavigation(direction);

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.TopBar)
                return HandleTopBarGamepadNavigation(direction);

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.AnnouncementBanner)
                return HandleAnnouncementBannerGamepadNavigation(direction);

            if (_mainViewMode == MainViewMode.AppCatalog && _appCatalogSubView == AppCatalogSubView.Review)
                return HandleCatalogReviewGamepadNavigation(direction);

            if (_mainViewMode == MainViewMode.Library && _isModsOverlayOpen)
                return HandleModsGamepadNavigation(direction);

            if (_mainViewMode == MainViewMode.Library && _isAppUpdatesReviewOpen)
                return HandleAppUpdatesReviewGamepadNavigation(direction);

            if (_mainViewMode == MainViewMode.AppCatalog && _appCatalogSubView == AppCatalogSubView.Sources)
            {
                return _gamepadNavigation.ActiveZone switch
                {
                    GamepadNavigationZone.CatalogSourcesToolbar => HandleCatalogSourcesToolbarNavigation(direction),
                    GamepadNavigationZone.CatalogSourcesFilters => HandleCatalogSourcesFiltersNavigation(direction),
                    GamepadNavigationZone.CatalogSourceCardActions => HandleCatalogSourceCardActionsNavigation(direction),
                    _ => HandleCatalogSourcesCardNavigation(direction),
                };
            }

            if (_mainViewMode == MainViewMode.Library)
                return HandleLibraryGamepadNavigation(direction);

            return false;
        }

        private bool HandleDisplayFilterGamepadNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectDisplayFilterFocusableControls();
            if (controls.Count == 0)
                return true;

            // Keep focus trapped in the overlay — never zone-transition to Library/sidebar.
            var nextIndex = direction is Services.NavigationDirection.Up or Services.NavigationDirection.Left
                ? _gamepadNavigation.MoveListIndex(_displayFilterGamepadFocusIndex, Services.NavigationDirection.Up, controls.Count)
                : _gamepadNavigation.MoveListIndex(_displayFilterGamepadFocusIndex, Services.NavigationDirection.Down, controls.Count);

            ApplyDisplayFilterGamepadSelection(nextIndex);
            return true;
        }

        private bool HandleEntryFormGamepadNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectEntryFormFocusableControls();
            if (controls.Count == 0)
                return true;

            // Keep focus trapped in the form — never zone-transition to Library underneath.
            var nextIndex = direction is Services.NavigationDirection.Up or Services.NavigationDirection.Left
                ? _gamepadNavigation.MoveListIndex(_entryFormGamepadFocusIndex, Services.NavigationDirection.Up, controls.Count)
                : _gamepadNavigation.MoveListIndex(_entryFormGamepadFocusIndex, Services.NavigationDirection.Down, controls.Count);

            ApplyEntryFormGamepadSelection(nextIndex);
            return true;
        }

        private bool HandleChangelogGamepadNavigation(Services.NavigationDirection direction)
        {
            // Keep focus trapped on Close; Up/Down scroll the changelog body.
            var scrollViewer = this.FindControl<ScrollViewer>("ChangelogScrollViewer");
            if (scrollViewer != null)
            {
                const double step = 96;
                if (direction is Services.NavigationDirection.Down or Services.NavigationDirection.Right)
                {
                    scrollViewer.Offset = new Avalonia.Vector(
                        scrollViewer.Offset.X,
                        scrollViewer.Offset.Y + step);
                }
                else if (direction is Services.NavigationDirection.Up or Services.NavigationDirection.Left)
                {
                    scrollViewer.Offset = new Avalonia.Vector(
                        scrollViewer.Offset.X,
                        Math.Max(0, scrollViewer.Offset.Y - step));
                }
            }

            ApplyChangelogGamepadSelection(0);
            return true;
        }

        private List<Control> CollectChangelogFocusableControls()
        {
            var controls = new List<Control>();
            var closeButton = this.FindControl<Button>("CloseChangelogButton");
            if (closeButton != null && closeButton.IsVisible && closeButton.IsEnabled)
                controls.Add(closeButton);
            return controls;
        }

        private void ApplyChangelogGamepadSelection(int index)
        {
            var controls = CollectChangelogFocusableControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _changelogGamepadFocusIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.ChangelogOverlay;

            ClearGamepadFocus();
            ClearSidebarGamepadFocus();
            ClearChangelogGamepadFocus();
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
        }

        private void ActivateChangelogGamepadSelection()
        {
            var controls = CollectChangelogFocusableControls();
            var index = _gamepadNavigation.ClampIndex(_changelogGamepadFocusIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void ClearChangelogGamepadFocus()
        {
            ClearStyledControlsGamepadFocusClasses(CollectChangelogFocusableControls());
        }

        private List<Control> CollectEntryFormFocusableControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled && control.Focusable)
                    controls.Add(control);
            }

            Add(NewGameNameTextBox);
            Add(NewGameProjectTextBox);
            Add(NewGameCustomDisplayNameTextBox);
            Add(NewGameRepositorySourceComboBox);
            Add(NewGameRepoTextBox);
            Add(NewGameFolderTextBox);
            Add(NewGameTagsTextBox);
            Add(NewGameIconTextBox);
            Add(NewGameFilesToAddTextBox);
            Add(NewGameModsPathTextBox);
            Add(NewGameModsFolderPerModCheckBox);
            Add(NewGameModsSourcesTextBox);
            Add(CancelButton);
            Add(CreateEditButton);
            return controls;
        }

        private void ApplyEntryFormGamepadSelection(int index)
        {
            var controls = CollectEntryFormFocusableControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _entryFormGamepadFocusIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.EntryFormOverlay;

            ClearGamepadFocus();
            ClearSidebarGamepadFocus();
            ClearEntryFormGamepadFocus();
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ActivateEntryFormGamepadSelection()
        {
            var controls = CollectEntryFormFocusableControls();
            var index = _gamepadNavigation.ClampIndex(_entryFormGamepadFocusIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            var control = controls[index];
            if (control is Button button)
                GamepadControlActivation.ActivateButton(button);
            else if (control is TextBox textBox)
                GamepadControlActivation.ActivateTextBox(textBox);
            else
                control.Focus();
        }

        private void ClearEntryFormGamepadFocus()
        {
            ClearStyledControlsGamepadFocusClasses(CollectEntryFormFocusableControls());
        }

        private bool HandleTagEditGamepadNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectTagEditFocusableControls();
            if (controls.Count == 0)
                return true;

            // Keep focus trapped in the overlay — never zone-transition to Library underneath.
            var nextIndex = direction is Services.NavigationDirection.Up or Services.NavigationDirection.Left
                ? _gamepadNavigation.MoveListIndex(_tagEditGamepadFocusIndex, Services.NavigationDirection.Up, controls.Count)
                : _gamepadNavigation.MoveListIndex(_tagEditGamepadFocusIndex, Services.NavigationDirection.Down, controls.Count);

            ApplyTagEditGamepadSelection(nextIndex);
            return true;
        }

        private List<Control> CollectTagEditFocusableControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled && control.Focusable)
                    controls.Add(control);
            }

            Add(TagEditTextBox);
            Add(TagEditCancelButton);
            Add(TagEditSaveButton);
            return controls;
        }

        private void ApplyTagEditGamepadSelection(int index)
        {
            var controls = CollectTagEditFocusableControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _tagEditGamepadFocusIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.TagEditOverlay;

            ClearGamepadFocus();
            ClearSidebarGamepadFocus();
            ClearTopBarGamepadFocus();
            ClearTagEditGamepadFocus();
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
        }

        private void ActivateTagEditGamepadSelection()
        {
            var controls = CollectTagEditFocusableControls();
            var index = _gamepadNavigation.ClampIndex(_tagEditGamepadFocusIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            var control = controls[index];
            if (control is Button button)
                GamepadControlActivation.ActivateButton(button);
            else if (control is TextBox textBox)
                GamepadControlActivation.ActivateTextBox(textBox);
            else
                control.Focus();
        }

        private void ClearTagEditGamepadFocus()
        {
            ClearStyledControlsGamepadFocusClasses(CollectTagEditFocusableControls());
        }

        private List<Control> CollectDisplayFilterFocusableControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled && control.Focusable)
                    controls.Add(control);
            }

            Add(DisplayFilterNameTextBox);
            Add(DisplayFilterTagsTextBox);
            Add(DisplayFilterMatchModeComboBox);
            Add(DisplayFilterExcludeTagsTextBox);
            Add(DisplayFilterExcludeMatchModeComboBox);
            Add(CancelDisplayFilterButton);
            Add(SaveDisplayFilterButton);
            return controls;
        }

        private void ApplyDisplayFilterGamepadSelection(int index)
        {
            var controls = CollectDisplayFilterFocusableControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _displayFilterGamepadFocusIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.DisplayFilterOverlay;

            ClearDisplayFilterGamepadFocus();
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ActivateDisplayFilterGamepadSelection()
        {
            var controls = CollectDisplayFilterFocusableControls();
            var index = _gamepadNavigation.ClampIndex(_displayFilterGamepadFocusIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            var control = controls[index];
            if (control is Button button)
                GamepadControlActivation.ActivateButton(button);
            else if (control is ComboBox comboBox)
                GamepadComboBoxNavigation.Open(comboBox);
            else if (control is TextBox textBox)
                GamepadControlActivation.ActivateTextBox(textBox);
            else
                control.Focus();
        }

        private void ClearDisplayFilterGamepadFocus()
        {
            ClearStyledControlsGamepadFocusClasses(CollectDisplayFilterFocusableControls());
        }

        private bool HandleSettingsGamepadNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectSettingsFocusableControls();
            if (controls.Count == 0)
                return true;

            var tabs = CollectSettingsTabItems();
            var focused = GetSettingsFocusedControl(controls);
            var closeIndex = FindSettingsCloseButtonIndex(controls);

            // Tab strip: Left/Right switch tabs; Up goes to close; Down enters content.
            if (focused is TabItem)
            {
                if (direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
                {
                    NavigateSettingsTabHeader(direction);
                    return true;
                }

                if (direction == Services.NavigationDirection.Up)
                {
                    if (closeIndex >= 0)
                        ApplySettingsGamepadSelection(closeIndex);
                    return true;
                }

                if (direction == Services.NavigationDirection.Down)
                {
                    var contentIndex = FindFirstSettingsContentIndex(controls);
                    if (contentIndex >= 0)
                        ApplySettingsGamepadSelection(contentIndex);
                    return true;
                }

                return true;
            }

            // Close button sits above the tab strip.
            if (closeIndex >= 0 && _settingsGamepadFocusIndex == closeIndex)
            {
                if (direction is Services.NavigationDirection.Down or Services.NavigationDirection.Right)
                {
                    ApplySettingsGamepadSelection(GetSelectedSettingsTabControlIndex(tabs));
                    return true;
                }

                return true;
            }

            // Sliders: Left/Right change the value; Up/Down still move between controls.
            if (focused is Slider slider &&
                direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
            {
                AdjustSettingsSliderValue(slider, direction);
                return true;
            }

            // Content controls: navigate by on-screen position so horizontal preset
            // rows do not steal Up/Down. Up with no neighbor above returns to the tab.
            var contentIndices = CollectSettingsContentIndices(controls);
            var contentPos = contentIndices.IndexOf(_settingsGamepadFocusIndex);
            if (contentPos < 0)
            {
                ApplySettingsGamepadSelection(GetSelectedSettingsTabControlIndex(tabs));
                return true;
            }

            var nextContentPos = FindNearestSettingsContentIndex(controls, contentIndices, contentPos, direction);
            if (nextContentPos < 0)
            {
                if (direction == Services.NavigationDirection.Up)
                    ApplySettingsGamepadSelection(GetSelectedSettingsTabControlIndex(tabs));
                return true;
            }

            ApplySettingsGamepadSelection(contentIndices[nextContentPos]);
            return true;
        }

        private static void AdjustSettingsSliderValue(Slider slider, Services.NavigationDirection direction)
        {
            var step = slider.TickFrequency > 0
                ? slider.TickFrequency
                : slider.SmallChange > 0
                    ? slider.SmallChange
                    : 1;

            var delta = direction == Services.NavigationDirection.Right ? step : -step;
            var next = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);

            if (slider.TickFrequency > 0)
            {
                var ticksFromMin = Math.Round((next - slider.Minimum) / slider.TickFrequency);
                next = Math.Clamp(
                    slider.Minimum + (ticksFromMin * slider.TickFrequency),
                    slider.Minimum,
                    slider.Maximum);
            }

            slider.Value = next;
            // Card layout behind Settings can steal keyboard focus when sizes change;
            // keep the slider focused for continued Left/Right adjustment.
            slider.Focus();
        }

        private int FindNearestSettingsContentIndex(
            IReadOnlyList<Control> controls,
            IReadOnlyList<int> contentIndices,
            int contentPos,
            Services.NavigationDirection direction)
        {
            if (contentPos < 0 || contentPos >= contentIndices.Count)
                return -1;

            var currentCenter = GetSettingsControlCenter(controls[contentIndices[contentPos]]);
            if (!currentCenter.HasValue)
                return -1;

            int? bestPos = null;
            var bestScore = double.MaxValue;

            for (var i = 0; i < contentIndices.Count; i++)
            {
                if (i == contentPos)
                    continue;

                var candidateCenter = GetSettingsControlCenter(controls[contentIndices[i]]);
                if (!candidateCenter.HasValue)
                    continue;

                var score = ScoreSettingsNavigation(currentCenter.Value, candidateCenter.Value, direction);
                if (!score.HasValue || score.Value >= bestScore)
                    continue;

                bestScore = score.Value;
                bestPos = i;
            }

            return bestPos ?? -1;
        }

        private Avalonia.Point? GetSettingsControlCenter(Control control)
        {
            var origin = SettingsPanel as Visual ?? this;
            var topLeft = control.TranslatePoint(new Avalonia.Point(0, 0), origin);
            if (!topLeft.HasValue)
                return null;

            var bounds = control.Bounds;
            return new Avalonia.Point(
                topLeft.Value.X + bounds.Width / 2,
                topLeft.Value.Y + bounds.Height / 2);
        }

        private static double? ScoreSettingsNavigation(
            Avalonia.Point current,
            Avalonia.Point candidate,
            Services.NavigationDirection direction)
        {
            var dx = candidate.X - current.X;
            var dy = candidate.Y - current.Y;

            // Up/Down must change rows so left-aligned checkboxes (Fill Cards) are not
            // skipped in favor of full-width sliders further down, and so preset-row
            // Left/Right neighbors are not treated as Up/Down targets.
            const double rowTolerance = 20;

            switch (direction)
            {
                case Services.NavigationDirection.Up:
                    if (dy >= -rowTolerance)
                        return null;
                    // Closest row below/above wins; light X tie-break only.
                    return Math.Abs(dy) + (Math.Abs(dx) * 0.05);

                case Services.NavigationDirection.Down:
                    if (dy <= rowTolerance)
                        return null;
                    return Math.Abs(dy) + (Math.Abs(dx) * 0.05);

                case Services.NavigationDirection.Left:
                    if (dx >= -1)
                        return null;
                    {
                        var secondary = Math.Abs(dy);
                        var offAxis = secondary > 10 ? secondary * 2.5 : 0;
                        return Math.Abs(dx) + (secondary * 0.3) + offAxis;
                    }

                case Services.NavigationDirection.Right:
                    if (dx <= 1)
                        return null;
                    {
                        var secondary = Math.Abs(dy);
                        var offAxis = secondary > 10 ? secondary * 2.5 : 0;
                        return Math.Abs(dx) + (secondary * 0.3) + offAxis;
                    }

                default:
                    return null;
            }
        }

        private void NavigateSettingsTabHeader(Services.NavigationDirection direction)
        {
            if (SettingsTabControl == null)
                return;

            var tabs = CollectSettingsTabItems();
            if (tabs.Count == 0)
                return;

            var currentTabIndex = SettingsTabControl.SelectedIndex;
            if (_settingsGamepadFocusIndex >= 0)
            {
                var controls = CollectSettingsFocusableControls();
                if (_settingsGamepadFocusIndex < controls.Count &&
                    controls[_settingsGamepadFocusIndex] is TabItem focusedTab)
                {
                    currentTabIndex = tabs.IndexOf(focusedTab);
                }
            }

            if (currentTabIndex < 0)
                currentTabIndex = 0;

            var nextTabIndex = direction == Services.NavigationDirection.Left
                ? (currentTabIndex - 1 + tabs.Count) % tabs.Count
                : (currentTabIndex + 1) % tabs.Count;

            SettingsTabControl.SelectedIndex = nextTabIndex;
            ApplySettingsGamepadSelection(nextTabIndex);
        }

        private int GetSelectedSettingsTabControlIndex(IReadOnlyList<TabItem> tabs)
        {
            if (tabs.Count == 0)
                return 0;

            var selected = SettingsTabControl?.SelectedIndex ?? 0;
            return Math.Clamp(selected, 0, tabs.Count - 1);
        }

        private int FindSettingsCloseButtonIndex(IReadOnlyList<Control> controls)
        {
            if (CloseSettingsButton == null)
                return -1;

            for (var i = 0; i < controls.Count; i++)
            {
                if (ReferenceEquals(controls[i], CloseSettingsButton))
                    return i;
            }

            return -1;
        }

        private int FindFirstSettingsContentIndex(IReadOnlyList<Control> controls)
        {
            for (var i = 0; i < controls.Count; i++)
            {
                if (controls[i] is TabItem)
                    continue;
                if (CloseSettingsButton != null && ReferenceEquals(controls[i], CloseSettingsButton))
                    continue;

                return i;
            }

            return -1;
        }

        private List<int> CollectSettingsContentIndices(IReadOnlyList<Control> controls)
        {
            var indices = new List<int>();
            for (var i = 0; i < controls.Count; i++)
            {
                if (controls[i] is TabItem)
                    continue;
                if (CloseSettingsButton != null && ReferenceEquals(controls[i], CloseSettingsButton))
                    continue;

                indices.Add(i);
            }

            return indices;
        }

        private List<TabItem> CollectSettingsTabItems()
        {
            if (SettingsTabControl == null)
                return [];

            var tabs = new List<TabItem>();
            foreach (var item in SettingsTabControl.Items)
            {
                if (item is not TabItem tab)
                    continue;

                tab.Focusable = true;
                if (tab.IsVisible && tab.IsEnabled)
                    tabs.Add(tab);
            }

            return tabs;
        }

        private List<Control> CollectSettingsFocusableControls()
        {
            if (SettingsPanel == null)
                return [];

            var controls = new List<Control>();
            controls.AddRange(CollectSettingsTabItems());

            // Close sits outside tab content; include it explicitly.
            if (CloseSettingsButton is { IsEffectivelyVisible: true, IsEnabled: true, Focusable: true })
                controls.Add(CloseSettingsButton);

            // Only walk the selected tab page. Walking the whole SettingsPanel also
            // picks up ScrollViewer/TabControl RepeatButtons above the first option,
            // which made Up from "Close After Launch" take two presses to reach tabs.
            var contentRoot = SettingsTabControl?.SelectedItem is TabItem { Content: Control page }
                ? page
                : null;
            if (contentRoot == null)
                return controls;

            foreach (var control in contentRoot.GetVisualDescendants().OfType<Control>())
            {
                if (control is RepeatButton)
                    continue;

                if (!control.IsEffectivelyVisible || !control.IsEnabled || !control.Focusable)
                    continue;

                if (control is Button or CheckBox or TextBox or Slider or ComboBox)
                    controls.Add(control);
            }

            return controls;
        }

        private Control? GetSettingsFocusedControl(IReadOnlyList<Control> controls)
        {
            if (_settingsGamepadFocusIndex < 0 || _settingsGamepadFocusIndex >= controls.Count)
                return null;

            return controls[_settingsGamepadFocusIndex];
        }

        private void ApplySettingsGamepadSelection(int index)
        {
            var controls = CollectSettingsFocusableControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _settingsGamepadFocusIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;

            ClearSettingsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ActivateSettingsGamepadSelection()
        {
            var controls = CollectSettingsFocusableControls();
            var index = _gamepadNavigation.ClampIndex(_settingsGamepadFocusIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            var control = controls[index];
            if (control is TabItem tabItem)
            {
                var tabs = CollectSettingsTabItems();
                var tabIndex = tabs.IndexOf(tabItem);
                if (tabIndex >= 0 && SettingsTabControl != null)
                    SettingsTabControl.SelectedIndex = tabIndex;

                // Move into the first content control below the tab strip.
                var refreshed = CollectSettingsFocusableControls();
                var firstContentIndex = FindFirstSettingsContentIndex(refreshed);
                if (firstContentIndex >= 0)
                    ApplySettingsGamepadSelection(firstContentIndex);
                else
                    ApplySettingsGamepadSelection(Math.Max(0, tabIndex));
                return;
            }

            if (control is CheckBox checkBox)
            {
                checkBox.IsChecked = !checkBox.IsChecked;
                // Layout-changing App Cards checkboxes rebuild the library underneath;
                // re-assert settings focus so keyboard focus does not land on a game card.
                ApplySettingsGamepadSelection(index);
            }
            else if (control is Button button)
            {
                GamepadControlActivation.ActivateButton(button);
                if (isSettingsPanelOpen)
                    ApplySettingsGamepadSelection(index);
            }
            else if (control is ComboBox comboBox)
                GamepadComboBoxNavigation.Open(comboBox);
            else if (control is TextBox textBox)
                GamepadControlActivation.ActivateTextBox(textBox);
            else
                control.Focus();
        }

        private static void ClearSettingsGamepadFocusClasses(IReadOnlyList<Control> controls)
        {
            foreach (var control in controls)
            {
                if (control is StyledElement styled)
                    styled.Classes.Set("gamepad-focused", false);
            }
        }

        private GamepadNavigationZone GetMainContentGamepadZone()
        {
            if (_mainViewMode == MainViewMode.Library && _isModsOverlayOpen)
                return GamepadNavigationZone.ModsOverlayList;

            if (_mainViewMode == MainViewMode.Library && _isAppUpdatesReviewOpen)
                return GamepadNavigationZone.AppUpdatesReviewList;

            if (_mainViewMode == MainViewMode.Library)
                return GamepadNavigationZone.Library;

            if (_appCatalogSubView == AppCatalogSubView.Review)
                return GamepadNavigationZone.CatalogReviewList;

            return GamepadNavigationZone.CatalogSources;
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

        private bool TryApplyGamepadZoneTransition(GamepadZoneTransition transition)
        {
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.Sidebar &&
                transition.Zone != GamepadNavigationZone.Sidebar)
            {
                ClearSidebarGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.TopBar &&
                transition.Zone != GamepadNavigationZone.TopBar)
            {
                ClearTopBarGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.AnnouncementBanner &&
                transition.Zone != GamepadNavigationZone.AnnouncementBanner)
            {
                ClearAnnouncementBannerGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesToolbar &&
                transition.Zone != GamepadNavigationZone.CatalogSourcesToolbar)
            {
                ClearCatalogSourcesToolbarGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesFilters &&
                transition.Zone != GamepadNavigationZone.CatalogSourcesFilters)
            {
                ClearCatalogSourcesFiltersGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourceCardActions &&
                transition.Zone != GamepadNavigationZone.CatalogSourceCardActions)
            {
                ClearCatalogSourceCardActionsGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions &&
                transition.Zone != GamepadNavigationZone.CatalogReviewRowActions)
            {
                ClearCatalogReviewRowActionsGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewFilters &&
                transition.Zone != GamepadNavigationZone.CatalogReviewFilters)
            {
                ClearCatalogReviewFilterGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewToolbar &&
                transition.Zone != GamepadNavigationZone.AppUpdatesReviewToolbar)
            {
                ClearAppUpdatesReviewToolbarGamepadFocus();
            }

            if (_gamepadNavigation.ActiveZone is GamepadNavigationZone.ModsOverlayToolbar
                or GamepadNavigationZone.ModsOverlayFilters
                or GamepadNavigationZone.ModsOverlaySourceFilters
                or GamepadNavigationZone.ModsOverlayList
                or GamepadNavigationZone.ModsOverlayRowActions)
            {
                if (transition.Zone is not (GamepadNavigationZone.ModsOverlayToolbar
                    or GamepadNavigationZone.ModsOverlayFilters
                    or GamepadNavigationZone.ModsOverlaySourceFilters
                    or GamepadNavigationZone.ModsOverlayList
                    or GamepadNavigationZone.ModsOverlayRowActions))
                {
                    ClearModsGamepadFocus();
                }
            }

            switch (transition.Zone)
            {
                case GamepadNavigationZone.Sidebar:
                    ClearGamepadFocus();
                    _gamepadNavigation.ActiveZone = GamepadNavigationZone.Sidebar;
                    _gamepadNavigation.LibrarySelectedIndex = -1;
                    _gamepadNavigation.CatalogSelectedIndex = -1;
                    _gamepadNavigation.CatalogReviewSelectedIndex = -1;
                    ApplySidebarGamepadSelection(_gamepadNavigation.SidebarSelectedIndex < 0 ? 0 : _gamepadNavigation.SidebarSelectedIndex);
                    return true;
                case GamepadNavigationZone.TopBar:
                    // Coming up from content: stop on the banner first when it is visible.
                    if (IsAnnouncementBannerVisible &&
                        _gamepadNavigation.ActiveZone is not (GamepadNavigationZone.TopBar
                            or GamepadNavigationZone.AnnouncementBanner))
                    {
                        ApplyAnnouncementBannerGamepadSelection();
                        return true;
                    }

                    ClearGamepadFocus();
                    _gamepadNavigation.ActiveZone = GamepadNavigationZone.TopBar;
                    _gamepadNavigation.LibrarySelectedIndex = -1;
                    _gamepadNavigation.CatalogReviewSelectedIndex = -1;
                    ApplyTopBarGamepadSelection(_gamepadNavigation.TopBarSelectedIndex < 0 ? 0 : _gamepadNavigation.TopBarSelectedIndex);
                    return true;
                case GamepadNavigationZone.AnnouncementBanner:
                    ApplyAnnouncementBannerGamepadSelection();
                    return true;
                case GamepadNavigationZone.Library:
                    ApplyLibraryGamepadSelection(transition.SelectedIndex ?? 0);
                    return true;
                case GamepadNavigationZone.CatalogSources:
                    if (CatalogSources.Count == 0)
                        ApplyCatalogSourcesToolbarSelection(transition.SelectedIndex ?? 0);
                    else
                        ApplyCatalogGamepadSelection(transition.SelectedIndex ?? 0);
                    return true;
                case GamepadNavigationZone.CatalogSourcesToolbar:
                    ClearGamepadFocus();
                    _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesToolbar;
                    ApplyCatalogSourcesToolbarSelection(
                        _gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
                    return true;
                case GamepadNavigationZone.CatalogSourcesFilters:
                    ClearGamepadFocus();
                    _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesFilters;
                    ApplyCatalogSourcesFilterSelection(
                        _gamepadNavigation.CatalogSourcesFilterIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogSourcesFilterIndex);
                    return true;
                case GamepadNavigationZone.CatalogSourceCardActions:
                    ApplyCatalogSourceCardActionSelection(
                        transition.SelectedIndex ??
                        (_gamepadNavigation.CatalogSourceCardActionIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogSourceCardActionIndex));
                    return true;
                case GamepadNavigationZone.CatalogReviewRowActions:
                    ApplyCatalogReviewRowActionSelection(
                        transition.SelectedIndex ??
                        (_gamepadNavigation.CatalogReviewRowActionIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogReviewRowActionIndex));
                    return true;
                case GamepadNavigationZone.CatalogReviewFilters:
                    ApplyCatalogReviewFilterSelection(
                        transition.SelectedIndex ?? GetCatalogReviewFilterIndexFromList());
                    return true;
                case GamepadNavigationZone.CatalogReviewList:
                    if (CatalogSyncRows.Count == 0)
                    {
                        var emptyActions = CollectCatalogReviewEmptyActionControls();
                        if (emptyActions.Count > 0)
                        {
                            ApplyCatalogReviewEmptyActionSelection(transition.SelectedIndex ?? 0);
                            return true;
                        }

                        return NavigateToCatalogReviewFiltersFromList() || true;
                    }

                    ApplyCatalogReviewRowSelection(transition.SelectedIndex ?? 0);
                    return true;
                case GamepadNavigationZone.AppUpdatesReviewToolbar:
                    ApplyAppUpdatesReviewToolbarSelection(transition.SelectedIndex ?? 0);
                    return true;
                case GamepadNavigationZone.AppUpdatesReviewList:
                    if (AppUpdateReviewRows.Count == 0)
                    {
                        ApplyAppUpdatesReviewToolbarSelection(0);
                        return true;
                    }

                    ApplyAppUpdatesReviewRowSelection(transition.SelectedIndex ?? 0);
                    return true;
                case GamepadNavigationZone.AppUpdatesReviewRowActions:
                    ApplyAppUpdatesReviewRowActionSelection(
                        transition.SelectedIndex ??
                        (_gamepadNavigation.AppUpdatesReviewRowActionIndex < 0
                            ? 0
                            : _gamepadNavigation.AppUpdatesReviewRowActionIndex));
                    return true;
                case GamepadNavigationZone.ModsOverlayToolbar:
                    ApplyModsToolbarSelection(transition.SelectedIndex ?? Math.Max(0, _modsGamepadToolbarIndex));
                    return true;
                case GamepadNavigationZone.ModsOverlayFilters:
                    ApplyModsFiltersSelection(transition.SelectedIndex ?? Math.Max(0, _modsGamepadFilterIndex));
                    return true;
                case GamepadNavigationZone.ModsOverlaySourceFilters:
                    if (CollectModsSourceFilterControls().Count == 0)
                    {
                        // Down from filters passes SelectedIndex; Up from list passes null.
                        if (transition.SelectedIndex.HasValue && ModListRows.Count > 0)
                            ApplyModsListSelection(transition.SelectedIndex.Value);
                        else
                            ApplyModsFiltersSelection(Math.Max(0, _modsGamepadFilterIndex));
                        return true;
                    }

                    ApplyModsSourceFiltersSelection(
                        transition.SelectedIndex ?? Math.Max(0, _modsGamepadSourceFilterIndex));
                    return true;
                case GamepadNavigationZone.ModsOverlayList:
                    if (ModListRows.Count == 0)
                    {
                        if (CollectModsSourceFilterControls().Count > 0)
                            ApplyModsSourceFiltersSelection(Math.Max(0, _modsGamepadSourceFilterIndex));
                        else
                            ApplyModsFiltersSelection(Math.Max(0, _modsGamepadFilterIndex));
                        return true;
                    }

                    ApplyModsListSelection(transition.SelectedIndex ?? 0);
                    return true;
                case GamepadNavigationZone.ModsOverlayRowActions:
                    ApplyModsRowActionSelection(
                        transition.SelectedIndex ?? Math.Max(0, _modsGamepadRowActionIndex));
                    return true;
                default:
                    return false;
            }
        }

        private bool HandleLibraryGamepadNavigation(Services.NavigationDirection direction)
        {
            var games = Games.ToList();
            var isListLayout = IsListGamepadLayout();
            var positions = games.Count > 0 && !isListLayout ? CollectGameCardPositions(games) : null;
            var currentIndex = _gamepadNavigation.LibrarySelectedIndex;

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                _gamepadNavigation.ActiveZone,
                GetMainContentGamepadZone(),
                isListLayout,
                positions,
                currentIndex,
                games.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.Library)
                return false;

            if (games.Count == 0)
                return false;

            var nextIndex = _gamepadNavigation.MoveLibraryIndex(
                currentIndex,
                direction,
                games.Count,
                isListLayout,
                positions);

            if (nextIndex == currentIndex &&
                direction is Services.NavigationDirection.Left or Services.NavigationDirection.Up)
            {
                var blockedTransition = _gamepadNavigation.TryGetBlockedMoveZoneTransition(
                    direction,
                    _gamepadNavigation.ActiveZone,
                    GetMainContentGamepadZone(),
                    isListLayout,
                    currentIndex,
                    games.Count);

                if (blockedTransition.HasValue)
                    return TryApplyGamepadZoneTransition(blockedTransition.Value);
            }

            ApplyLibraryGamepadSelection(nextIndex);
            return true;
        }

        private bool HandleCatalogSourcesCardNavigation(Services.NavigationDirection direction)
        {
            var currentIndex = _gamepadNavigation.CatalogSelectedIndex;

            if (CatalogSources.Count > 0)
            {
                var positions = CollectCatalogCardPositions();

                var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                    direction,
                    _gamepadNavigation.ActiveZone,
                    GetMainContentGamepadZone(),
                    isListLayout: false,
                    positions,
                    currentIndex,
                    CatalogSources.Count);

                if (zoneTransition.HasValue)
                    return TryApplyGamepadZoneTransition(zoneTransition.Value);

                if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.CatalogSources)
                    return false;

                var nextIndex = _gamepadNavigation.MoveCatalogIndex(
                    currentIndex,
                    direction,
                    CatalogSources.Count,
                    positions);

                if (nextIndex == currentIndex &&
                    direction is Services.NavigationDirection.Left or Services.NavigationDirection.Up)
                {
                    var blockedTransition = _gamepadNavigation.TryGetBlockedMoveZoneTransition(
                        direction,
                        _gamepadNavigation.ActiveZone,
                        GetMainContentGamepadZone(),
                        isListLayout: false,
                        currentIndex,
                        CatalogSources.Count);

                    if (blockedTransition.HasValue)
                        return TryApplyGamepadZoneTransition(blockedTransition.Value);
                }

                ApplyCatalogGamepadSelection(nextIndex);
                return true;
            }

            var emptyTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                _gamepadNavigation.ActiveZone,
                GetMainContentGamepadZone(),
                isListLayout: false,
                positions: null,
                currentIndex,
                itemCount: 0);

            if (emptyTransition.HasValue)
                return TryApplyGamepadZoneTransition(emptyTransition.Value);

            return false;
        }

        private bool HandleCatalogSourcesToolbarNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectCatalogSourcesToolbarControls();
            if (controls.Count == 0)
                return false;

            var currentIndex = _gamepadNavigation.CatalogSourcesToolbarSelectedIndex;

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.CatalogSourcesToolbar,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex,
                controls.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
                return false;

            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
            ApplyCatalogSourcesToolbarSelection(nextIndex);
            return true;
        }

        private bool HandleCatalogSourcesFiltersNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectCatalogSourcesFilterControls();
            if (controls.Count == 0)
                return false;

            var currentIndex = _gamepadNavigation.CatalogSourcesFilterIndex;

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.CatalogSourcesFilters,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex,
                CatalogSources.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
                return false;

            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
            ApplyCatalogSourcesFilterSelection(nextIndex);
            return true;
        }

        private bool HandleCatalogSourceCardActionsNavigation(Services.NavigationDirection direction)
        {
            if (CatalogSources.Count == 0)
            {
                ApplyCatalogSourcesFilterSelection(
                    _gamepadNavigation.CatalogSourcesFilterIndex < 0
                        ? 0
                        : _gamepadNavigation.CatalogSourcesFilterIndex);
                return true;
            }

            var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
            if (cardIndex < 0 || cardIndex >= CatalogSources.Count)
                return false;

            var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
            if (controls.Count == 0)
                return false;

            var currentIndex = _gamepadNavigation.CatalogSourceCardActionIndex;

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.CatalogSourceCardActions,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex,
                controls.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
                return false;

            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
            ApplyCatalogSourceCardActionSelection(nextIndex);
            return true;
        }

        private bool HandleCatalogReviewGamepadNavigation(Services.NavigationDirection direction)
        {
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewFilters)
                return HandleCatalogReviewFiltersGamepadNavigation(direction);

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions)
                return HandleCatalogReviewRowActionsNavigation(direction);

            // Needs-review complete (or other empty filter): navigate the Back buttons.
            if (CatalogSyncRows.Count == 0)
                return HandleCatalogReviewEmptyActionsNavigation(direction);

            var rows = CatalogSyncRows.ToList();
            var currentIndex = _gamepadNavigation.CatalogReviewSelectedIndex;

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                _gamepadNavigation.ActiveZone,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex,
                rows.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.CatalogReviewList)
                return false;

            var nextIndex = _gamepadNavigation.MoveListIndex(currentIndex, direction, rows.Count, wrap: false);
            ApplyCatalogReviewRowSelection(nextIndex);
            return true;
        }

        private bool HandleCatalogReviewEmptyActionsNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectCatalogReviewEmptyActionControls();
            if (controls.Count == 0)
            {
                // Nothing to focus in the body — Up returns to the filter strip (tags if present).
                if (direction == Services.NavigationDirection.Up)
                    return NavigateToCatalogReviewFiltersFromList() || true;

                if (direction == Services.NavigationDirection.Left)
                    return TryApplyGamepadZoneTransition(
                        new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));

                return true;
            }

            var currentIndex = _gamepadNavigation.CatalogReviewSelectedIndex;

            if (direction == Services.NavigationDirection.Up)
                return NavigateToCatalogReviewFiltersFromList() || true;

            if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
            {
                return TryApplyGamepadZoneTransition(
                    new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
            }

            if (direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
            {
                var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
                ApplyCatalogReviewEmptyActionSelection(nextIndex);
                return true;
            }

            // Down stays on the empty actions.
            return true;
        }

        private bool HandleCatalogReviewRowActionsNavigation(Services.NavigationDirection direction)
        {
            var rows = CatalogSyncRows.ToList();
            if (rows.Count == 0)
                return false;

            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return false;

            var controls = CollectCatalogReviewRowActionControls(rows[rowIndex]);
            if (controls.Count == 0)
                return false;

            var currentIndex = _gamepadNavigation.ClampIndex(
                _gamepadNavigation.CatalogReviewRowActionIndex,
                controls.Count);

            // Leave the action strip back to the row list.
            if (direction is Services.NavigationDirection.Up or Services.NavigationDirection.Down)
            {
                ClearCatalogReviewRowActionsGamepadFocus();
                var nextRow = _gamepadNavigation.MoveListIndex(rowIndex, direction, rows.Count, wrap: false);
                ApplyCatalogReviewRowSelection(nextRow);
                return true;
            }

            // Left from the first action returns to the row (do not wrap to the far-right button).
            if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
            {
                ClearCatalogReviewRowActionsGamepadFocus();
                ApplyCatalogReviewRowSelection(rowIndex);
                return true;
            }

            if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
                return false;

            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
            ApplyCatalogReviewRowActionSelection(nextIndex);
            return true;
        }

        private bool HandleCatalogReviewFiltersGamepadNavigation(Services.NavigationDirection direction)
        {
            var ranges = GetCatalogReviewFilterRanges();
            var controls = CollectCatalogReviewFilterControls();
            if (controls.Count == 0 || ranges.Total <= 0)
                return false;

            var currentIndex = _gamepadNavigation.ClampIndex(
                _gamepadNavigation.CatalogReviewFilterIndex,
                controls.Count);
            var row = ranges.ResolveRow(currentIndex);
            var localIndex = ranges.LocalIndex(currentIndex);

            if (direction == Services.NavigationDirection.Up)
            {
                if (row == CatalogReviewFilterGamepadLayout.Row.Tags && ranges.HasStatus)
                {
                    ApplyCatalogReviewFilterSelection(ranges.StatusStart);
                    return true;
                }

                if ((row == CatalogReviewFilterGamepadLayout.Row.Tags ||
                     row == CatalogReviewFilterGamepadLayout.Row.Status) &&
                    ranges.HasBulk)
                {
                    ApplyCatalogReviewFilterSelection(ranges.BulkStart);
                    return true;
                }

                return TryApplyGamepadZoneTransition(
                    new GamepadZoneTransition(GamepadNavigationZone.TopBar, null));
            }

            if (direction == Services.NavigationDirection.Down)
            {
                if (row == CatalogReviewFilterGamepadLayout.Row.Bulk && ranges.HasStatus)
                {
                    ApplyCatalogReviewFilterSelection(ranges.StatusStart);
                    return true;
                }

                if (row == CatalogReviewFilterGamepadLayout.Row.Bulk && ranges.HasTags)
                {
                    ApplyCatalogReviewFilterSelection(ranges.TagStart);
                    return true;
                }

                if (row == CatalogReviewFilterGamepadLayout.Row.Status && ranges.HasTags)
                {
                    ApplyCatalogReviewFilterSelection(ranges.TagStart);
                    return true;
                }

                if (CatalogSyncRows.Count == 0)
                {
                    var emptyActions = CollectCatalogReviewEmptyActionControls();
                    if (emptyActions.Count > 0)
                    {
                        ApplyCatalogReviewEmptyActionSelection(0);
                        return true;
                    }

                    return true;
                }

                return TryApplyGamepadZoneTransition(
                    new GamepadZoneTransition(GamepadNavigationZone.CatalogReviewList, 0));
            }

            if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
                return true;

            var rowCount = ranges.RowCount(row);
            if (rowCount <= 0)
                return true;

            if (CatalogReviewFilterGamepadLayout.ShouldLeaveToSidebarOnLeft(localIndex, direction))
            {
                return TryApplyGamepadZoneTransition(
                    new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
            }

            int nextLocal;
            if (row == CatalogReviewFilterGamepadLayout.Row.Tags)
            {
                // Tag chips clamp at ends (no wrap) so Right stops on the last tag.
                nextLocal = CatalogReviewFilterGamepadLayout.MoveHorizontalClamped(
                    localIndex, direction, rowCount);
            }
            else
            {
                nextLocal = _gamepadNavigation.MoveHorizontalIndex(localIndex, direction, rowCount);
            }

            ApplyCatalogReviewFilterSelection(ranges.AbsoluteIndex(row, nextLocal));
            return true;
        }

        private bool NavigateToCatalogReviewFiltersFromList()
        {
            var index = GetCatalogReviewFilterIndexFromList();
            if (index < 0)
                return false;

            ApplyCatalogReviewFilterSelection(index);
            return true;
        }

        private bool HandleAppUpdatesReviewGamepadNavigation(Services.NavigationDirection direction)
        {
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewToolbar)
                return HandleAppUpdatesReviewToolbarNavigation(direction);

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewRowActions)
                return HandleAppUpdatesReviewRowActionsNavigation(direction);

            var rows = AppUpdateReviewRows.ToList();
            var currentIndex = _gamepadNavigation.AppUpdatesReviewSelectedIndex;

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.AppUpdatesReviewList,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex,
                rows.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (rows.Count == 0)
                return true;

            if (direction == Services.NavigationDirection.Right)
            {
                ApplyAppUpdatesReviewRowActionSelection(0);
                return true;
            }

            var nextIndex = _gamepadNavigation.MoveListIndex(currentIndex, direction, rows.Count, wrap: false);
            ApplyAppUpdatesReviewRowSelection(nextIndex);
            return true;
        }

        private bool HandleAppUpdatesReviewToolbarNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectAppUpdatesReviewToolbarControls();
            if (controls.Count == 0)
                return false;

            var currentIndex = _gamepadNavigation.AppUpdatesReviewToolbarIndex;
            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.AppUpdatesReviewToolbar,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex,
                controls.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
                return true;

            if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
            {
                return TryApplyGamepadZoneTransition(
                    new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
            }

            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
            ApplyAppUpdatesReviewToolbarSelection(nextIndex);
            return true;
        }

        private bool HandleAppUpdatesReviewRowActionsNavigation(Services.NavigationDirection direction)
        {
            var rows = AppUpdateReviewRows.ToList();
            if (rows.Count == 0)
                return false;

            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.AppUpdatesReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return false;

            var controls = CollectAppUpdatesReviewRowActionControls(rows[rowIndex]);
            if (controls.Count == 0)
                return false;

            var currentIndex = _gamepadNavigation.AppUpdatesReviewRowActionIndex;

            if (direction is Services.NavigationDirection.Up or Services.NavigationDirection.Down)
            {
                ClearAppUpdatesReviewRowActionsGamepadFocus();
                var nextRow = _gamepadNavigation.MoveListIndex(rowIndex, direction, rows.Count, wrap: false);
                ApplyAppUpdatesReviewRowSelection(nextRow);
                return true;
            }

            if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
            {
                ClearAppUpdatesReviewRowActionsGamepadFocus();
                ApplyAppUpdatesReviewRowSelection(rowIndex);
                return true;
            }

            if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
                return false;

            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
            ApplyAppUpdatesReviewRowActionSelection(nextIndex);
            return true;
        }

        private List<Control> CollectAppUpdatesReviewToolbarControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            Add(this.FindControl<Button>("AppUpdatesUpdateAllButton"));
            Add(this.FindControl<Button>("AppUpdatesSkipAllButton"));
            Add(this.FindControl<Button>("AppUpdatesBackToLibraryButton"));
            return controls;
        }

        private List<Control> CollectAppUpdatesReviewRowActionControls(GameInfo game)
        {
            var controls = new List<Control>();
            var itemsControl = this.FindControl<ItemsControl>("AppUpdatesReviewItemsControl");
            if (itemsControl == null)
                return controls;

            foreach (var button in itemsControl.GetVisualDescendants().OfType<Button>())
            {
                if (!ReferenceEquals(button.DataContext, game))
                    continue;
                if (!button.IsVisible || !button.IsEnabled)
                    continue;
                controls.Add(button);
            }

            return controls;
        }

        private void ApplyAppUpdatesReviewToolbarSelection(int index)
        {
            var controls = CollectAppUpdatesReviewToolbarControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.AppUpdatesReviewToolbarIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.AppUpdatesReviewToolbar;

            ClearGamepadFocus();
            ClearAppUpdatesReviewRowFocus();
            ClearStyledControlsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
        }

        private void ApplyAppUpdatesReviewRowSelection(int index)
        {
            var rows = AppUpdateReviewRows.ToList();
            index = _gamepadNavigation.ClampIndex(index, rows.Count);
            _gamepadNavigation.AppUpdatesReviewSelectedIndex = index;
            _gamepadNavigation.AppUpdatesReviewRowActionIndex = -1;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.AppUpdatesReviewList;

            ClearAppUpdatesReviewToolbarGamepadFocus();
            ClearAppUpdatesReviewRowActionsGamepadFocus();
            ClearAppUpdatesReviewRowFocus();
            ClearGamepadFocus();
            if (index < 0 || index >= rows.Count)
                return;

            rows[index].IsGamepadFocused = true;
            Dispatcher.UIThread.Post(
                () => FindAppUpdateReviewRowBorder(rows[index])?.BringIntoView(),
                DispatcherPriority.Loaded);
        }

        private void ApplyAppUpdatesReviewRowActionSelection(int index)
        {
            var rows = AppUpdateReviewRows.ToList();
            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.AppUpdatesReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return;

            var controls = CollectAppUpdatesReviewRowActionControls(rows[rowIndex]);
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.AppUpdatesReviewRowActionIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.AppUpdatesReviewRowActions;

            ClearAppUpdatesReviewToolbarGamepadFocus();
            ClearStyledControlsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
        }

        private void ClearAppUpdatesReviewToolbarGamepadFocus()
        {
            var controls = CollectAppUpdatesReviewToolbarControls();
            ClearStyledControlsGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private void ClearAppUpdatesReviewRowActionsGamepadFocus()
        {
            var rows = AppUpdateReviewRows.ToList();
            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.AppUpdatesReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return;

            ClearStyledControlsGamepadFocusClasses(CollectAppUpdatesReviewRowActionControls(rows[rowIndex]));
        }

        private void ClearAppUpdatesReviewRowFocus()
        {
            foreach (var game in AppUpdateReviewRows)
                game.IsGamepadFocused = false;
        }

        private Border? FindAppUpdateReviewRowBorder(GameInfo game)
        {
            var itemsControl = this.FindControl<ItemsControl>("AppUpdatesReviewItemsControl");
            return itemsControl?.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => ReferenceEquals(b.DataContext, game));
        }

        private void SelectInitialAppUpdatesReviewGamepadItem()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (AppUpdateReviewRows.Count == 0)
            {
                ApplyAppUpdatesReviewToolbarSelection(0);
                return;
            }

            ApplyAppUpdatesReviewRowSelection(0);
        }

        private void ActivateAppUpdatesReviewToolbarSelection()
        {
            var controls = CollectAppUpdatesReviewToolbarControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.AppUpdatesReviewToolbarIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void ActivateAppUpdatesReviewRowSelection()
        {
            var rows = AppUpdateReviewRows.ToList();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.AppUpdatesReviewSelectedIndex, rows.Count);
            if (index < 0 || index >= rows.Count)
                return;

            var controls = CollectAppUpdatesReviewRowActionControls(rows[index]);
            if (controls.Count == 0)
                return;

            ApplyAppUpdatesReviewRowActionSelection(0);
        }

        private void ActivateAppUpdatesReviewRowActionSelection()
        {
            var rows = AppUpdateReviewRows.ToList();
            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.AppUpdatesReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return;

            var controls = CollectAppUpdatesReviewRowActionControls(rows[rowIndex]);
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.AppUpdatesReviewRowActionIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private bool HandleSidebarGamepadNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectSidebarFocusableControls();
            if (controls.Count == 0)
                return false;

            var currentIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.SidebarSelectedIndex, controls.Count);
            var footerStartIndex = FindSidebarFooterStartIndex(controls);

            // Footer icons are a horizontal strip: Left/Right only between them.
            if (footerStartIndex >= 0 && currentIndex >= footerStartIndex)
            {
                var footerCount = controls.Count - footerStartIndex;
                var footerLocalIndex = currentIndex - footerStartIndex;

                if (direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
                {
                    // Right from the last footer button leaves the sidebar.
                    if (direction == Services.NavigationDirection.Right && footerLocalIndex >= footerCount - 1)
                    {
                        var leaveSidebar = _gamepadNavigation.TryGetZoneTransition(
                            direction,
                            GamepadNavigationZone.Sidebar,
                            GetMainContentGamepadZone(),
                            isListLayout: true,
                            positions: null,
                            currentIndex,
                            controls.Count);
                        if (leaveSidebar.HasValue)
                            return TryApplyGamepadZoneTransition(leaveSidebar.Value);
                        return true;
                    }

                    var nextLocal = _gamepadNavigation.MoveHorizontalIndex(footerLocalIndex, direction, footerCount);
                    ApplySidebarGamepadSelection(footerStartIndex + nextLocal);
                    return true;
                }

                if (direction == Services.NavigationDirection.Up)
                {
                    if (footerStartIndex > 0)
                        ApplySidebarGamepadSelection(footerStartIndex - 1);
                    return true;
                }

                if (direction == Services.NavigationDirection.Down)
                {
                    // Stay on the current footer icon; wrap from the last one to the top of the sidebar.
                    if (currentIndex >= controls.Count - 1 && footerStartIndex > 0)
                        ApplySidebarGamepadSelection(0);
                    return true;
                }

                return true;
            }

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.Sidebar,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex,
                controls.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (direction is not (Services.NavigationDirection.Up or Services.NavigationDirection.Down))
                return true;

            // Enter the footer strip on its first button (GitHub), not Discord.
            if (direction == Services.NavigationDirection.Down &&
                footerStartIndex >= 0 &&
                currentIndex == footerStartIndex - 1)
            {
                ApplySidebarGamepadSelection(footerStartIndex);
                return true;
            }

            var nextIndex = _gamepadNavigation.MoveListIndex(currentIndex, direction, controls.Count);

            // Wrapping Up from the first sidebar item lands on the last control (Discord).
            // Prefer the start of the footer strip so Up/Down never step GitHub ↔ Discord.
            if (direction == Services.NavigationDirection.Up &&
                currentIndex <= 0 &&
                footerStartIndex >= 0 &&
                nextIndex >= footerStartIndex)
            {
                nextIndex = footerStartIndex;
            }

            ApplySidebarGamepadSelection(nextIndex);
            return true;
        }

        private int FindSidebarFooterStartIndex(IReadOnlyList<Control> controls)
        {
            for (var i = 0; i < controls.Count; i++)
            {
                if (ReferenceEquals(controls[i], GitHubFooterButton) ||
                    ReferenceEquals(controls[i], DiscordFooterButton))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool HandleTopBarGamepadNavigation(Services.NavigationDirection direction)
        {
            var controls = CollectTopBarControls();
            if (controls.Count == 0)
                return false;

            if (direction == Services.NavigationDirection.Down && IsAnnouncementBannerVisible)
            {
                ClearTopBarGamepadFocus();
                ApplyAnnouncementBannerGamepadSelection();
                return true;
            }

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.TopBar,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                _gamepadNavigation.TopBarSelectedIndex,
                controls.Count);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            if (direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
            {
                var nextIndex = _gamepadNavigation.MoveHorizontalIndex(
                    _gamepadNavigation.TopBarSelectedIndex,
                    direction,
                    controls.Count);

                ApplyTopBarGamepadSelection(nextIndex);
                return true;
            }

            // Consume Up (and any other non-transition direction) so InputService's
            // Avalonia focus walk does not move keyboard focus onto Sort By while
            // the gamepad selection ring stays on Check for Updates / Settings / etc.
            return true;
        }

        private bool HandleAnnouncementBannerGamepadNavigation(Services.NavigationDirection direction)
        {
            if (!IsAnnouncementBannerVisible)
            {
                return TryApplyGamepadZoneTransition(
                    new GamepadZoneTransition(GetMainContentGamepadZone(), 0));
            }

            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(
                direction,
                GamepadNavigationZone.AnnouncementBanner,
                GetMainContentGamepadZone(),
                isListLayout: true,
                positions: null,
                currentIndex: 0,
                itemCount: 1);

            if (zoneTransition.HasValue)
                return TryApplyGamepadZoneTransition(zoneTransition.Value);

            return true;
        }

        private bool IsAnnouncementBannerVisible =>
            AnnouncementBanner is { IsVisible: true } &&
            AnnouncementBannerCloseButton is { IsVisible: true, IsEnabled: true };

        private void ApplyAnnouncementBannerGamepadSelection()
        {
            if (!IsAnnouncementBannerVisible)
                return;

            ClearGamepadFocus();
            ClearAnnouncementBannerGamepadFocus();
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.AnnouncementBanner;

            if (AnnouncementBannerCloseButton is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            GamepadControlActivation.ApplyGamepadHighlightFocus(AnnouncementBannerCloseButton!);
        }

        private void ClearAnnouncementBannerGamepadFocus()
        {
            if (AnnouncementBannerCloseButton is StyledElement styled)
                styled.Classes.Set("gamepad-focused", false);

            if (AnnouncementBannerCloseButton != null)
                ClearFocusIfOnControls([AnnouncementBannerCloseButton]);
        }

        private List<Control> CollectSidebarFocusableControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            Add(ContinueButton);
            Add(LibraryNavButton);
            Add(AppCatalogNavButton);

            if (_mainViewMode == MainViewMode.Library && LibraryFiltersPanel is { IsVisible: true })
            {
                Add(UnhideAllGamesButton);
                Add(HideNonInstalledButton);
                Add(ShowHiddenGamesButton);

                if (TagDisplayFiltersItemsControl != null)
                {
                    foreach (var button in TagDisplayFiltersItemsControl.GetVisualDescendants().OfType<Button>()
                                 .Where(b => b.Classes.Contains("display-filter-row") && b.IsVisible && b.IsEnabled))
                    {
                        controls.Add(button);
                    }
                }

                var addDisplayFilterButton = LibraryFiltersPanel.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b =>
                        b.IsVisible &&
                        b.IsEnabled &&
                        b.Content is string content &&
                        content == "Add Display Filter");

                Add(addDisplayFilterButton);
            }

            Add(GitHubFooterButton);
            Add(DiscordFooterButton);
            return controls;
        }

        private List<Control> CollectTopBarControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            if (_mainViewMode == MainViewMode.Library && !_isAppUpdatesReviewOpen && !_isModsOverlayOpen)
            {
                Add(AddNewEntryButton);
                Add(SortByComboBox);
            }
            else if (_mainViewMode == MainViewMode.AppCatalog && _appCatalogSubView == AppCatalogSubView.Review)
            {
                Add(CatalogReviewSortByComboBox);
                Add(CatalogReviewBackButton);
            }

            Add(CheckForUpdatesButton);
            Add(SettingsButton);
            Add(MinimizeButton);
            Add(ToggleFullscreenButton);
            Add(CloseLauncherButton);
            return controls;
        }

        private List<Control> CollectCatalogReviewFilterChipControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            Add(CatalogFilterAllButton);
            Add(CatalogFilterNeedsReviewButton);
            Add(CatalogFilterNewButton);
            Add(CatalogFilterNotInLibraryButton);
            Add(CatalogFilterChangedButton);
            Add(CatalogFilterUpToDateButton);
            Add(CatalogFilterHiddenButton);
            Add(CatalogSearchTextBox);
            return controls;
        }

        private List<Control> CollectCatalogReviewTagChipControls()
        {
            var controls = new List<Control>();

            var itemsControl = this.FindControl<ItemsControl>("CatalogTagChipsItemsControl");
            if (itemsControl == null || !itemsControl.IsVisible)
                return controls;

            foreach (var button in itemsControl.GetVisualDescendants().OfType<Button>())
            {
                if (button.IsVisible && button.IsEnabled)
                    controls.Add(button);
            }

            return controls;
        }

        private List<Control> CollectCatalogReviewBulkActionControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            Add(CatalogSyncAddAllButton);
            Add(CatalogSyncReplaceAllButton);
            Add(CatalogSyncAcknowledgeButton);
            return controls;
        }

        private CatalogReviewFilterGamepadLayout.Ranges GetCatalogReviewFilterRanges() =>
            CatalogReviewFilterGamepadLayout.FromCounts(
                CollectCatalogReviewFilterChipControls().Count,
                CollectCatalogReviewTagChipControls().Count,
                CollectCatalogReviewBulkActionControls().Count);

        private List<Control> CollectCatalogReviewFilterControls()
        {
            // Status chips, then tag chips (row below status), then bulk actions (row above status).
            var controls = CollectCatalogReviewFilterChipControls();
            controls.AddRange(CollectCatalogReviewTagChipControls());
            controls.AddRange(CollectCatalogReviewBulkActionControls());
            return controls;
        }

        private int GetCatalogReviewFilterIndexFromList() =>
            GetCatalogReviewFilterRanges().PreferredIndexFromList;

        private List<Control> CollectCatalogReviewEmptyActionControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            var emptyPanel = this.FindControl<StackPanel>("CatalogSyncNeedsReviewEmptyPanel");
            if (emptyPanel == null || !emptyPanel.IsVisible)
                return controls;

            Add(this.FindControl<Button>("CatalogSyncBackToLibraryButton"));
            Add(this.FindControl<Button>("CatalogSyncBackToSourcesButton"));
            return controls;
        }

        private List<Control> CollectCatalogSourcesToolbarControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            Add(AddCatalogSourceButton);
            Add(RefreshCatalogSourcesButton);
            return controls;
        }

        private List<Control> CollectCatalogSourcesFilterControls()
        {
            var controls = new List<Control>();

            void Add(Control? control)
            {
                if (control != null && control.IsVisible && control.IsEnabled)
                    controls.Add(control);
            }

            Add(CatalogSourceFilterAllButton);
            Add(CatalogSourceFilterEnabledButton);
            Add(CatalogSourceFilterDisabledButton);
            return controls;
        }

        private List<Control> CollectCatalogSourceCardActionControls(CatalogSourceListItem source)
        {
            var controls = new List<Control>();
            var border = FindCatalogCardBorder(source);
            if (border == null)
                return controls;

            var checkBox = border.GetVisualDescendants().OfType<CheckBox>()
                .FirstOrDefault(c => c.IsVisible && c.IsEnabled);
            if (checkBox != null)
                controls.Add(checkBox);

            foreach (var button in border.GetVisualDescendants().OfType<Button>()
                         .Where(b => b.IsVisible && b.IsEnabled && b.Classes.Contains("options")))
            {
                controls.Add(button);
            }

            return controls;
        }

        /// <summary>
        /// Confirm on a source card drills into actions on the first Button (Review), not Enabled.
        /// Enabled remains reachable via Left/Right within card actions.
        /// Note: Avalonia CheckBox inherits Button, so exclude ToggleButton.
        /// </summary>
        internal static int GetDefaultCatalogSourceCardActionIndex(IReadOnlyList<Control> controls)
        {
            for (var i = 0; i < controls.Count; i++)
            {
                if (controls[i] is Button and not ToggleButton)
                    return i;
            }

            return controls.Count > 0 ? 0 : -1;
        }

        private List<Control> CollectCatalogReviewRowActionControls(CatalogSyncRowItem row)
        {
            var controls = new List<Control>();
            var border = FindCatalogSyncRowBorder(row);
            if (border == null)
                return controls;

            foreach (var button in border.GetVisualDescendants().OfType<Button>()
                         .Where(b => b.IsVisible && b.IsEnabled && b.Classes.Contains("options")))
            {
                controls.Add(button);
            }

            return controls;
        }

        private void ApplyCatalogSourcesToolbarSelection(int index)
        {
            var controls = CollectCatalogSourcesToolbarControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.CatalogSourcesToolbarSelectedIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesToolbar;

            ClearGamepadFocus();
            ClearStyledControlsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ApplyCatalogSourcesFilterSelection(int index)
        {
            var controls = CollectCatalogSourcesFilterControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.CatalogSourcesFilterIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesFilters;

            ClearGamepadFocus();
            ClearStyledControlsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ApplyCatalogSourceCardActionSelection(int index)
        {
            if (CatalogSources.Count == 0)
                return;

            var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
            var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.CatalogSourceCardActionIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourceCardActions;

            ClearGamepadFocus();
            ClearCatalogSourceCardActionsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            CatalogSources[cardIndex].IsGamepadFocused = true;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ClearCatalogSourcesToolbarGamepadFocus()
        {
            var controls = CollectCatalogSourcesToolbarControls();
            ClearStyledControlsGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private void ClearCatalogSourcesFiltersGamepadFocus()
        {
            var controls = CollectCatalogSourcesFilterControls();
            ClearStyledControlsGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private void ClearCatalogSourceCardActionsGamepadFocus()
        {
            if (CatalogSources.Count == 0)
                return;

            var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
            if (cardIndex < 0 || cardIndex >= CatalogSources.Count)
                return;

            var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
            ClearCatalogSourceCardActionsGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private void ClearCatalogReviewRowActionsGamepadFocus()
        {
            var rows = CatalogSyncRows.ToList();
            if (rows.Count == 0)
                return;

            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return;

            var controls = CollectCatalogReviewRowActionControls(rows[rowIndex]);
            ClearStyledControlsGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
            _gamepadNavigation.CatalogReviewRowActionIndex = -1;
        }

        private static void ClearStyledControlsGamepadFocusClasses(IReadOnlyList<Control> controls)
        {
            foreach (var control in controls)
            {
                if (control is StyledElement styled)
                    styled.Classes.Set("gamepad-focused", false);
            }
        }

        private static void ClearCatalogSourceCardActionsGamepadFocusClasses(IReadOnlyList<Control> controls)
        {
            ClearStyledControlsGamepadFocusClasses(controls);
        }

        private void ApplyCatalogReviewRowActionSelection(int index)
        {
            var rows = CatalogSyncRows.ToList();
            if (rows.Count == 0)
                return;

            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return;

            var controls = CollectCatalogReviewRowActionControls(rows[rowIndex]);
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewRowActions;

            // ClearGamepadFocus → ClearCatalogReviewRowActionsGamepadFocus resets action index to -1.
            // Assign the new index after that clear, or Left wraps as if index were 0 → last button forever.
            ClearGamepadFocus();
            ClearStyledControlsGamepadFocusClasses(controls);
            _gamepadNavigation.CatalogReviewRowActionIndex = index;
            if (index < 0 || index >= controls.Count)
                return;

            rows[rowIndex].IsGamepadFocused = true;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ApplySidebarGamepadSelection(int index)
        {
            var controls = CollectSidebarFocusableControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.SidebarSelectedIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.Sidebar;

            ClearGamepadFocus();
            ClearSidebarGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ApplyTopBarGamepadSelection(int index)
        {
            var controls = CollectTopBarControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.TopBarSelectedIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.TopBar;

            ClearGamepadFocus();
            ClearTopBarGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
        }

        private void ClearSidebarGamepadFocus()
        {
            var controls = CollectSidebarFocusableControls();
            ClearSidebarGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private void ClearTopBarGamepadFocus()
        {
            var controls = CollectTopBarControls();
            ClearTopBarGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private static void ClearSidebarGamepadFocusClasses(IReadOnlyList<Control> controls)
        {
            foreach (var control in controls)
            {
                if (control is StyledElement styled)
                    styled.Classes.Set("gamepad-focused", false);
            }
        }

        private static void ClearTopBarGamepadFocusClasses(IReadOnlyList<Control> controls)
        {
            foreach (var control in controls)
            {
                if (control is StyledElement styled)
                    styled.Classes.Set("gamepad-focused", false);
            }
        }

        private void ClearFocusIfOnControls(IReadOnlyList<Control> controls)
        {
            var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
            if (focusManager?.GetFocusedElement() is Control focusedControl &&
                controls.Contains(focusedControl))
            {
                focusManager.ClearFocus();
            }
        }

        private void DismissTextInputFocus()
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }

        private void CloseAfterLaunchIfNeeded(bool launched)
        {
            if (!launched || !_settings.CloseAfterLaunch)
                return;

            DismissTextInputFocus();

            if (_settings.CloseToTray)
            {
                HideToTray();
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Hide();

            Close();
        }

        public void HideToTray()
        {
            DismissTextInputFocus();
            Hide();
            _app?.SetTrayVisible(true);
            RefreshUpdateCheckStatus();
        }

        public void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _app?.SetTrayVisible(_settings.CloseToTray || _settings.BackgroundUpdateCheckEnabled);
            RefreshUpdateCheckStatus();
        }

        public void RequestExit()
        {
            _forceExit = true;
            if (!IsVisible)
                Show();
            Close();
        }

        /// <summary>Called from App after tray wiring is ready.</summary>
        public void ApplyTraySettingsFromApp() => ApplyTrayAndBackgroundUpdateSettings();

        private void ApplyTrayAndBackgroundUpdateSettings()
        {
            var showTray = _settings.CloseToTray || _settings.BackgroundUpdateCheckEnabled;
            _app?.SetTrayVisible(showTray);
            ConfigureBackgroundUpdateTimer();
            RefreshUpdateCheckStatus();
        }

        private void ConfigureBackgroundUpdateTimer()
        {
            _backgroundUpdateTimer?.Stop();

            if (!_settings.BackgroundUpdateCheckEnabled)
                return;

            var minutes = BackgroundUpdateCheckIntervals.Normalize(
                _settings.BackgroundUpdateCheckIntervalMinutes);

            _backgroundUpdateTimer ??= new DispatcherTimer();
            _backgroundUpdateTimer.Tick -= BackgroundUpdateTimer_Tick;
            _backgroundUpdateTimer.Tick += BackgroundUpdateTimer_Tick;
            _backgroundUpdateTimer.Interval = TimeSpan.FromMinutes(minutes);
            _backgroundUpdateTimer.Start();
        }

        private async void BackgroundUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_isBackgroundUpdateTickRunning || IsCheckingUpdates)
                return;

            _isBackgroundUpdateTickRunning = true;
            try
            {
                await RunUpdateCheckAsync(promptForReview: false, isManualCheck: false);
            }
            finally
            {
                _isBackgroundUpdateTickRunning = false;
            }
        }

        private void ApplyCatalogReviewFilterSelection(int index)
        {
            var controls = CollectCatalogReviewFilterControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.CatalogReviewFilterIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewFilters;

            ClearCatalogReviewEmptyActionGamepadFocus();
            ClearCatalogReviewRowActionsGamepadFocus();
            ClearGamepadFocus();
            ClearStyledControlsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ApplyCatalogReviewEmptyActionSelection(int index)
        {
            var controls = CollectCatalogReviewEmptyActionControls();
            index = _gamepadNavigation.ClampIndex(index, controls.Count);
            _gamepadNavigation.CatalogReviewSelectedIndex = index;
            _gamepadNavigation.CatalogReviewRowActionIndex = -1;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewList;

            ClearCatalogReviewFilterGamepadFocus();
            ClearCatalogReviewRowActionsGamepadFocus();
            ClearGamepadFocus();
            ClearStyledControlsGamepadFocusClasses(controls);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is StyledElement styled)
                styled.Classes.Set("gamepad-focused", true);

            controls[index].Focus();
            Dispatcher.UIThread.Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ClearCatalogReviewFilterGamepadFocus()
        {
            var controls = CollectCatalogReviewFilterControls();
            ClearStyledControlsGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private void ClearCatalogReviewEmptyActionGamepadFocus()
        {
            var controls = CollectCatalogReviewEmptyActionControls();
            ClearStyledControlsGamepadFocusClasses(controls);
            ClearFocusIfOnControls(controls);
        }

        private void ApplyCatalogReviewRowSelection(int index)
        {
            var rows = CatalogSyncRows.ToList();
            index = _gamepadNavigation.ClampIndex(index, rows.Count);
            _gamepadNavigation.CatalogReviewSelectedIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewList;

            ClearCatalogReviewFilterGamepadFocus();
            ClearCatalogReviewEmptyActionGamepadFocus();
            ClearCatalogReviewRowActionsGamepadFocus();
            ClearGamepadFocus();
            if (index < 0 || index >= rows.Count)
                return;

            rows[index].IsGamepadFocused = true;
            Dispatcher.UIThread.Post(() => FindCatalogSyncRowBorder(rows[index])?.BringIntoView(), DispatcherPriority.Loaded);
        }

        private void SelectInitialGamepadItemForCurrentView()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (_mainViewMode == MainViewMode.Library && _isModsOverlayOpen)
                SelectInitialModsGamepadItem();
            else if (_mainViewMode == MainViewMode.Library && _isAppUpdatesReviewOpen)
                SelectInitialAppUpdatesReviewGamepadItem();
            else if (_mainViewMode == MainViewMode.Library)
                SelectInitialLibraryGamepadItem();
            else if (_mainViewMode == MainViewMode.AppCatalog && _appCatalogSubView == AppCatalogSubView.Sources)
                SelectInitialCatalogGamepadItem();
            else if (_mainViewMode == MainViewMode.AppCatalog && _appCatalogSubView == AppCatalogSubView.Review)
                SelectInitialCatalogReviewGamepadItem();
        }

        private void SelectInitialLibraryGamepadItem()
        {
            if (!IsGamepadFocusActive ||
                isSettingsPanelOpen ||
                _isEntryFormOpen ||
                _isTagEditOpen ||
                IsDisplayFilterOverlayOpen)
            {
                if (!IsGamepadFocusActive)
                    ClearGamepadFocus();
                return;
            }

            if (Games.Count == 0)
            {
                ClearGamepadFocus();
                _gamepadNavigation.LibrarySelectedIndex = -1;
                return;
            }

            ApplyLibraryGamepadSelection(_gamepadNavigation.LibrarySelectedIndex < 0 ? 0 : _gamepadNavigation.LibrarySelectedIndex);
        }

        private void SelectInitialCatalogGamepadItem()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (CatalogSources.Count == 0)
            {
                ApplyCatalogSourcesToolbarSelection(0);
                return;
            }

            ApplyCatalogGamepadSelection(_gamepadNavigation.CatalogSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogSelectedIndex);
        }

        private void SelectInitialCatalogReviewGamepadItem()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (CatalogSyncRows.Count == 0)
            {
                var emptyActions = CollectCatalogReviewEmptyActionControls();
                if (emptyActions.Count > 0)
                {
                    ApplyCatalogReviewEmptyActionSelection(0);
                    return;
                }

                ClearGamepadFocus();
                _gamepadNavigation.CatalogReviewSelectedIndex = -1;
                ApplyCatalogReviewFilterSelection(0);
                return;
            }

            ApplyCatalogReviewRowSelection(
                _gamepadNavigation.CatalogReviewSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogReviewSelectedIndex);
        }

        private void SyncGamepadLibrarySelection()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (isSettingsPanelOpen || _mainViewMode != MainViewMode.Library)
                return;

            if (Games.Count == 0)
            {
                ClearGamepadFocus();
                _gamepadNavigation.LibrarySelectedIndex = -1;
                return;
            }

            var clamped = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
            ApplyLibraryGamepadSelection(clamped);
        }

        private void SyncCatalogGamepadSelection()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (isSettingsPanelOpen ||
                _mainViewMode != MainViewMode.AppCatalog ||
                _appCatalogSubView != AppCatalogSubView.Sources)
            {
                return;
            }

            if (CatalogSources.Count == 0)
            {
                _gamepadNavigation.CatalogSelectedIndex = -1;
                _gamepadNavigation.CatalogSourceCardActionIndex = -1;

                if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesToolbar)
                {
                    ApplyCatalogSourcesToolbarSelection(
                        _gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
                }
                else
                {
                    // Empty filter (e.g. enabled last Disabled source) — re-home to filter chips
                    // so Confirm is not stuck in CatalogSourceCardActions with nothing to activate.
                    ApplyCatalogSourcesFilterSelection(
                        _gamepadNavigation.CatalogSourcesFilterIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogSourcesFilterIndex);
                }

                return;
            }

            // Keep toolbar/filter focus after list rebuilds (e.g. Refresh All Sources)
            // so focus does not jump to a source under a pending Yes/No prompt.
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesToolbar)
            {
                ApplyCatalogSourcesToolbarSelection(
                    _gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0
                        ? 0
                        : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
                return;
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesFilters)
            {
                ApplyCatalogSourcesFilterSelection(
                    _gamepadNavigation.CatalogSourcesFilterIndex < 0
                        ? 0
                        : _gamepadNavigation.CatalogSourcesFilterIndex);
                return;
            }

            var clamped = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
            ApplyCatalogGamepadSelection(clamped);
        }

        private void SyncCatalogReviewGamepadSelection()
        {
            if (!IsGamepadFocusActive)
            {
                ClearGamepadFocus();
                return;
            }

            if (_mainViewMode != MainViewMode.AppCatalog ||
                _appCatalogSubView != AppCatalogSubView.Review)
            {
                return;
            }

            if (CatalogSyncRows.Count == 0)
            {
                ClearCatalogReviewRowActionsGamepadFocus();
                ClearGamepadFocus();
                _gamepadNavigation.CatalogReviewRowActionIndex = -1;

                var emptyActions = CollectCatalogReviewEmptyActionControls();
                if (emptyActions.Count > 0)
                {
                    ApplyCatalogReviewEmptyActionSelection(0);
                    return;
                }

                _gamepadNavigation.CatalogReviewSelectedIndex = -1;
                ApplyCatalogReviewFilterSelection(0);
                return;
            }

            var wasInRowActions =
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions;
            var actionIndex = _gamepadNavigation.CatalogReviewRowActionIndex;
            var clamped = _gamepadNavigation.ClampIndex(
                _gamepadNavigation.CatalogReviewSelectedIndex,
                CatalogSyncRows.Count);

            // After Add/Ignore/etc the row list is rebuilt. Keep the same list index (now the
            // next app) and restore either the row ring or the action-strip focus.
            if (wasInRowActions)
            {
                _gamepadNavigation.CatalogReviewSelectedIndex = clamped;
                ClearGamepadFocus();
                CatalogSyncRows[clamped].IsGamepadFocused = true;

                Dispatcher.UIThread.Post(() =>
                {
                    if (!_settings.EnableGamepadInput ||
                        _mainViewMode != MainViewMode.AppCatalog ||
                        _appCatalogSubView != AppCatalogSubView.Review ||
                        CatalogSyncRows.Count == 0)
                    {
                        return;
                    }

                    var rowIndex = _gamepadNavigation.ClampIndex(
                        _gamepadNavigation.CatalogReviewSelectedIndex,
                        CatalogSyncRows.Count);
                    if (rowIndex < 0)
                        return;

                    var controls = CollectCatalogReviewRowActionControls(CatalogSyncRows[rowIndex]);
                    if (controls.Count == 0)
                    {
                        ApplyCatalogReviewRowSelection(rowIndex);
                        return;
                    }

                    var nextAction = _gamepadNavigation.ClampIndex(
                        actionIndex < 0 ? 0 : actionIndex,
                        controls.Count);
                    ApplyCatalogReviewRowActionSelection(nextAction < 0 ? 0 : nextAction);
                }, DispatcherPriority.Loaded);
                return;
            }

            ApplyCatalogReviewRowSelection(clamped);
        }

        private void ApplyLibraryGamepadSelection(int index)
        {
            // Overlays sit above the library; never steal zone/focus while they are open.
            if (isSettingsPanelOpen ||
                _isEntryFormOpen ||
                _isTagEditOpen ||
                IsDisplayFilterOverlayOpen)
            {
                return;
            }

            var games = Games.ToList();
            index = _gamepadNavigation.ClampIndex(index, games.Count);
            _gamepadNavigation.LibrarySelectedIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;

            // Drop chrome focus entirely so sidebar/top bar don't keep or regain orange rings.
            ClearSidebarGamepadFocus();
            ClearTopBarGamepadFocus();
            _gamepadNavigation.SidebarSelectedIndex = -1;
            _gamepadNavigation.TopBarSelectedIndex = -1;

            ClearGamepadFocus();
            // Cards use IsGamepadFocused only — clear Avalonia focus so Continue/:focus cannot dual-highlight.
            DismissTextInputFocus();
            if (index < 0 || index >= games.Count)
                return;

            games[index].IsGamepadFocused = true;
            Dispatcher.UIThread.Post(() => FindGameCardRoot(games[index])?.BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ApplyCatalogGamepadSelection(int index)
        {
            if (CatalogSources.Count == 0)
            {
                ApplyCatalogSourcesToolbarSelection(
                    _gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0
                        ? 0
                        : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
                return;
            }

            index = _gamepadNavigation.ClampIndex(index, CatalogSources.Count);
            _gamepadNavigation.CatalogSelectedIndex = index;
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSources;

            ClearSidebarGamepadFocus();
            ClearCatalogSourcesToolbarGamepadFocus();
            ClearGamepadFocus();
            ClearCatalogSourceCardActionsGamepadFocus();
            DismissTextInputFocus();
            if (index < 0 || index >= CatalogSources.Count)
                return;

            CatalogSources[index].IsGamepadFocused = true;
            Dispatcher.UIThread.Post(() => FindCatalogCardBorder(CatalogSources[index])?.BringIntoView(), DispatcherPriority.Loaded);
        }

        private void ClearGamepadFocus()
        {
            foreach (var game in _gameManager.Games)
                game.IsGamepadFocused = false;

            foreach (var source in CatalogSources)
                source.IsGamepadFocused = false;

            foreach (var row in CatalogSyncRows)
                row.IsGamepadFocused = false;

            ClearTopBarGamepadFocusClasses(CollectTopBarControls());
            ClearSidebarGamepadFocusClasses(CollectSidebarFocusableControls());
            ClearAnnouncementBannerGamepadFocus();
            ClearCatalogSourcesToolbarGamepadFocus();
            ClearCatalogSourcesFiltersGamepadFocus();
            ClearCatalogSourceCardActionsGamepadFocus();
            ClearCatalogReviewRowActionsGamepadFocus();
            ClearCatalogReviewEmptyActionGamepadFocus();
            ClearAppUpdatesReviewToolbarGamepadFocus();
            ClearAppUpdatesReviewRowActionsGamepadFocus();
            ClearAppUpdatesReviewRowFocus();
            ClearModsGamepadFocus();
            ClearSettingsGamepadFocusClasses(CollectSettingsFocusableControls());
        }

        /// <summary>
        /// Keep the selected library card's orange focus ring while a context menu is open,
        /// without restoring sidebar/top-bar focus rings.
        /// </summary>
        private void PreserveLibraryGamepadFocusWhileOpeningMenu()
        {
            if (!IsGamepadFocusActive || _mainViewMode != MainViewMode.Library)
            {
                ClearGamepadFocus();
                return;
            }

            ClearSidebarGamepadFocus();
            ClearTopBarGamepadFocus();
            _gamepadNavigation.SidebarSelectedIndex = -1;
            _gamepadNavigation.TopBarSelectedIndex = -1;

            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
            if (index < 0)
                return;

            var selected = Games[index];
            foreach (var game in _gameManager.Games)
                game.IsGamepadFocused = ReferenceEquals(game, selected);

            foreach (var source in CatalogSources)
                source.IsGamepadFocused = false;
            foreach (var row in CatalogSyncRows)
                row.IsGamepadFocused = false;
        }

        private void RestoreLibraryGamepadFocusAfterMenu()
        {
            if (!IsGamepadFocusActive ||
                isSettingsPanelOpen ||
                _isTagEditOpen ||
                _isEntryFormOpen ||
                _mainViewMode != MainViewMode.Library)
            {
                if (!IsGamepadFocusActive)
                    ClearGamepadFocus();
                return;
            }

            ClearSidebarGamepadFocus();
            ClearTopBarGamepadFocus();
            _gamepadNavigation.SidebarSelectedIndex = -1;
            _gamepadNavigation.TopBarSelectedIndex = -1;

            if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.Library)
                return;

            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
            if (index < 0)
                return;

            var selected = Games[index];
            foreach (var game in _gameManager.Games)
                game.IsGamepadFocused = ReferenceEquals(game, selected);
        }

        private void FocusSidebarNav()
        {
            ApplySidebarGamepadSelection(0);
        }

        private bool IsListGamepadLayout() => !_settings.UseGridView;

        private ItemsControl? GetActiveGamesItemsControl()
        {
            if (_settings.UseGridView)
                return _settings.GridCompactCards ? CompactGridViewControl : ClassicGridViewControl;

            return ListViewControl;
        }

        private List<(double X, double Y)> CollectGameCardPositions(IReadOnlyList<GameInfo> games)
        {
            var positions = new List<(double X, double Y)>();
            foreach (var game in games)
            {
                var card = FindGameCardRoot(game);
                positions.Add(GetControlCenter(card) ?? (0, positions.Count * 120));
            }

            return positions;
        }

        private List<(double X, double Y)> CollectCatalogCardPositions()
        {
            var positions = new List<(double X, double Y)>();
            foreach (var source in CatalogSources)
            {
                var card = FindCatalogCardBorder(source);
                positions.Add(GetControlCenter(card) ?? (0, positions.Count * 160));
            }

            return positions;
        }

        private (double X, double Y)? GetControlCenter(Control? control)
        {
            if (control == null)
                return null;

            var topLeft = control.TranslatePoint(new Point(0, 0), this);
            if (!topLeft.HasValue)
                return null;

            var bounds = control.Bounds;
            return (topLeft.Value.X + bounds.Width / 2, topLeft.Value.Y + bounds.Height / 2);
        }

        private Border? FindGameCardBorder(GameInfo game, ItemsControl? itemsControl = null)
        {
            itemsControl ??= GetActiveGamesItemsControl();
            if (itemsControl == null)
                return null;

            return itemsControl.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b =>
                    ReferenceEquals(b.DataContext, game) &&
                    (b.Classes.Contains("gamecard") ||
                     b.Classes.Contains("gamecardgrid") ||
                     b.Classes.Contains("gamecardcompact")));
        }

        private Control? FindGameCardRoot(GameInfo game, ItemsControl? itemsControl = null)
        {
            var border = FindGameCardBorder(game, itemsControl);
            if (border == null)
                return null;

            if (border.Classes.Contains("gamecardgrid") &&
                border.Parent is StackPanel stack &&
                ReferenceEquals(stack.DataContext, game))
            {
                return stack;
            }

            return border;
        }

        private Border? FindCatalogCardBorder(CatalogSourceListItem source)
        {
            var panel = this.FindControl<ScrollViewer>("CatalogSourcesPanel");
            if (panel == null)
                return null;

            return panel.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => ReferenceEquals(b.DataContext, source));
        }

        private Border? FindCatalogSyncRowBorder(CatalogSyncRowItem row)
        {
            if (CatalogSyncRowsItemsControl == null)
                return null;

            return CatalogSyncRowsItemsControl.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => ReferenceEquals(b.DataContext, row));
        }

        private Button? FindGameActionButton(GameInfo game) =>
            GetActiveGamesItemsControl()?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b =>
                    ReferenceEquals(b.DataContext, game) &&
                    (b.Classes.Contains("modern") || b.Classes.Contains("modern-icon")));

        private Button? FindGameOptionsButton(GameInfo game) =>
            GetActiveGamesItemsControl()?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => ReferenceEquals(b.DataContext, game) && b.ContextMenu != null);

        /// <summary>
        /// Prefer a stable placement target for download/executable menus. The action button
        /// mutates during PerformActionAsync and can orphan X11 popups under Gamescope.
        /// </summary>
        private Control? ResolveDownloadMenuAnchor(GameInfo game, Control? fallback = null) =>
            FindGameOptionsButton(game) as Control
            ?? FindGameCardBorder(game)
            ?? FindGameActionButton(game)
            ?? fallback
            ?? FindGameMenuAnchor(game);

        private async Task PerformSelectedGameActionAsync(GameInfo game)
        {
            var launched = false;
            try
            {
                if (game.Status == GameStatus.UpdateAvailable)
                {
                    var updateAnchor = ResolveDownloadMenuAnchor(game);
                    if (updateAnchor == null)
                        return;

                    ShowUpdateActionMenu(updateAnchor, game);
                    return;
                }

                if (game.Status == GameStatus.NotInstalled)
                    game.ClearDownloadSelection();

                launched = await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);

                var anchor = ResolveDownloadMenuAnchor(game);
                if (anchor != null && TryShowPendingSelectionMenus(anchor, game))
                    return;

                UpdateContinueButtonState();
                RestoreLibraryGamepadFocusAfterMenu();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to perform action for {game.Name}: {ex.Message}", "Action Error");
                RestoreLibraryGamepadFocusAfterMenu();
            }

            CloseAfterLaunchIfNeeded(launched);
        }

        private async Task ReviewCatalogSourceByIdAsync(string sourceId)
        {
            var source = _settings.AppCatalogSources.FirstOrDefault(s => s.Id == sourceId);
            var filter = source is { PendingReviewCount: > 0 }
                ? CatalogReviewFilter.NeedsReview
                : CatalogReviewFilter.All;
            await OpenCatalogReviewAsync(sourceId, filter);
        }

        private void ActivateSidebarSelection()
        {
            var controls = CollectSidebarFocusableControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.SidebarSelectedIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            var control = controls[index];
            if (control is Button button)
            {
                GamepadControlActivation.ActivateButton(button);
                return;
            }

            control.Focus();
        }

        private void ActivateTopBarSelection()
        {
            var controls = CollectTopBarControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.TopBarSelectedIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            var control = controls[index];
            if (control is ComboBox comboBox)
            {
                comboBox.Focus();
                if (!comboBox.IsDropDownOpen)
                {
                    comboBox.IsDropDownOpen = true;
                    GamepadComboBoxNavigation.Open(comboBox);
                }

                return;
            }

            if (control is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void ActivateReviewFilterSelection()
        {
            var controls = CollectCatalogReviewFilterControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewFilterIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
            else if (controls[index] is TextBox textBox)
                GamepadControlActivation.ActivateTextBox(textBox);
        }

        private void ActivateReviewRowSelection()
        {
            if (CatalogSyncRows.Count == 0)
            {
                ActivateCatalogReviewEmptyActionSelection();
                return;
            }

            var rows = CatalogSyncRows.ToList();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
            if (index < 0 || index >= rows.Count)
                return;

            var controls = CollectCatalogReviewRowActionControls(rows[index]);
            if (controls.Count == 0)
                return;

            // Enter the action strip; do not fire Add/Ignore/etc until Confirm again.
            ApplyCatalogReviewRowActionSelection(0);
        }

        private void ActivateCatalogReviewEmptyActionSelection()
        {
            var controls = CollectCatalogReviewEmptyActionControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void ActivateCatalogReviewRowActionSelection()
        {
            var rows = CatalogSyncRows.ToList();
            if (rows.Count == 0)
                return;

            var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
            if (rowIndex < 0 || rowIndex >= rows.Count)
                return;

            var controls = CollectCatalogReviewRowActionControls(rows[rowIndex]);
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewRowActionIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void ActivateCatalogSourcesToolbarSelection()
        {
            var controls = CollectCatalogSourcesToolbarControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSourcesToolbarSelectedIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void ActivateCatalogSourcesFilterSelection()
        {
            var controls = CollectCatalogSourcesFilterControls();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSourcesFilterIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void ActivateCatalogSourceCardActionSelection()
        {
            if (CatalogSources.Count == 0)
                return;

            var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
            var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSourceCardActionIndex, controls.Count);
            if (index < 0 || index >= controls.Count)
                return;

            if (controls[index] is CheckBox checkBox)
            {
                checkBox.IsChecked = !checkBox.IsChecked;
                return;
            }

            if (controls[index] is Button button)
                GamepadControlActivation.ActivateButton(button);
        }

        private void HandleOptionsAction()
        {
            if (!AllowChromeActions)
                return;

            if (_isChangelogOpen)
            {
                CloseChangelog();
                return;
            }

            if (_isModDetailsOpen)
            {
                CloseModDetails();
                return;
            }

            // Options toggles: close an open context menu (download/executable/app options).
            if (_inputService?.TryHandleContextMenuOptionsDismiss() == true)
                return;

            // Don't open the app options menu over combo boxes / modal dialogs.
            if (_inputService?.IsGamepadOverlayActive == true)
                return;

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.Sidebar)
            {
                TryOpenFocusedDisplayFilterOverflowMenu();
                return;
            }

            if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.Library ||
                _gamepadNavigation.LibrarySelectedIndex < 0)
            {
                return;
            }

            var games = Games.ToList();
            var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, games.Count);
            if (index < 0 || index >= games.Count)
                return;

            var optionsButton = FindGameOptionsButton(games[index]);
            if (optionsButton != null)
                OptionsButton_Click(optionsButton, new RoutedEventArgs());
        }

        private void HandleConfirmAction()
        {
            if (_handlingGamepadConfirm)
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

        private void HandleConfirmActionCore()
        {
            if (_isChangelogOpen &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.ChangelogOverlay)
            {
                ActivateChangelogGamepadSelection();
                return;
            }

            if (_isModDetailsOpen &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.ModsDetailsOverlay)
            {
                HandleModDetailsGamepadConfirm();
                return;
            }

            if (AllowChromeActions && _inputService?.TryHandleContextMenuConfirm() == true)
                return;

            if (AllowChromeActions && _inputService?.TryHandleModalConfirm() == true)
                return;

            if (AllowChromeActions && _inputService?.TryHandleComboBoxConfirm() == true)
                return;

            // Prefer overlays whenever open, even if a layout sync briefly flipped ActiveZone.
            if (isSettingsPanelOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Settings;
                ActivateSettingsGamepadSelection();
                return;
            }

            if (IsDisplayFilterOverlayOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.DisplayFilterOverlay;
                ActivateDisplayFilterGamepadSelection();
                return;
            }

            if (_isEntryFormOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.EntryFormOverlay;
                ActivateEntryFormGamepadSelection();
                return;
            }

            if (_isTagEditOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.TagEditOverlay;
                ActivateTagEditGamepadSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.Sidebar)
            {
                ActivateSidebarSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.TopBar)
            {
                ActivateTopBarSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.AnnouncementBanner)
            {
                DismissAnnouncementBanner();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewFilters)
            {
                ActivateReviewFilterSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewList)
            {
                ActivateReviewRowSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions)
            {
                ActivateCatalogReviewRowActionSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.ModsDetailsOverlay)
            {
                HandleModDetailsGamepadConfirm();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone is GamepadNavigationZone.ModsOverlayToolbar
                    or GamepadNavigationZone.ModsOverlayFilters
                    or GamepadNavigationZone.ModsOverlaySourceFilters
                    or GamepadNavigationZone.ModsOverlayList
                    or GamepadNavigationZone.ModsOverlayRowActions)
            {
                HandleModsGamepadConfirm();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewToolbar)
            {
                ActivateAppUpdatesReviewToolbarSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewList)
            {
                ActivateAppUpdatesReviewRowSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewRowActions)
            {
                ActivateAppUpdatesReviewRowActionSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesToolbar)
            {
                ActivateCatalogSourcesToolbarSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesFilters)
            {
                ActivateCatalogSourcesFilterSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourceCardActions)
            {
                if (CatalogSources.Count == 0)
                {
                    ApplyCatalogSourcesFilterSelection(
                        _gamepadNavigation.CatalogSourcesFilterIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogSourcesFilterIndex);
                    ActivateCatalogSourcesFilterSelection();
                    return;
                }

                ActivateCatalogSourceCardActionSelection();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.Library &&
                _gamepadNavigation.LibrarySelectedIndex >= 0)
            {
                var games = Games.ToList();
                var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, games.Count);
                if (index >= 0 && index < games.Count)
                {
                    _ = PerformSelectedGameActionAsync(games[index]);
                    return;
                }
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSources &&
                _gamepadNavigation.CatalogSelectedIndex >= 0)
            {
                var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
                if (index >= 0 && index < CatalogSources.Count)
                {
                    var actions = CollectCatalogSourceCardActionControls(CatalogSources[index]);
                    var actionIndex = GetDefaultCatalogSourceCardActionIndex(actions);
                    if (actionIndex >= 0)
                        ApplyCatalogSourceCardActionSelection(actionIndex);
                    return;
                }
            }

            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

            if (focused is MenuItem menuItem)
            {
                menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else if (focused is Button button)
            {
                if (button.ContextMenu != null)
                    OpenContextMenu(button, button.ContextMenu);
                else
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else if (focused is CheckBox checkBox)
            {
                checkBox.IsChecked = !checkBox.IsChecked;
            }
            else if (focused is ToggleButton toggleButton)
            {
                toggleButton.IsChecked = !toggleButton.IsChecked;
            }
        }

        private void HandleCancelAction()
        {
            if (_rebindListeningAction.HasValue)
            {
                CancelGamepadRebindListen();
                return;
            }

            if (_keyboardRebindListeningAction.HasValue)
            {
                CancelKeyboardRebindListen();
                return;
            }

            // Changelog takes priority over a leftover context menu underneath it.
            if (_isChangelogOpen)
            {
                _inputService?.TryHandleContextMenuOptionsDismiss();
                CloseChangelog();
                return;
            }

            if (_isModDetailsOpen)
            {
                CloseModDetails();
                return;
            }

            if (AllowChromeActions && _inputService?.TryHandleContextMenuCancel() == true)
                return;

            if (AllowChromeActions && _inputService?.TryHandleModalCancel() == true)
                return;

            if (AllowChromeActions && _inputService?.TryHandleComboBoxCancel() == true)
                return;

            // First Cancel leaves text edit / dismisses Steam OSK; second closes the overlay.
            if (AllowChromeActions &&
                TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            {
                DismissTextInputFocus();
                return;
            }

            if (IsDisplayFilterOverlayOpen)
            {
                CloseDisplayFilterOverlay();
                return;
            }

            if (_isEntryFormOpen)
            {
                CloseEntryFormOverlay();
                return;
            }

            if (_isTagEditOpen)
            {
                CloseTagEditOverlay();
                return;
            }

            if (_isModsOverlayOpen)
            {
                HandleModsGamepadCancel();
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourceCardActions)
            {
                if (CatalogSources.Count == 0)
                {
                    ApplyCatalogSourcesFilterSelection(
                        _gamepadNavigation.CatalogSourcesFilterIndex < 0
                            ? 0
                            : _gamepadNavigation.CatalogSourcesFilterIndex);
                    return;
                }

                ApplyCatalogGamepadSelection(_gamepadNavigation.CatalogSelectedIndex);
                return;
            }

            if (AllowChromeActions &&
                _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions)
            {
                ApplyCatalogReviewRowSelection(_gamepadNavigation.CatalogReviewSelectedIndex);
                return;
            }

            // Close settings panel if open
            if (CloseSettingsPanel())
                return;

            if (AllowChromeActions && TryNavigateGamepadZoneBack())
                return;
        }

        private bool TryNavigateGamepadZoneBack()
        {
            switch (_gamepadNavigation.ActiveZone)
            {
                case GamepadNavigationZone.Sidebar:
                case GamepadNavigationZone.TopBar:
                case GamepadNavigationZone.AnnouncementBanner:
                    ClearSidebarGamepadFocus();
                    ClearTopBarGamepadFocus();
                    ClearAnnouncementBannerGamepadFocus();
                    SelectInitialGamepadItemForCurrentView();
                    return true;

                case GamepadNavigationZone.CatalogSourcesToolbar:
                case GamepadNavigationZone.CatalogSourcesFilters:
                    ClearCatalogSourcesToolbarGamepadFocus();
                    ClearCatalogSourcesFiltersGamepadFocus();
                    SelectInitialCatalogGamepadItem();
                    return true;

                case GamepadNavigationZone.CatalogReviewFilters:
                    _gamepadNavigation.CatalogReviewFilterIndex = -1;
                    SelectInitialCatalogReviewGamepadItem();
                    return true;

                case GamepadNavigationZone.CatalogReviewList:
                    ShowAppCatalogSourcesView();
                    return true;

                case GamepadNavigationZone.AppUpdatesReviewToolbar:
                case GamepadNavigationZone.AppUpdatesReviewRowActions:
                    ClearAppUpdatesReviewToolbarGamepadFocus();
                    ClearAppUpdatesReviewRowActionsGamepadFocus();
                    SelectInitialAppUpdatesReviewGamepadItem();
                    return true;

                case GamepadNavigationZone.AppUpdatesReviewList:
                    ShowLibraryView();
                    return true;

                default:
                    return false;
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Unsubscribe from events
            this.Activated -= MainWindow_Activated;
            this.Deactivated -= MainWindow_Deactivated;

            // Stop Launcher Music
            _fadeTaskCts?.Cancel();
            _fadeTaskCts?.Dispose();
            StopLauncherMusic();

            if (_inputService != null)
            {
                _inputService.OnConfirm -= HandleConfirmAction;
                _inputService.OnCancel -= HandleCancelAction;
                _inputService.OnOptions -= HandleOptionsAction;
                _inputService.OnGamepadConnectionChanged -= HandleGamepadConnectionChanged;
                _inputService.Dispose();
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!_forceExit && _settings.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _backgroundUpdateTimer?.Stop();
            _fadeTaskCts?.Cancel();
            StopLauncherMusic();
            _app?.SetTrayVisible(false);
            base.OnClosing(e);
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            RestoreWindowStateAfterMinimizeIfNeeded();

            // Always reclaim input when Quiver is focused. Waiting on the launched process can
            // hang (shell-execute / orphaned waiters), which used to leave _launchedGameOwnsInput
            // true and swallow all gamepad/keyboard navigation while Cancel still worked.
            RestoreLauncherInputAfterForeground();

            #if WINDOWS
                        _ = FadeMusicAsync(MusicVolume, FADE_DURATION_MS);
            #else
                if (_musicPausedByDeactivation)
                {
                    _musicPausedByDeactivation = false;
                    _ = FadeMusicAsync(MusicVolume, FADE_DURATION_MS);
                }
            #endif
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            _inputService?.SetWindowActive(false);
            if (_inputService?.ShouldKeepPollingWhenDeactivated() != true)
                _inputService?.SetGamepadEnabled(false);

            #if WINDOWS
                        _ = FadeMusicAsync(0f, FADE_DURATION_MS);
            #else
                if (_musicProcess != null && !_musicProcess.HasExited)
                {
                    _musicPausedByDeactivation = true;
                    _ = FadeMusicAsync(0f, FADE_DURATION_MS);
                }
            #endif
        }

        private void SubscribeToGameEvents(GameInfo game)
        {
            // Unsubscribe
            game.GameProcessStarted -= OnGameProcessStarted;
            game.GameProcessStarted += OnGameProcessStarted;
        }

        private void OnGameProcessStarted(Process? process)
        {
            DismissTextInputFocus();
            _launchedGameOwnsInput = true;
            _trackingLaunchedGameProcess = process != null;
            _inputService?.SetWindowActive(false);
            _inputService?.SetGamepadEnabled(false);

            if (process == null)
            {
                // Nothing to wait on — reclaim immediately if Quiver is still foreground.
                if (IsActive)
                    RestoreLauncherInputAfterForeground();
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync();

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        var currentProcessGroupId = await TryGetProcessGroupIdAsync(Environment.ProcessId);
                        var launchedProcessGroupId = await TryGetProcessGroupIdAsync(process.Id);
                        if (launchedProcessGroupId.HasValue && launchedProcessGroupId != currentProcessGroupId)
                        {
                            while (await HasActiveProcessGroupAsync(launchedProcessGroupId.Value))
                            {
                                await Task.Delay(1000);
                            }
                        }
                    }
                }
                catch { /* process may have already exited */ }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _trackingLaunchedGameProcess = false;
                    _launchedGameOwnsInput = false;

                    if (IsActive)
                        RestoreLauncherInputAfterForeground();
                });
            });
        }

        /// <summary>
        /// Re-enable launcher input and restore the current gamepad/keyboard highlight after
        /// returning from a launched game (or when focus returns while a wait is still pending).
        /// </summary>
        private void RestoreLauncherInputAfterForeground()
        {
            _launchedGameOwnsInput = false;
            _inputService?.SetWindowActive(true);
            if (_settings.EnableGamepadInput)
                _inputService?.SetGamepadEnabled(true);

            UpdateGamepadChromeClass();

            if (!IsGamepadFocusActive)
                return;

            RestoreCurrentGamepadZoneSelection();
        }

        private void RestoreCurrentGamepadZoneSelection()
        {
            switch (_gamepadNavigation.ActiveZone)
            {
                case GamepadNavigationZone.Sidebar:
                    ApplySidebarGamepadSelection(
                        _gamepadNavigation.SidebarSelectedIndex < 0
                            ? 0
                            : _gamepadNavigation.SidebarSelectedIndex);
                    return;
                case GamepadNavigationZone.TopBar:
                    ApplyTopBarGamepadSelection(
                        _gamepadNavigation.TopBarSelectedIndex < 0
                            ? 0
                            : _gamepadNavigation.TopBarSelectedIndex);
                    return;
                case GamepadNavigationZone.AnnouncementBanner:
                    ApplyAnnouncementBannerGamepadSelection();
                    return;
                case GamepadNavigationZone.ModsOverlayToolbar:
                    ApplyModsToolbarSelection(
                        _modsGamepadToolbarIndex < 0 ? 0 : _modsGamepadToolbarIndex);
                    return;
                case GamepadNavigationZone.ModsOverlayFilters:
                    ApplyModsFiltersSelection(
                        _modsGamepadFilterIndex < 0 ? 0 : _modsGamepadFilterIndex);
                    return;
                case GamepadNavigationZone.ModsOverlaySourceFilters:
                    ApplyModsSourceFiltersSelection(
                        _modsGamepadSourceFilterIndex < 0 ? 0 : _modsGamepadSourceFilterIndex);
                    return;
                case GamepadNavigationZone.ModsOverlayList:
                    ApplyModsListSelection(
                        _modsGamepadListIndex < 0 ? 0 : _modsGamepadListIndex);
                    return;
                case GamepadNavigationZone.ModsOverlayRowActions:
                    ApplyModsRowActionSelection(
                        _modsGamepadRowActionIndex < 0 ? 0 : _modsGamepadRowActionIndex);
                    return;
                case GamepadNavigationZone.ModsDetailsOverlay:
                    ApplyModDetailsGamepadSelection(
                        _modDetailsGamepadFocusIndex < 0 ? 0 : _modDetailsGamepadFocusIndex);
                    return;
                default:
                    SelectInitialGamepadItemForCurrentView();
                    return;
            }
        }

        private static async Task<int?> TryGetProcessGroupIdAsync(int processId)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-o pgid= -p {processId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var psProcess = Process.Start(startInfo);
                if (psProcess == null)
                {
                    return null;
                }

                var output = await psProcess.StandardOutput.ReadToEndAsync();
                await psProcess.WaitForExitAsync();

                if (psProcess.ExitCode != 0)
                {
                    return null;
                }

                var trimmed = output.Trim();
                if (int.TryParse(trimmed, out var processGroupId))
                {
                    return processGroupId;
                }
            }
            catch
            {
            }

            return null;
        }

        private static async Task<bool> HasActiveProcessGroupAsync(int processGroupId)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-o pid= -g {processGroupId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var psProcess = Process.Start(startInfo);
                if (psProcess == null)
                {
                    return false;
                }

                var output = await psProcess.StandardOutput.ReadToEndAsync();
                await psProcess.WaitForExitAsync();

                if (psProcess.ExitCode != 0)
                {
                    return false;
                }

                return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => !string.IsNullOrWhiteSpace(line));
            }
            catch
            {
                return false;
            }
        }

        private async void ShowChangelog_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null || string.IsNullOrEmpty(game.Repository))
            {
                await ShowMessageBoxAsync("Unable to retrieve changelog information.", "Error");
                return;
            }

            _currentChangelogGame = game;
            await ShowChangelogAsync(game);
        }

        private async Task ShowChangelogAsync(GameInfo game)
        {
            try
            {
                // Dismiss the options menu so it doesn't sit under/over the changelog.
                _inputService?.TryHandleContextMenuOptionsDismiss();

                _isChangelogOpen = true;
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.ChangelogOverlay;
                OnPropertyChanged(nameof(GamepadHintsVisible));

                // Show changelog panel
                var changelogPanel = this.FindControl<Border>("ChangelogPanel");
                if (changelogPanel != null)
                {
                    changelogPanel.IsVisible = true;
                }

                // Update header title
                var headerTitle = this.FindControl<TextBlock>("HeaderTitleText");
                if (headerTitle != null)
                {
                    headerTitle.Text = $"{game.Name} - Version {game.LatestVersion ?? "Unknown"}";
                }

                // Update changelog title
                var changelogTitle = this.FindControl<TextBlock>("ChangelogTitle");
                if (changelogTitle != null)
                {
                    changelogTitle.Text = $"{game.Name} Changelog";
                }

                // Fetch and render changelog
                var changelogContent = this.FindControl<ItemsControl>("ChangelogContent");
                if (changelogContent != null)
                {
                    // Show loading placeholder
                    var loadingPanel = new StackPanel();
                    loadingPanel.Children.Add(new TextBlock
                    {
                        Text = "Loading changelog...",
                        Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                        FontSize = 14
                    });
                    changelogContent.ItemsSource = new[] { loadingPanel };
                }

                if (IsGamepadFocusActive)
                    Dispatcher.UIThread.Post(() => ApplyChangelogGamepadSelection(0), DispatcherPriority.Loaded);

                string changelogText = await FetchChangelogAsync(game);

                if (!_isChangelogOpen)
                    return;

                if (changelogContent != null)
                {
                    changelogContent.ItemsSource = ParseMarkdown(changelogText);
                }

                if (IsGamepadFocusActive)
                    ApplyChangelogGamepadSelection(0);
                else
                    ClearChangelogGamepadFocus();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to load changelog: {ex.Message}", "Error");
                CloseChangelog();
            }
        }

        private void CloseChangelog_Click(object? sender, RoutedEventArgs e)
        {
            CloseChangelog();
        }

        private List<Control> ParseMarkdown(string markdown)
        {
            var controls = new List<Control>();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                controls.Add(new SelectableTextBlock
                {
                    Text = "No changelog available.",
                    Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                    FontSize = 14
                });
                return controls;
            }

            var lines = markdown.Split('\n');
            var listItems = new List<string>();
            var codeBlockLines = new List<string>();
            bool inCodeBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                // Code blocks
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        if (codeBlockLines.Count > 0)
                        {
                            var codeBlock = new Border
                            {
                                Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
                                BorderBrush = new SolidColorBrush(Color.Parse("#2d2d30")),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(12),
                                Margin = new Thickness(0, 8, 0, 8)
                            };
                            codeBlock.Child = new SelectableTextBlock
                            {
                                Text = string.Join("\n", codeBlockLines),
                                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                                FontSize = 13,
                                Foreground = new SolidColorBrush(Color.Parse("#d4d4d4"))
                            };
                            controls.Add(codeBlock);
                            codeBlockLines.Clear();
                        }
                        inCodeBlock = false;
                    }
                    else
                    {
                        FlushListItems(controls, listItems);
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBlockLines.Add(line);
                    continue;
                }

                // GitHub alerts: > [!NOTE], etc.
                var alertMatch = System.Text.RegularExpressions.Regex.Match(line.TrimStart(),
                    @"^>\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (alertMatch.Success)
                {
                    FlushListItems(controls, listItems);

                    var alertType = alertMatch.Groups[1].Value.ToUpper();
                    var alertContentLines = new List<string>();

                    // Move to the first line of content
                    i++;

                    // Collect all lines belonging to the alert block
                    while (i < lines.Length)
                    {
                        var nextLine = lines[i].TrimEnd('\r');

                        // Stop if the line is empty
                        if (string.IsNullOrWhiteSpace(nextLine))
                        {
                            break;
                        }

                        var trimmedLine = nextLine.TrimStart();

                        if (trimmedLine.StartsWith(">"))
                        {
                            // Standard blockquote line: strip the '>'
                            var content = trimmedLine.Substring(1);
                            if (content.StartsWith(" ")) content = content.Substring(1);
                            alertContentLines.Add(content);
                        }
                        else
                        {
                            // Lazy continuation
                            alertContentLines.Add(trimmedLine);
                        }
                        i++;
                    }

                    // Define colors and icon names
                    var (borderColorHex, iconPath) = alertType switch
                    {
                        "NOTE" => ("#0969da", "markdown_info.png"),
                        "TIP" => ("#1a7f37", "markdown_tip.png"),
                        "IMPORTANT" => ("#8250df", "markdown_important.png"),
                        "WARNING" => ("#9a6700", "markdown_warning.png"),
                        "CAUTION" => ("#d1242f", "markdown_caution.png"),
                        _ => ("#2d2d30", "markdown_info.png")
                    };

                    var alertColor = Color.Parse(borderColorHex);
                    var alertBrush = new SolidColorBrush(alertColor);

                    var alertBorder = new Border
                    {
                        BorderBrush = alertBrush,
                        BorderThickness = new Thickness(4, 0, 0, 0),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 8, 0, 8),
                        Background = new SolidColorBrush(alertColor) { Opacity = 0.05 }
                    };

                    var alertPanel = new StackPanel();

                    // Title row with icon tinted to border color
                    var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

                    try
                    {
                        // Use a Rectangle + OpacityMask to draw the icon in the alert's color
                        var iconRect = new Avalonia.Controls.Shapes.Rectangle
                        {
                            Width = 16,
                            Height = 16,
                            Margin = new Thickness(0, 0, 8, 0),
                            Fill = alertBrush,
                            VerticalAlignment = VerticalAlignment.Center,
                            OpacityMask = new ImageBrush
                            {
                                Source = new Avalonia.Media.Imaging.Bitmap(
                                    Avalonia.Platform.AssetLoader.Open(
                                        new Uri($"avares://QuiverLauncher/Assets/{iconPath}")))
                            }
                        };
                        titlePanel.Children.Add(iconRect);
                    }
                    catch (Exception)
                    {
                        // Fallback circle if icon load fails
                        titlePanel.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = alertBrush,
                            Margin = new Thickness(0, 0, 8, 0)
                        });
                    }

                    titlePanel.Children.Add(new SelectableTextBlock
                    {
                        Text = alertType,
                        FontSize = 14,
                        FontWeight = FontWeight.Bold,
                        Foreground = alertBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    alertPanel.Children.Add(titlePanel);

                    // Parse the inner content for markdown (bold, links, etc.)
                    var contentText = string.Join("\n", alertContentLines);
                    var contentBlocks = ParseInlineMarkdown(contentText);
                    foreach (var block in contentBlocks)
                    {
                        block.Margin = new Thickness(0, 2, 0, 2);
                        alertPanel.Children.Add(block);
                    }

                    alertBorder.Child = alertPanel;
                    controls.Add(alertBorder);
                    continue;
                }

                // Headers
                if (line.StartsWith("#"))
                {
                    FlushListItems(controls, listItems);
                    int level = 0;
                    while (level < line.Length && line[level] == '#') level++;
                    var headerText = line.Substring(level).Trim();
                    var fontSize = level switch { 1 => 24, 2 => 20, 3 => 18, 4 => 16, _ => 14 };
                    var fontWeight = level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;

                    controls.Add(new SelectableTextBlock
                    {
                        Text = headerText,
                        FontSize = fontSize,
                        FontWeight = fontWeight,
                        Foreground = new SolidColorBrush(Colors.White),
                        Margin = new Thickness(0, level == 1 ? 16 : 12, 0, 8)
                    });
                    continue;
                }

                // Lists
                if (line.TrimStart().StartsWith("* ") || line.TrimStart().StartsWith("- "))
                {
                    var itemText = line.TrimStart().Substring(2);
                    listItems.Add("• " + itemText);
                    continue;
                }

                var orderedMatch = System.Text.RegularExpressions.Regex.Match(line.TrimStart(), @"^(\d+)\.\s+(.+)");
                if (orderedMatch.Success)
                {
                    listItems.Add(orderedMatch.Groups[1].Value + ". " + orderedMatch.Groups[2].Value);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("---"))
                {
                    FlushListItems(controls, listItems);
                    if (line.Trim().StartsWith("---"))
                        controls.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2d2d30")), Margin = new Thickness(0, 12, 0, 12) });
                    continue;
                }

                FlushListItems(controls, listItems);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var blocks = ParseInlineMarkdown(line);
                    foreach (var block in blocks)
                    {
                        block.Margin = new Thickness(0, 0, 0, 8);
                        controls.Add(block);
                    }
                }
            }

            FlushListItems(controls, listItems);
            return controls;
        }

        private List<Control> ParseInlineMarkdown(string text)
        {
            var blocks = new List<Control>();
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };

            int i = 0;
            var currentText = new StringBuilder();

            void FlushText()
            {
                if (currentText.Length > 0)
                {
                    panel.Children.Add(new SelectableTextBlock
                    {
                        Text = currentText.ToString(),
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    currentText.Clear();
                }
            }

            void AddLineBreak()
            {
                // Add all current panel content to blocks
                if (panel.Children.Count > 0)
                {
                    blocks.Add(panel);
                    panel = new WrapPanel { Orientation = Orientation.Horizontal };
                }
            }

            while (i < text.Length)
            {
                // Check for line breaks
                if (text[i] == '\n' || text[i] == '\r')
                {
                    FlushText();
                    AddLineBreak();

                    // Skip \r\n or \n\r combinations
                    if (i + 1 < text.Length && (text[i + 1] == '\n' || text[i + 1] == '\r') && text[i] != text[i + 1])
                    {
                        i++;
                    }
                    i++;
                    continue;
                }

                // Bold **text**
                if (i < text.Length - 1 && text[i] == '*' && text[i + 1] == '*')
                {
                    FlushText();
                    i += 2;
                    var boldText = new StringBuilder();
                    while (i < text.Length - 1 && !(text[i] == '*' && text[i + 1] == '*')) { boldText.Append(text[i]); i++; }
                    if (i < text.Length - 1) i += 2;
                    panel.Children.Add(new SelectableTextBlock
                    {
                        Text = boldText.ToString(),
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    continue;
                }

                // Inline code `text`
                if (text[i] == '`')
                {
                    FlushText();
                    i++;
                    var codeText = new StringBuilder();
                    while (i < text.Length && text[i] != '`') { codeText.Append(text[i]); i++; }
                    if (i < text.Length) i++;
                    panel.Children.Add(new SelectableTextBlock
                    {
                        Text = codeText.ToString(),
                        FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                        Foreground = new SolidColorBrush(Color.Parse("#d4d4d4")),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    continue;
                }

                // Links [text](url)
                if (text[i] == '[')
                {
                    var linkMatch = System.Text.RegularExpressions.Regex.Match(text.Substring(i), @"^\[([^\]]+)\]\(([^\)]+)\)");
                    if (linkMatch.Success)
                    {
                        FlushText();
                        var linkText = linkMatch.Groups[1].Value;
                        var linkUrl = linkMatch.Groups[2].Value;

                        var linkButton = new Button
                        {
                            Content = linkText,
                            Foreground = new SolidColorBrush(Color.Parse("#0969da")),
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            Padding = new Thickness(0),
                            Cursor = new Cursor(StandardCursorType.Hand),
                            FontSize = 14,
                            VerticalAlignment = VerticalAlignment.Center,
                            Tag = linkUrl
                        };

                        linkButton.Click += (s, e) =>
                        {
                            if (linkButton.Tag is string url)
                            {
                                try { OpenUrl(url); } catch { }
                            }
                        };

                        panel.Children.Add(linkButton);
                        i += linkMatch.Length;
                        continue;
                    }
                }

                currentText.Append(text[i]);
                i++;
            }

            FlushText();

            if (panel.Children.Count > 0)
            {
                blocks.Add(panel);
            }

            return blocks;
        }

        private void FlushListItems(List<Control> controls, List<string> listItems)
        {
            if (listItems.Count > 0)
            {
                var listPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                foreach (var item in listItems)
                {
                    var blocks = ParseInlineMarkdown(item);
                    foreach (var block in blocks)
                    {
                        block.Margin = new Thickness(0, 2, 0, 2);
                        listPanel.Children.Add(block);
                    }
                }
                controls.Add(listPanel);
                listItems.Clear();
            }
        }

        private async Task<string> FetchChangelogAsync(GameInfo game)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(game.Repository))
                    return "No changelog available for this release.";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "QuiverLauncher");

                if (RepositorySourceHelper.IsGitLab(game.RepositorySource))
                {
                    if (!string.IsNullOrEmpty(_settings?.GitLabApiToken))
                        client.DefaultRequestHeaders.TryAddWithoutValidation("PRIVATE-TOKEN", _settings.GitLabApiToken);

                    var encoded = Uri.EscapeDataString(game.Repository);
                    var url = $"{GitLabReleaseSource.ApiBaseUrl}/projects/{encoded}/releases";
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                        return "Failed to fetch changelog from GitLab.";

                    var json = await response.Content.ReadAsStringAsync();
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Array ||
                        document.RootElement.GetArrayLength() == 0)
                    {
                        return "No changelog available for this release.";
                    }

                    var first = document.RootElement[0];
                    if (first.TryGetProperty("description", out var descriptionElement))
                    {
                        var description = descriptionElement.GetString();
                        if (!string.IsNullOrEmpty(description))
                            return description;
                    }

                    return "No changelog available for this release.";
                }

                if (!string.IsNullOrEmpty(_settings?.GitHubApiToken))
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"token {_settings.GitHubApiToken}");
                }

                var githubUrl = $"https://api.github.com/repos/{game.Repository}/releases/latest";
                var githubResponse = await client.GetAsync(githubUrl);

                if (!githubResponse.IsSuccessStatusCode)
                {
                    return "Failed to fetch changelog from GitHub.";
                }

                var githubJson = await githubResponse.Content.ReadAsStringAsync();
                using var githubDocument = JsonDocument.Parse(githubJson);
                var root = githubDocument.RootElement;

                if (root.TryGetProperty("body", out var bodyElement))
                {
                    var body = bodyElement.GetString();
                    if (!string.IsNullOrEmpty(body))
                    {
                        return body;
                    }
                }

                return "No changelog available for this release.";
            }
            catch (Exception ex)
            {
                return $"Error fetching changelog: {ex.Message}";
            }
        }

        private void CloseChangelog()
        {
            ClearChangelogGamepadFocus();
            _changelogGamepadFocusIndex = -1;

            _isChangelogOpen = false;
            _currentChangelogGame = null;

            // Hide changelog panel
            var changelogPanel = this.FindControl<Border>("ChangelogPanel");
            if (changelogPanel != null)
            {
                changelogPanel.IsVisible = false;
            }

            // Restore sidebar content
            var sidebarContent = this.FindControl<StackPanel>("SidebarContent");
            if (sidebarContent != null)
            {
                sidebarContent.Width = double.NaN;
            }

            // Restore header title
            var headerTitle = this.FindControl<TextBlock>("HeaderTitleText");
            if (headerTitle != null)
            {
                headerTitle.Text = "Library";
            }

            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.ChangelogOverlay)
            {
                _gamepadNavigation.ActiveZone = GetMainContentGamepadZone();
                if (_mainViewMode == MainViewMode.Library)
                    RestoreLibraryGamepadFocusAfterMenu();
                else
                    SelectInitialGamepadItemForCurrentView();
            }

            OnPropertyChanged(nameof(GamepadHintsVisible));
        }

        private async void CreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                string launcherPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(launcherPath))
                {
                    await ShowMessageBoxAsync("Could not determine launcher location.", "Error");
                    return;
                }

                await QuiverLauncher.Services.ShortcutHelper.CreateGameShortcutAsync(
                    game,
                    launcherPath,
                    _gameManager.CacheFolder);

            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to create shortcut: {ex.Message}", "Error");
            }
        }

        private async void AddToSteam_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                string launcherPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(launcherPath))
                {
                    await ShowMessageBoxAsync("Could not determine launcher location.", "Error");
                    return;
                }

                string resultMessage = QuiverLauncher.Services.ShortcutHelper.IsSteamRunning()
                    ? QuiverLauncher.Services.ShortcutHelper.QueueGameAddToSteam(game, launcherPath)
                    : QuiverLauncher.Services.ShortcutHelper.AddGameToSteam(
                        game,
                        launcherPath,
                        _gameManager.CacheFolder);

                await ShowMessageBoxAsync(resultMessage, "Steam Shortcut");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to add the game to Steam: {ex.Message}", "Error");
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }

    public class MarkdownBlock
    {
        public string Type { get; set; } = "paragraph";
        public string Content { get; set; } = "";
        public int Level { get; set; } = 0;
        public List<string> Items { get; set; } = new List<string>();
    }

}

