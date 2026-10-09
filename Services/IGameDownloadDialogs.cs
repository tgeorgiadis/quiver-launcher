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
    /// Asks before installing a verified release whose download several antivirus engines flag, since the player's
    /// antivirus may block or remove it; false keeps what is installed.
    /// </summary>
    Task<bool> ConfirmFlaggedReleaseAsync(string appName, string version, ReleaseCheck check, bool update) => Task.FromResult(true);
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
    // Left for the player to update by hand, after the warning, rather than broken by their antivirus unseen.
    public Task<bool> ConfirmFlaggedReleaseAsync(string appName, string version, ReleaseCheck check, bool update) => Task.FromResult(false);
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

    public Task<bool> ConfirmFlaggedReleaseAsync(string appName, string version, ReleaseCheck check, bool update) =>
        ReleaseWarnings.ConfirmFlaggedAsync(appName, version, check, update, GameDialogService.ShowQuestionAsync);
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

    // Verified, and asked for on the command line: said, then installed.
    public Task<bool> ConfirmFlaggedReleaseAsync(string appName, string version, ReleaseCheck check, bool update)
    {
        Console.Error.WriteLine($"Warning: {check.ScanEngines ?? "several engines"} on VirusTotal flag " +
            $"{check.ScanFile ?? "one of its files"} ({appName} {version}). Your antivirus may block or remove it.");
        return Task.FromResult(true);
    }
}
