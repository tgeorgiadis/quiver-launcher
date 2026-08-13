using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace QuiverLauncher.Services.Mods;

public enum ModInstallStatus
{
    NotInstalled,
    Installed,
    UpdateAvailable,
}

public sealed class ModListItem : INotifyPropertyChanged
{
    private bool _isGamepadFocused;
    private ModInstallStatus _status = ModInstallStatus.NotInstalled;
    private string? _installedVersion;
    private int _installedFileCount;
    private IReadOnlyList<InstalledModRecord> _installedRecords = [];
    private IReadOnlyList<ModDownloadFile>? _knownDownloadFiles;
    private bool _isBusy;
    private double _downloadProgress;
    private bool _hasDownloadProgress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required ModPackage Package { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Package.Name)
        ? Package.FullName
        : Package.Name.Replace('_', ' ');

    public string Owner => Package.Owner;

    /// <summary>Author line with optional source in brackets, e.g. "by Owner [Thunderstore]".</summary>
    public string AuthorLine
    {
        get
        {
            var provider = ResolveProviderBadgeText(Package.ProviderId);
            var sourceSuffix = string.IsNullOrEmpty(provider) ? string.Empty : $" [{provider}]";
            if (string.IsNullOrWhiteSpace(Package.Owner))
                return string.IsNullOrEmpty(provider) ? string.Empty : $"[{provider}]";
            return $"by {Package.Owner}{sourceSuffix}";
        }
    }

    public string Description => Package.Description;
    public string DescriptionPreview => Package.Description?.Trim() ?? string.Empty;
    public string? IconUrl => Package.IconUrl;
    public string SourceLabel => Package.SourceDisplayLabel;
    public string LatestVersion => Package.LatestVersion?.Version ?? "—";
    public string ProviderId => Package.ProviderId;
    public string SourceKey => Package.SourceKey;
    public string PackageId => Package.Id;

    /// <summary>Short provider name for card metadata (e.g. Thunderstore).</summary>
    public string ProviderBadgeText => ResolveProviderBadgeText(Package.ProviderId);

    internal static string ResolveProviderBadgeText(string? providerId)
    {
        if (string.Equals(providerId, ModProviderIds.Thunderstore, StringComparison.OrdinalIgnoreCase))
            return "Thunderstore";
        if (string.Equals(providerId, ModProviderIds.GameBanana, StringComparison.OrdinalIgnoreCase))
            return "GameBanana";
        return string.Empty;
    }

    public string DownloadCountText => ModRelativeTime.FormatCompactCount(Package.DownloadCount);
    public string RatingText => ModRelativeTime.FormatCompactCount(Package.RatingScore);
    public string UpdatedText => ModRelativeTime.Format(Package.UpdatedAtUnix);

    public bool HasDownloadCount => Package.DownloadCount > 0;
    public bool HasRatingScore => Package.RatingScore > 0;
    public bool HasUpdatedText => !string.IsNullOrWhiteSpace(UpdatedText);
    public bool HasStatsRow => HasDownloadCount || HasRatingScore || HasUpdatedText;
    public bool HasDescriptionPreview => !string.IsNullOrWhiteSpace(DescriptionPreview);

