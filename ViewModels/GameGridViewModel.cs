using System.Collections.ObjectModel;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public class GameGridViewModel
{
    public IReadOnlyList<GameInfo> SortGames(
        IEnumerable<GameInfo> games,
        string sortMode,
        string gamesFolder,
        bool ignoreArticlesWhenSorting = true)
    {
        var list = games.Where(g => g != null).Cast<GameInfo>().ToList();
        string NameKey(GameInfo g) => ignoreArticlesWhenSorting
            ? NameSortHelper.GetAlphabeticalSortKey(g.Name)
            : (g.Name ?? string.Empty);

        return sortMode switch
        {
            "NameDesc" => list
                .OrderByDescending(NameKey, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "Installed" => list
                .OrderByDescending(g => g.IsInstalled)
                .ThenBy(NameKey, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "NotInstalled" => list
                .OrderBy(g => g.IsInstalled)
                .ThenBy(NameKey, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "LastPlayed" => list
                .OrderByDescending(g => GetLastPlayedTime(g, gamesFolder))
                .ThenBy(NameKey, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => list
                .OrderBy(NameKey, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    public void ApplySort(
        ObservableCollection<GameInfo> games,
        string sortMode,
        string gamesFolder,
        bool ignoreArticlesWhenSorting = true)
    {
        if (games.Count == 0)
            return;

        var sorted = SortGames(games, sortMode, gamesFolder, ignoreArticlesWhenSorting);
        games.Clear();
        foreach (var game in sorted)
            games.Add(game);
    }

    public static DateTime GetLastPlayedTime(GameInfo game, string gamesFolder)
    {
        if (string.IsNullOrEmpty(game.FolderName))
            return DateTime.MinValue;

        try
        {
            var gamePath = game.GetInstallPath(gamesFolder);
            var lastPlayedPath = Path.Combine(gamePath, "LastPlayed.txt");

            if (File.Exists(lastPlayedPath))
            {
                var content = File.ReadAllText(lastPlayedPath).Trim();
                if (DateTime.TryParseExact(content, "yyyy-MM-dd HH:mm:ss", null,
                        System.Globalization.DateTimeStyles.None, out var lastPlayed))
                    return lastPlayed;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read LastPlayed for {game.Name}: {ex.Message}");
        }

        return DateTime.MinValue;
    }
}
