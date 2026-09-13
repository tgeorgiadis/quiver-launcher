using QuiverLauncher.Models;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Services;

/// <summary>Android owns the installed package and its data; Quiver owns its release receipts.</summary>
public static class AndroidLibraryUninstall
{
    public static async Task<bool> RunAsync(GameInfo game, string gamePath, IAppInstallLaunchService service)
    {
        GameStatusService.TryRestoreAndroidPackageName(game, gamePath);
        game.IsLoading = true;
        game.Status = GameStatus.Installing;
        try
        {
            if (!await service.UninstallAsync(game, gamePath)) return false;
            if (service.IsInstalled(game))
                throw new InvalidOperationException("Android still reports this app as installed. Its saved information has been kept.");
            AndroidInstalledRelease.Clear(gamePath);
            return true;
        }
        finally
        {
            game.IsLoading = false;
            if (service.IsInstalled(game))
            {
                game.InstalledVersion = service.GetInstalledVersion(game, gamePath) ?? game.InstalledVersion;
                game.Status = GameStatus.Installed;
                game.RefreshInstalledStatus();
            }
            else
            {
                game.InstalledVersion = "";
                game.Status = GameStatus.NotInstalled;
            }
        }
    }
}
