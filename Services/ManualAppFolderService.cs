using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

/// <summary>
/// Creates and inspects install folders for apps that are not downloaded from GitHub/GitLab.
/// </summary>
public static class ManualAppFolderService
{
    public const string InstructionFileName = "Place app files here.txt";

    public static readonly string InstructionContents =
        "Most apps in Quiver Launcher come from GitHub or GitLab Releases, so Quiver Launcher can download them and keep them up to date. This app is not on GitHub or GitLab Releases, so Quiver Launcher cannot download or update it." +
        Environment.NewLine +
        Environment.NewLine +
        "Download the app yourself and put its files in this folder. Quiver Launcher will detect an executable and let you launch it. You will need to keep it up to date yourself." +
        Environment.NewLine +
        Environment.NewLine +
        "If a GitHub or GitLab release becomes available and this app has a repository set, this entry will gain version checks and updates.";

    public static string? GetInstallPath(GameInfo game, string appsFolder)
    {
        var path = game.GetInstallPath(appsFolder);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static bool EnsurePrepared(GameInfo game, string appsFolder)
    {
        var installPath = GetInstallPath(game, appsFolder);
        if (installPath == null)
            return false;

        Directory.CreateDirectory(installPath);

        if (!HasLaunchableFiles(game, appsFolder))
        {
            var instructionPath = Path.Combine(installPath, InstructionFileName);
            if (!File.Exists(instructionPath))
            {
                try
                {
                    File.WriteAllText(instructionPath, InstructionContents);
                }
                catch
                {
                    // Best-effort placeholder; the folder itself is the drop target.
                }
            }
        }

        AppFilesToAddService.SyncForGame(game, appsFolder);
        return true;
    }

    public static bool HasLaunchableFiles(GameInfo game, string appsFolder)
    {
        var installPath = GetInstallPath(game, appsFolder);
        if (installPath == null || !Directory.Exists(installPath))
            return false;

        var executables = GameInstallationService.FindExecutableCandidates(
            installPath,
            SearchOption.AllDirectories,
            game.GetInstallationOptions(),
            out _);
        return executables.Count > 0;
    }
}
