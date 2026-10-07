using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

/// <summary>
/// Applies and persists an out-of-tree install folder without changing library identity.
/// </summary>
public static class GameInstallLocationService
{
    public static void ApplyLocatedPath(GameInfo game, string selectedPath)
    {
        if (game.IsWindowsInstaller)
            throw new InvalidOperationException("Use Change executable for Windows Installer apps. Their Quiver metadata folder cannot be relocated to the installed program directory.");
        game.InstallPath = selectedPath;
    }

    /// <summary>
    /// Copies <see cref="GameInfo.InstallPath"/> onto the matching saved row.
    /// If no row shares <see cref="GameInfo.InstanceKey"/>, appends <paramref name="game"/>.
    /// </summary>
    public static GameInfo PersistTo(IList<GameInfo> savedApps, GameInfo game)
    {
        var match = savedApps.FirstOrDefault(saved =>
            string.Equals(saved.InstanceKey, game.InstanceKey, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            savedApps.Add(game);
            return game;
        }

        match.InstallPath = game.InstallPath;
        return match;
    }
}
