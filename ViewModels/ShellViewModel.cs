using Avalonia.Media;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

/// <summary>Shared screen state and update status; feature data belongs to its own view model.</summary>
public sealed class ShellViewModel : ObservableViewModel
{
    public bool IsMobile => PlatformCapabilities.IsMobile;
    public bool IsDesktopPlatform => !IsMobile;
    public bool ShowMinimizeButton => IsDesktopPlatform && !SteamDeckEnvironment.IsGamingMode();
    private string _backgroundImageUri = "";
    public string BackgroundImageUri { get => _backgroundImageUri; private set => Set(ref _backgroundImageUri, value); }
    private float _backgroundOpacity;
    public float BackgroundOpacity { get => _backgroundOpacity; private set => Set(ref _backgroundOpacity, value); }
    private string _version = "Unknown";
    public string Version { get => _version; set => Set(ref _version, value); }
    private int _catalogReviewBadgeCount;
    public int CatalogReviewBadgeCount { get => _catalogReviewBadgeCount; set { if (Set(ref _catalogReviewBadgeCount, value)) Notify(nameof(CatalogReviewBadgeVisible)); } }
    public bool CatalogReviewBadgeVisible => CatalogReviewBadgeCount > 0;
    public void RefreshPresentation(AppSettings settings)
    {
        var path = settings.BackgroundImagePath;
        BackgroundImageUri = !string.IsNullOrEmpty(path) && System.IO.File.Exists(path) ? new Uri(path).AbsoluteUri : "";
        BackgroundOpacity = settings.BackgroundOpacity;
    }
    private SolidColorBrush _themeColorBrush = new(Colors.Transparent);
    private SolidColorBrush _secondaryColorBrush = new(Colors.Transparent);
    public SolidColorBrush ThemeColorBrush { get => _themeColorBrush; set => Set(ref _themeColorBrush, value); }
    public SolidColorBrush SecondaryColorBrush { get => _secondaryColorBrush; set => Set(ref _secondaryColorBrush, value); }
    private bool _isCheckingUpdates;
    private int _pendingUpdatesCount;
    private DateTime? _lastUpdateCheckTime;
    private string? _lastLauncherCheckNote;
    private string _updateCheckStatus = "";
    public string UpdateCheckStatus { get => _updateCheckStatus; set => Set(ref _updateCheckStatus, value); }
    private string _updateCheckDetails = "";
    public string UpdateCheckDetails
    {
        get => _updateCheckDetails;
        set { if (Set(ref _updateCheckDetails, value)) Notify(nameof(HasUpdateCheckDetails)); }
    }
    public bool HasUpdateCheckDetails => !string.IsNullOrWhiteSpace(UpdateCheckDetails);
    private bool _updateCheckStatusDismissed;
    public bool ShowUpdateCheckStatus => IsCheckingUpdates ||
        (!_updateCheckStatusDismissed && !string.IsNullOrEmpty(LastLauncherCheckNote));

    public void DismissUpdateCheckStatus()
    {
        if (IsCheckingUpdates) return;
        _updateCheckStatusDismissed = true;
        Notify(nameof(ShowUpdateCheckStatus));
    }
    public string? LastLauncherCheckNote
    {
        get => _lastLauncherCheckNote;
        set { if (Set(ref _lastLauncherCheckNote, value)) NotifyUpdateCheckUiProperties(); }
    }
    private MainViewMode _mode = MainViewMode.Library;
    private AppCatalogSubView _catalogSubView = AppCatalogSubView.Sources;
    public MainViewMode Mode { get => _mode; set => Set(ref _mode, value); }
    public AppCatalogSubView CatalogSubView { get => _catalogSubView; set => Set(ref _catalogSubView, value); }
    private bool _appUpdatesOpen;
    public bool AppUpdatesOpen { get => _appUpdatesOpen; set => Set(ref _appUpdatesOpen, value); }
    private bool _settingsOpen;
    public bool SettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }
    private bool _documentOpen;
    public bool DocumentOpen { get => _documentOpen; set => Set(ref _documentOpen, value); }
    private bool _entryEditorOpen;
    public bool EntryEditorOpen { get => _entryEditorOpen; set => Set(ref _entryEditorOpen, value); }
    private bool _tagEditorOpen;
    public bool TagEditorOpen { get => _tagEditorOpen; set => Set(ref _tagEditorOpen, value); }
    private bool _catalogDetailsOpen;
    public bool CatalogDetailsOpen { get => _catalogDetailsOpen; set => Set(ref _catalogDetailsOpen, value); }
    private bool _modsOpen;
    public bool ModsOpen { get => _modsOpen; set => Set(ref _modsOpen, value); }
    private bool _modDetailsOpen;
    public bool ModDetailsOpen { get => _modDetailsOpen; set => Set(ref _modDetailsOpen, value); }
        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set
            {
                if (_isCheckingUpdates == value)
                    return;

                _isCheckingUpdates = value;
                if (value)
                {
                    _updateCheckStatusDismissed = false;
                    UpdateCheckDetails = "";
                }
                Notify(nameof(IsCheckingUpdates));
                NotifyUpdateCheckUiProperties();
            }
        }

        public int PendingUpdatesCount
        {
            get => _pendingUpdatesCount;
            set
            {
                if (_pendingUpdatesCount == value)
                    return;

                _pendingUpdatesCount = value;
                Notify(nameof(PendingUpdatesCount));
                NotifyUpdateCheckUiProperties();
            }
        }

        public bool UpdatesBadgeVisible => PendingUpdatesCount > 0;

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
            set
            {
                if (_lastUpdateCheckTime == value)
                    return;

                _lastUpdateCheckTime = value;
                Notify(nameof(LastUpdateCheckTime));
                NotifyUpdateCheckUiProperties();
            }
        }

        public string CheckForUpdatesToolTip
        {
            get
            {
                if (IsCheckingUpdates)
                    return UpdateCheckStatus;

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

        public void NotifyUpdateCheckUiProperties()
        {
            Notify(nameof(ShowUpdateCheckStatus));
            Notify(nameof(UpdatesBadgeVisible));
            Notify(nameof(UpdatesUpToDateBadgeVisible));
            Notify(nameof(UpdatesBadgeText));
            Notify(nameof(CheckForUpdatesIconOpacity));
            Notify(nameof(CheckForUpdatesToolTip));
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

}
