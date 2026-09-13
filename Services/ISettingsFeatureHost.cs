using Avalonia.Controls;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public sealed record SettingsFeatureContext(
    Func<AppSettings> Settings,
    SettingsViewModel Model,
    LauncherSession Session,
    LauncherMusicService Music,
    GameManager GameManager,
    Func<InputService?> Input);

public interface ISettingsFeatureHost : IFeatureNavigationHost
{
    void NotifyPresentationChanged();
    void CloseSettings();
    void EditTheme(bool secondary);

    void UpdateGridLayoutVisibility();
    void FitMobileLibraryCardWidth();
    void ApplySorting();
    void ApplyLibraryDisplaySettingsToGames();
    Task ApplyLibraryCatalogPendingBadgesAsync();
    void ApplyTopBanner();
    void ApplyTrayAndBackgroundUpdateSettings();
    void UpdateGamepadHintsBar();
    void UpdateGamepadChromeClass();
    void SelectInitialGamepadItemForCurrentView();
    void NotifyGamepadUiChanged();
    void ClearGamepadFocus();
    void SetIgnoreArticlesWhenSorting(bool enabled);
    void OpenUrl(string url);
    void OpenMenu(Control anchor, ContextMenu menu);
    Task<bool> ShowMessageBoxAsync(string message, string title, bool showCancel = false);
}
