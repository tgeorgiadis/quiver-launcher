namespace QuiverLauncher.Services;

public interface IGameDownloadDialogs
{
    Task<bool> ConfirmDownloadWithoutRunnerAsync();
    Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(
        string gamePath,
        LinuxWindowsRunnerConfig? existing = null,
        bool isInstall = true);
    Task ShowRateLimitExceededAsync();
    Task ShowGitLabRateLimitExceededAsync();
    Task ShowErrorAsync(string message, string title);
    /// <summary>Asks before installing a release Quiver hasn't verified or blocked; false keeps what is installed.</summary>
    Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check);
}

/// <summary>Automatic updates install only verified releases and never ask.</summary>
public sealed class AutomaticGameDownloadDialogs(IGameDownloadDialogs inner) : IGameDownloadDialogs
{
    public Task<bool> ConfirmDownloadWithoutRunnerAsync() => inner.ConfirmDownloadWithoutRunnerAsync();
    public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string gamePath, LinuxWindowsRunnerConfig? existing = null, bool isInstall = true) =>
        inner.ConfigureWindowsRunnerAsync(gamePath, existing, isInstall);
    public Task ShowRateLimitExceededAsync() => inner.ShowRateLimitExceededAsync();
    public Task ShowGitLabRateLimitExceededAsync() => inner.ShowGitLabRateLimitExceededAsync();
    public Task ShowErrorAsync(string message, string title) => inner.ShowErrorAsync(message, title);
    public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check) => Task.FromResult(false);
}

public sealed class AvaloniaGameDownloadDialogs : IGameDownloadDialogs
{
    public static AvaloniaGameDownloadDialogs Instance { get; } = new();

    public Task<bool> ConfirmDownloadWithoutRunnerAsync() =>
        GameDialogService.ShowWineNotFoundWarningAsync();

    public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(
        string gamePath,
        LinuxWindowsRunnerConfig? existing = null,
        bool isInstall = true) =>
        GameDialogService.ShowLinuxWindowsRunnerDialogAsync(gamePath, existing, isInstall);

    public Task ShowRateLimitExceededAsync() =>
        GameDialogService.ShowRateLimitErrorAsync();

    public Task ShowGitLabRateLimitExceededAsync() =>
        GameDialogService.ShowGitLabRateLimitErrorAsync();

    public Task ShowErrorAsync(string message, string title) =>
        GameDialogService.ShowMessageBoxAsync(message, title);

    public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check) =>
        ReleaseWarnings.ConfirmAsync(appName, version, check, GameDialogService.ShowQuestionAsync);
}

public sealed class HeadlessGameDownloadDialogs : IGameDownloadDialogs
{
    public static HeadlessGameDownloadDialogs Instance { get; } = new();

    public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(true);

    public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(
        string gamePath,
        LinuxWindowsRunnerConfig? existing = null,
        bool isInstall = true)
    {
        var kind = existing?.Kind ?? WindowsRunnerService.GetPreferredDefaultKind();
        return Task.FromResult<LinuxWindowsRunnerConfig?>(new LinuxWindowsRunnerConfig
        {
            Kind = kind,
            PrefixPath = existing?.PrefixPath ?? WindowsRunnerService.GetDefaultPrefixPathForKind(kind, gamePath),
            ProtonPath = existing?.ProtonPath ?? WindowsRunnerService.ListDetectedProtonInstallations().FirstOrDefault()?.ProtonExecutable,
            CustomLaunchCommand = existing?.CustomLaunchCommand,
        });
    }

    public Task ShowRateLimitExceededAsync() => Task.CompletedTask;

    public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;

    public Task ShowErrorAsync(string message, string title) => Task.CompletedTask;

    // Nobody can confirm, so nothing unverified is installed.
    public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check) => Task.FromResult(false);
}
