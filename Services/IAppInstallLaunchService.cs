using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public interface IAppInstallLaunchService
{
    Task<bool> InstallAsync(GameInfo game, string downloadedPackagePath, string version, string gamePath);

    Task<bool> LaunchAsync(GameInfo game, string gamesFolder);

    Task<bool> UninstallAsync(GameInfo game, string gamePath);

    bool IsInstalled(GameInfo game);

    string? GetInstalledVersion(GameInfo game, string gamePath);
}

public static class AppInstallLaunch
{
    public static IAppInstallLaunchService Current { get; set; } = new DesktopAppInstallLaunchService();
}
