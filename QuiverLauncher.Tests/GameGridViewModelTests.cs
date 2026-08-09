using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class GameGridViewModelTests
{
    [Fact]
    public void SortGames_orders_by_name_by_default()
    {
        var games = new[]
        {
            new GameInfo { Name = "Zelda" },
            new GameInfo { Name = "Banjo" },
        };

        var sorted = new GameGridViewModel().SortGames(games, "Name", "/apps", ignoreArticlesWhenSorting: false);
        sorted.Select(g => g.Name).Should().Equal("Banjo", "Zelda");
    }

    [Fact]
    public void SortGames_name_mode_keeps_leading_articles_when_disabled()
    {
        var games = new[]
        {
            new GameInfo { Name = "Zelda" },
            new GameInfo { Name = "The Legend of Zelda" },
            new GameInfo { Name = "Banjo" },
        };

        var sorted = new GameGridViewModel().SortGames(games, "Name", "/apps", ignoreArticlesWhenSorting: false);
        sorted.Select(g => g.Name).Should().Equal("Banjo", "The Legend of Zelda", "Zelda");
    }

    [Fact]
    public void SortGames_name_mode_strips_leading_articles_when_enabled()
    {
        var games = new[]
        {
            new GameInfo { Name = "Zelda" },
            new GameInfo { Name = "The Legend of Zelda" },
            new GameInfo { Name = "A Short Hike" },
            new GameInfo { Name = "Banjo" },
        };

        var sorted = new GameGridViewModel().SortGames(games, "Name", "/apps", ignoreArticlesWhenSorting: true);
        sorted.Select(g => g.Name).Should().Equal("Banjo", "The Legend of Zelda", "A Short Hike", "Zelda");
    }

    [Fact]
    public void GetLastPlayedTime_reads_timestamp_file()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var gameFolder = Path.Combine(tempRoot, "TestGame");
        Directory.CreateDirectory(gameFolder);

        try
        {
            var timestamp = "2024-06-01 12:00:00";
            File.WriteAllText(Path.Combine(gameFolder, "LastPlayed.txt"), timestamp);

            var game = new GameInfo { FolderName = "TestGame" };
            var parsed = GameGridViewModel.GetLastPlayedTime(game, tempRoot);

            parsed.Should().Be(DateTime.ParseExact(timestamp, "yyyy-MM-dd HH:mm:ss", null));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
