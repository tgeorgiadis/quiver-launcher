namespace QuiverLauncher.Services;

public interface IGameDownloadDialogs
{
    Task<bool> ConfirmWindowsInstallerAsync(string appName) =>
        throw new InvalidOperationException("MSI installation requires the Windows desktop interface. Open Quiver and run the installer there.");
    Task<string?> PickWindowsExecutableAsync(string appName, string? previousPath) =>
        throw new InvalidOperationException("Open Quiver's Windows desktop interface and select the installed executable.");
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
    /// <summary>
    /// Offers another release Quiver verified when the files of the one picked are gone; true installs it.
    /// </summary>
    Task<bool> OfferOtherReleaseAsync(string appName, string problem, string version) => Task.FromResult(false);
}

/// <summary>Automatic updates install only verified releases and never ask; a Windows setup wizard never runs on its own.</summary>
public sealed class AutomaticGameDownloadDialogs(IGameDownloadDialogs inner) : IGameDownloadDialogs
{
    public Task<bool> ConfirmWindowsInstallerAsync(string appName) => Task.FromResult(false);
    public Task<bool> ConfirmDownloadWithoutRunnerAsync() => inner.ConfirmDownloadWithoutRunnerAsync();
    public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string gamePath, LinuxWindowsRunnerConfig? existing = null, bool isInstall = true) =>
        inner.ConfigureWindowsRunnerAsync(gamePath, existing, isInstall);
    public Task ShowRateLimitExceededAsync() => inner.ShowRateLimitExceededAsync();
    public Task ShowGitLabRateLimitExceededAsync() => inner.ShowGitLabRateLimitExceededAsync();
    public Task ShowErrorAsync(string message, string title) => inner.ShowErrorAsync(message, title);
    public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check) => Task.FromResult(false);
    // The automatic pick is already the newest verified release; another is the player's choice.
    public Task<bool> OfferOtherReleaseAsync(string appName, string problem, string version) => Task.FromResult(false);
}

public sealed class AvaloniaGameDownloadDialogs : IGameDownloadDialogs
{
    public Task<bool> ConfirmWindowsInstallerAsync(string appName) => GameDialogService.ConfirmWindowsInstallerAsync(appName);
    public Task<string?> PickWindowsExecutableAsync(string appName, string? previousPath) => GameDialogService.PickWindowsExecutableAsync(appName, previousPath);
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

    public Task<bool> OfferOtherReleaseAsync(string appName, string problem, string version) =>
        GameDialogService.ShowQuestionAsync(
            $"{problem}\n\nInstall {version} of {appName} instead? It's the newest version Quiver verified.", "Download Removed");
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

    public Task ShowErrorAsync(string message, string title)
    {
        Console.Error.WriteLine($"{title}: {message}");
        return Task.CompletedTask;
    }

    // Nobody can confirm, so nothing unverified is installed.
    public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check) => Task.FromResult(false);
}
