using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public interface IModsFeatureHost : IFeatureNavigationHost
{
    void RefreshShell();
    void ResetNavigation();
    void RestoreLibraryFocus();
    void RestoreCurrentFocus();
    void NotifyHints();
    void OpenUrl(string url);
    Task ShowErrorAsync(string message, string title);
    Task<MessagePromptResult> ChooseAsync(string message, string title);
}

public sealed record ModsFeatureContext(
    GameManager Games,
    Func<AppSettings> Settings,
    SettingsViewModel SettingsModel,
    LauncherSession Session,
    MarkdownRenderer Renderer,
    ShellViewModel Shell);
