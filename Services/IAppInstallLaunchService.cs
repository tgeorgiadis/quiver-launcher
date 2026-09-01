using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public interface IAppInstallLaunchService
{
    Task<bool> InstallAsync(GameInfo game, string downloadedPackagePath, string version);

    Task<bool> LaunchAsync(GameInfo game, string gamesFolder);

    Task<bool> UninstallAsync(GameInfo game);

    bool IsInstalled(GameInfo game);

    string? GetInstalledVersion(GameInfo game);
}

public static class AppInstallLaunch
{
    public static IAppInstallLaunchService Current { get; set; } = new DesktopAppInstallLaunchService();
}
