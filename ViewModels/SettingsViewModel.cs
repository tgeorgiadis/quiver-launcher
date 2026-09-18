using System.Collections.ObjectModel;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.ViewModels;

public class SettingsViewModel : ObservableViewModel
{
    public AndroidLauncherUpdater? AndroidUpdates => AndroidLauncherUpdater.Current;
    public bool HasAndroidUpdates => AndroidUpdates != null;
    private readonly ISettingsStore _settingsStore;
    public event Action<SettingsChange>? Changed;
    public event Action<Exception>? SaveFailed;
    public event Action<string>? CredentialsChanged;
    public void SaveApiToken(string provider, string? draft)
    {
        var value = draft?.Trim() ?? "";
        var github = provider == "github";
        var previous = github ? Current.GitHubApiToken : Current.GitLabApiToken;
        if (previous == value) return;
        if (github) Current.GitHubApiToken = value; else Current.GitLabApiToken = value;
        try { Save(Current); }
        catch (Exception ex)
        {
            if (github) Current.GitHubApiToken = previous; else Current.GitLabApiToken = previous;
            SaveFailed?.Invoke(ex);
            return;
        }
        CredentialsChanged?.Invoke(provider);
    }


    private void Change<T>(T oldValue, T newValue, Action<AppSettings> apply, SettingsChange change)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
        apply(Current);
        Commit(change);
    }

    private void Commit(SettingsChange change)
    {
        try { Save(Current); }
        catch (Exception ex) { SaveFailed?.Invoke(ex); }
        Changed?.Invoke(change);
    }
    private AppSettings? _fallbackSettings;
    private CancellationTokenSource? _pathEdit;
    private readonly SemaphoreSlim _pathGate = new(1, 1);
    private Func<string, CancellationToken, Task<bool>>? _confirmPath;
    private Func<string, CancellationToken, Task>? _applyPath;

    public void ConfigurePathChanges(
        Func<string, CancellationToken, Task<bool>> confirmPath,
        Func<string, CancellationToken, Task> applyPath)
    {
        _confirmPath = confirmPath;
        _applyPath = applyPath;
    }

    public void CancelPathChange() => _pathEdit?.Cancel();

    public async Task<bool> ChangeAppsPathAsync(string path, CancellationToken cancellationToken, bool debounce = true)
    {
        var previous = _pathEdit;
        using var edit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pathEdit = edit;
        previous?.Cancel();
        try
        {
            if (debounce) await Task.Delay(500, edit.Token);
            await _pathGate.WaitAsync(edit.Token);
            try
            {
                edit.Token.ThrowIfCancellationRequested();
                path = path.Trim();
                if (Current.AppsPath == path) return false;
                if (_confirmPath == null || _applyPath == null)
                    throw new InvalidOperationException("Settings path operations have not been configured.");
                if (!await _confirmPath(path, edit.Token)) return false;
                edit.Token.ThrowIfCancellationRequested();
                await _applyPath(path, edit.Token);
                edit.Token.ThrowIfCancellationRequested();
                Current.AppsPath = path;
                Save(Current);
                return true;
            }
            finally { _pathGate.Release(); }
        }
        catch (OperationCanceledException) when (edit.IsCancellationRequested) { return false; }
        finally
        {
            if (ReferenceEquals(_pathEdit, edit)) _pathEdit = null;
        }
    }

    public SettingsViewModel(ISettingsStore? settingsStore = null)
    {
        _settingsStore = settingsStore ?? SettingsStoreProvider.Default;
    }

    public AppSettings Current => _fallbackSettings ?? _settingsStore.Current;
    public AppSettings Settings => Current;

    internal void ReplaceCurrent(AppSettings settings) =>
        _fallbackSettings = ReferenceEquals(settings, _settingsStore.Current) ? null : settings;

    public string BackgroundImagePath
    {
        get => Current.BackgroundImagePath ?? string.Empty;
        set { Current.BackgroundImagePath = value; Notify(); }
    }
    public float BackgroundOpacity
    {
        get => Current.BackgroundOpacity;
        set { Current.BackgroundOpacity = value; Notify(); }
    }
    public string LauncherMusicPath
    {
        get => Current.LauncherMusicPath ?? string.Empty;
        set { Current.LauncherMusicPath = value; Notify(); }
    }
    public float MusicVolume
    {
        get => Current.MusicVolume;
        set { Current.MusicVolume = value; Notify(); }
    }
    public TargetOS Platform
    {
        get => Current.Platform;
        set => Change(Current.Platform, value, s => s.Platform = value, SettingsChange.Presentation);
    }
    public string PlatformString => Platform switch
    {
        TargetOS.Auto => "Automatic", TargetOS.Windows => "Windows", TargetOS.MacOS => "macOS",
        TargetOS.LinuxX64 => "Linux x64", TargetOS.LinuxARM64 => "Linux ARM64", TargetOS.Android => "Android", _ => "Unknown",
    };
    public bool IsDesktopPlatform => !PlatformCapabilities.IsMobile;
    public bool IsLinuxPlatform => IsDesktopPlatform && PlatformString.Contains("Linux", StringComparison.OrdinalIgnoreCase);
    public bool ShowStartFullscreenSetting => IsDesktopPlatform && !SteamDeckEnvironment.IsDesktopMode();

    public AppSettings Load()
    {
        var settings = _settingsStore.Load();
        settings.ListRowHeight = Math.Clamp(settings.ListRowHeight ?? (settings.UseGridView ? 96 : settings.SlotSize), 72, 400);
        _fallbackSettings = null;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        _settingsStore.Save(settings);
        _fallbackSettings = null;
        Refresh();
    }

    public void Refresh() => Notify(null);

    public void SaveCurrent() => Save(Current);
    // Catalog bookkeeping does not change display settings or credentials.
    public void SaveCatalogState() => _settingsStore.Save(Current);

    // Window movement must not refresh bindings or reapply interface scaling.
    internal void SaveWindowPlacement(DesktopWindowPlacement placement)
    {
        Current.DesktopWindowPlacement = placement;
        _settingsStore.Save(Current);
    }

    public void ApplyCardLayout(CardLayoutPreset preset, bool persist = true)
    {
        var settings = Current;
        if (preset == CardLayoutPreset.List)
        {
            settings.UseGridView = false;
            settings.ListRowHeight = 96;
            if (persist) Commit(SettingsChange.Layout | SettingsChange.LibraryDisplay);
            else Refresh();
            return;
        }
        settings.IconFill = false;
        settings.UseGridView = true;
        settings.IconOpacity = 1.0f;
        settings.IconMargin = 0;
        settings.SlotTextMargin = 0;
        settings.GridCompactCards = preset == CardLayoutPreset.SquareCompact;
        (settings.SlotSize, settings.IconSize) = preset switch
        {
            CardLayoutPreset.Landscape => (304, 220),
            CardLayoutPreset.Portrait => (144, 200),
            CardLayoutPreset.Square => (180, 124),
            CardLayoutPreset.SquareCompact => (188, 128),
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
        if (preset is CardLayoutPreset.Square or CardLayoutPreset.SquareCompact) settings.ActionButtonSize = 36;
        if (preset == CardLayoutPreset.SquareCompact)
        {
            settings.TruncateLibraryCardTitles = true;
            settings.LibraryCardTagMaxLines = 0;
        }
        if (persist) Commit(SettingsChange.Layout | SettingsChange.LibraryDisplay);
        else Refresh();
    }

    public int MouseWheelScrollSpeedIndex
    {
        get => MouseWheelScrollSpeed switch { 2 => 1, 3 => 2, 5 => 3, _ => 0 };
        set { if (value is >= 0 and <= 3) MouseWheelScrollSpeed = value switch { 1 => 2, 2 => 3, 3 => 5, _ => 1 }; }
    }
    public int MouseWheelScrollSpeed
    {
        get => Current.MouseWheelScrollSpeed is 2 or 3 or 5 ? Current.MouseWheelScrollSpeed : 1;
        set
        {
            var speed = value is 2 or 3 or 5 ? value : 1;
            Change(Current.MouseWheelScrollSpeed, speed, s => s.MouseWheelScrollSpeed = speed, SettingsChange.Presentation);
            Notify(nameof(MouseWheelScrollSpeed));
            Notify(nameof(MouseWheelScrollSpeedIndex));
        }
    }

    public bool CloseAfterLaunch
    {
        get => Current.CloseAfterLaunch;
        set => Change(Current.CloseAfterLaunch, value, s => s.CloseAfterLaunch = value, SettingsChange.Presentation);
    }
    public bool CloseToTray
    {
        get => Current.CloseToTray;
        set => Change(Current.CloseToTray, value, s => s.CloseToTray = value, SettingsChange.Tray);
    }
    public bool IgnoreArticlesWhenSorting
    {
        get => Current.IgnoreArticlesWhenSorting;
        set => Change(Current.IgnoreArticlesWhenSorting, value, s => s.IgnoreArticlesWhenSorting = value, SettingsChange.Sorting);
    }
    public bool AutoUpdateNewlyAddedApps
    {
        get => Current.AutoUpdateNewlyAddedApps;
        set => Change(Current.AutoUpdateNewlyAddedApps, value, s => s.AutoUpdateNewlyAddedApps = value, SettingsChange.Presentation);
    }
    public bool BackgroundUpdateCheckEnabled
    {
        get => Current.BackgroundUpdateCheckEnabled;
        set => Change(Current.BackgroundUpdateCheckEnabled, value, s => s.BackgroundUpdateCheckEnabled = value, SettingsChange.Tray);
    }
    public bool PromptCatalogUpdates
    {
        get => Current.PromptCatalogUpdates;
        set => Change(Current.PromptCatalogUpdates, value, s => s.PromptCatalogUpdates = value, SettingsChange.Presentation);
    }
    public bool PromptAppUpdateReviews
    {
        get => Current.PromptAppUpdateReviews;
        set => Change(Current.PromptAppUpdateReviews, value, s => s.PromptAppUpdateReviews = value, SettingsChange.Presentation);
    }
    public bool ShowLibraryAppUpdateBadges
    {
        get => Current.ShowLibraryAppUpdateBadges;
        set => Change(Current.ShowLibraryAppUpdateBadges, value, s => s.ShowLibraryAppUpdateBadges = value, SettingsChange.LibraryDisplay | SettingsChange.Badges);
    }
    public bool TruncateLibraryCardTitles
    {
        get => Current.TruncateLibraryCardTitles;
        set => Change(Current.TruncateLibraryCardTitles, value, s => s.TruncateLibraryCardTitles = value, SettingsChange.LibraryDisplay);
    }
    public bool AllowPrereleaseLauncherUpdates
    {
        get => Current.AllowPrereleaseLauncherUpdates;
        set => Change(Current.AllowPrereleaseLauncherUpdates, value, s => s.AllowPrereleaseLauncherUpdates = value, SettingsChange.Presentation);
    }
    public bool StartFullscreen
    {
        get => Current.StartFullscreen;
        set => Change(Current.StartFullscreen, value, s => s.StartFullscreen = value, SettingsChange.Presentation);
    }
    public bool ShowOSTopBar
    {
        get => Current.ShowOSTopBar;
        set => Change(Current.ShowOSTopBar, value, s => s.ShowOSTopBar = value, SettingsChange.Presentation);
    }

    public bool DesktopSidebarCollapsed
    {
        get => Current.DesktopSidebarCollapsed;
        set
        {
            Change(Current.DesktopSidebarCollapsed, value, s => s.DesktopSidebarCollapsed = value, SettingsChange.Presentation);
            Notify();
        }
    }

    public IReadOnlyList<int> InterfaceScaleOptions => InterfaceScale.Percentages;
    public int InterfaceScalePercent
    {
        get => InterfaceScale.Normalize(Current.InterfaceScalePercent);
        set
        {
            Change(Current.InterfaceScalePercent, InterfaceScale.Normalize(value),
                s => s.InterfaceScalePercent = InterfaceScale.Normalize(value), SettingsChange.Presentation);
            Notify();
            Notify(nameof(InterfaceScaleIndex));
            Notify(nameof(InterfaceScaleNotice));
        }
    }
    public int InterfaceScaleIndex
    {
        get => InterfaceScaleOptions.ToList().IndexOf(InterfaceScalePercent);
        set { if (value >= 0 && value < InterfaceScaleOptions.Count) InterfaceScalePercent = InterfaceScaleOptions[value]; }
    }
    private int _appliedInterfaceScalePercent = 100;
    public string InterfaceScaleNotice => _appliedInterfaceScalePercent == InterfaceScalePercent ? string.Empty
        : $"Using {_appliedInterfaceScalePercent}% to fit this display. Selected: {InterfaceScalePercent}%.";
    public bool ShowInterfaceScaleNotice => _appliedInterfaceScalePercent != InterfaceScalePercent;
    internal void SetAppliedInterfaceScale(int percent)
    {
        _appliedInterfaceScalePercent = percent;
        Notify(nameof(InterfaceScaleNotice));
        Notify(nameof(ShowInterfaceScaleNotice));
    }
    public bool UseGridView
    {
        get => Current.UseGridView;
        set
        {
            Current.ListRowHeight ??= Math.Clamp(Current.UseGridView ? 96 : Current.SlotSize, 72, 400);
            Change(Current.UseGridView, value, s => s.UseGridView = value, SettingsChange.Layout);
        }
    }
    public bool GridCompactCards
    {
        get => Current.GridCompactCards;
        set => Change(Current.GridCompactCards, value, s => s.GridCompactCards = value, SettingsChange.Layout);
    }
    public bool IconFill
    {
        get => Current.IconFill;
        set => Change(Current.IconFill, value, s => s.IconFill = value, SettingsChange.Presentation);
    }
    public bool EnableGamepadInput
    {
        get => Current.EnableGamepadInput;
        set => Change(Current.EnableGamepadInput, value, s => s.EnableGamepadInput = value, SettingsChange.Input);
    }
    public int SlotSize
    {
        get => Current.SlotSize;
        set => Change(Current.SlotSize, value, s => s.SlotSize = value, SettingsChange.Layout);
    }
    public int ListRowHeight
    {
        get => Math.Clamp(Current.ListRowHeight ?? (Current.UseGridView ? 96 : Current.SlotSize), 72, 400);
        set
        {
            var height = (int)Math.Round(Math.Clamp(value, 72, 400) / 4.0) * 4;
            Change(Current.ListRowHeight, (int?)height, s => s.ListRowHeight = height, SettingsChange.Layout);
        }
    }
    public string CardSizeLabel => UseGridView ? "CARD SIZE" : "ROW HEIGHT";
    public int DisplayCardSize => UseGridView ? SlotSize : ListRowHeight;
    public string ImageFillDescription => UseGridView ? "Stretch images to fill entire cards" : "Crop images to fill their thumbnails";
    public int ActionButtonSize
    {
        get => Current.ActionButtonSize;
        set => Change(Current.ActionButtonSize, value, s => s.ActionButtonSize = value, SettingsChange.Presentation);
    }
    public float IconOpacity
    {
        get => Current.IconOpacity;
        set => Change(Current.IconOpacity, value, s => s.IconOpacity = value, SettingsChange.Presentation);
    }
    public int IconSize
    {
        get => Current.IconSize;
        set => Change(Current.IconSize, value, s => s.IconSize = value, SettingsChange.Presentation);
    }
    public int IconMargin
    {
        get => Current.IconMargin;
        set => Change(Current.IconMargin, value, s => s.IconMargin = value, SettingsChange.Presentation);
    }
    public int SlotTextMargin
    {
        get => Current.SlotTextMargin;
        set => Change(Current.SlotTextMargin, value, s => s.SlotTextMargin = value, SettingsChange.Presentation);
    }
}
