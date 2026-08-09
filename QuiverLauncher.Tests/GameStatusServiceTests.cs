using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GameStatusServiceTests
{
    [Fact]
    public async Task CheckStatusAsync_marks_missing_folder_as_not_installed()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempRoot);

        try
        {
            var game = new GameInfo
            {
                Name = "Missing",
                FolderName = "MissingFolder",
                Repository = "owner/missing",
            };

            using var httpClient = new HttpClient();
            await GameStatusService.CheckStatusAsync(game, httpClient, tempRoot);

            game.Status.Should().Be(GameStatus.NotInstalled);
            game.IsLoading.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task EnsureInstalledVersionFileAsync_writes_default_version()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");

        try
        {
            var version = await GameStatusService.EnsureInstalledVersionFileAsync(tempFile);
            version.Should().Be("0.0.0");
            File.ReadAllText(tempFile).Trim().Should().Be("0.0.0");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