    public string? InstalledVersion
    {
        get => _installedVersion;
        set
        {
            if (_installedVersion == value)
                return;
            _installedVersion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VersionLine));
        }
    }

    public ModInstallStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(ShowStatusBadge));
            OnPropertyChanged(nameof(HasUpdateAvailable));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUpdate));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(VersionLine));
            OnPropertyChanged(nameof(InstallButtonLabel));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
                return;
            _isBusy = value;
            if (!_isBusy)
            {
                _downloadProgress = 0;
                _hasDownloadProgress = false;
                OnPropertyChanged(nameof(DownloadProgress));
                OnPropertyChanged(nameof(ProgressBarColor));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUpdate));
            OnPropertyChanged(nameof(CanUninstall));
        }
    }

    /// <summary>Install/update download progress, 0–100 (same scale as library game cards).</summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_downloadProgress - clamped) < 0.01 && _hasDownloadProgress)
                return;

            _downloadProgress = clamped;
            _hasDownloadProgress = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressBarColor));
        }
    }

    /// <summary>True while busy before the first determinate progress report (e.g. uninstall).</summary>
    public bool IsProgressIndeterminate => IsBusy && !_hasDownloadProgress;

    public IBrush ProgressBarColor
    {
        get
        {
            var progress = DownloadProgress / 100.0;
            if (Status == ModInstallStatus.UpdateAvailable)
            {
                // Yellow → green (matches library update cards).
                byte r = (byte)(255 - (255 - 52) * progress);
                byte g = (byte)(149 + (199 - 149) * progress);
                byte b = (byte)(0 + (89 - 0) * progress);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }

            // Blue → green (matches library install cards).
            byte br = (byte)(0 + (52 - 0) * progress);
            byte bg = (byte)(122 + (199 - 122) * progress);
            byte bb = (byte)(255 - (255 - 89) * progress);
            return new SolidColorBrush(Color.FromRgb(br, bg, bb));
        }
    }

    public bool IsGamepadFocused
    {
        get => _isGamepadFocused;
        set
        {
            if (_isGamepadFocused == value)
                return;
            _isGamepadFocused = value;
            OnPropertyChanged();
        }
    }

    public string StatusLabel => Status switch
    {
        ModInstallStatus.Installed => "Installed",
        ModInstallStatus.UpdateAvailable => "Update available",
        _ => string.Empty,
    };

    /// <summary>Show status chip only for installed / update-available mods.</summary>
    public bool ShowStatusBadge => Status is ModInstallStatus.Installed or ModInstallStatus.UpdateAvailable;

    public bool HasUpdateAvailable => Status == ModInstallStatus.UpdateAvailable;

    public string VersionLine
    {
        get
        {
            if (Status == ModInstallStatus.NotInstalled)
                return $"v{LatestVersion}";

            if (_installedFileCount > 1)
            {
                return Status == ModInstallStatus.UpdateAvailable
                    ? $"{_installedFileCount} files · update"
                    : $"{_installedFileCount} files";
            }

            var installed = InstalledVersion ?? "?";
            if (Status == ModInstallStatus.Installed ||
                string.Equals(installed, LatestVersion, StringComparison.OrdinalIgnoreCase))
                return $"v{installed}";

            return $"v{installed} → v{LatestVersion}";
        }
    }

    public IReadOnlyList<ModDownloadFile> EffectiveDownloadFiles =>
        _knownDownloadFiles ?? Package.DownloadFiles;

    /// <summary>
    /// GameBanana pages can list several downloads; keep Install visible after the first file
    /// only while at least one catalog file is still missing.
    /// </summary>
    public bool AllowsAdditionalFiles
    {
        get
        {
            if (!string.Equals(Package.ProviderId, ModProviderIds.GameBanana, StringComparison.OrdinalIgnoreCase))
                return false;

            var files = EffectiveDownloadFiles;
            if (files.Count == 0)
                return true;

            return ModDownloadFileSelection.GetUninstalledFiles(files, _installedRecords).Count > 0;
        }
    }

    public bool CanInstall =>
        !IsBusy &&
        (Status == ModInstallStatus.NotInstalled || AllowsAdditionalFiles);

    public void SetKnownDownloadFiles(IReadOnlyList<ModDownloadFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _knownDownloadFiles = files;
        if (_installedRecords.Count > 0)
        {
            // Recompute Installed vs UpdateAvailable now that per-file versions exist.
            ApplyInstalled(_installedRecords);
            return;
        }

        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(AllowsAdditionalFiles));
        OnPropertyChanged(nameof(InstallButtonLabel));
    }

    public string InstallButtonLabel =>
        Status == ModInstallStatus.NotInstalled ? "Install" : "Add files";

    public bool CanUpdate => !IsBusy && Status == ModInstallStatus.UpdateAvailable;
    public bool CanUninstall => !IsBusy && Status is ModInstallStatus.Installed or ModInstallStatus.UpdateAvailable;

    public void ApplyInstalled(InstalledModRecord? record) =>
        ApplyInstalled(record == null ? [] : [record]);

    public void ApplyInstalled(IReadOnlyList<InstalledModRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            _installedFileCount = 0;
            _installedRecords = [];
            InstalledVersion = null;
            Status = ModInstallStatus.NotInstalled;
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(AllowsAdditionalFiles));
            OnPropertyChanged(nameof(VersionLine));
            return;
        }

        _installedFileCount = records.Count;
        _installedRecords = records;
        InstalledVersion = records.Count == 1
            ? records[0].Version
            : $"{records.Count} files";
        var files = EffectiveDownloadFiles;
        Status = records.Any(r => ModDownloadFileSelection.IsRecordUpdateAvailable(r, Package, files))
            ? ModInstallStatus.UpdateAvailable
            : ModInstallStatus.Installed;
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(AllowsAdditionalFiles));
        OnPropertyChanged(nameof(VersionLine));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
