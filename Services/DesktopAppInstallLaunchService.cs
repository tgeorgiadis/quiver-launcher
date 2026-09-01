using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public sealed class DesktopAppInstallLaunchService : IAppInstallLaunchService
{
    public Task<bool> InstallAsync(GameInfo game, string downloadedPackagePath, string version)
        => Task.FromResult(false);

    public Task<bool> LaunchAsync(GameInfo game, string gamesFolder)
        => GameLaunchService.LaunchAsync(game, gamesFolder);

    public Task<bool> UninstallAsync(GameInfo game)
        => Task.FromResult(false);

    public bool IsInstalled(GameInfo game) => game.IsInstalled;

    public string? GetInstalledVersion(GameInfo game) => game.InstalledVersion;
}
