using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class AppUpdateSelection
{
    public static bool IsManualPendingUpdate(GameInfo game) =>
        game.Status == GameStatus.UpdateAvailable && !game.AutoUpdate;

    public static bool IsAutoPendingUpdate(GameInfo game) =>
        game.Status == GameStatus.UpdateAvailable && game.AutoUpdate;

    public static List<GameInfo> GetManualPendingUpdates(IEnumerable<GameInfo> games) =>
        games
            .Where(IsManualPendingUpdate)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<GameInfo> GetAutoPendingUpdates(IEnumerable<GameInfo> games) =>
        games
            .Where(IsAutoPendingUpdate)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static int CountManualPendingUpdates(IEnumerable<GameInfo> games) =>
        games.Count(IsManualPendingUpdate);
}
