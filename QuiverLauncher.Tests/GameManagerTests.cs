using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GameManagerTests
{
    [Fact]
    public void HideGame_adds_hidden_key_and_filters_collection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var store = new FileSettingsStore(Path.Combine(tempDir, "settings.json"));
        var manager = new GameManager(store);
        var game = new GameInfo
        {
            Name = "Hidden Game",
            FolderName = "HiddenFolder",
            Repository = "owner/hidden",
        };

        manager.Games.Add(game);
        manager.HideGame(game);

        store.Current.ManuallyHiddenApps.Should().Contain("folder:HiddenFolder");
        manager.Games.Should().BeEmpty();

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void IsManuallyHidden_reflects_manual_hide_list()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var store = new FileSettingsStore(Path.Combine(tempDir, "settings.json"));
        var manager = new GameManager(store);
        var game = new GameInfo { Name = "Manual", FolderName = "ManualFolder" };

        manager.ToggleUserHide(game);
        manager.IsManuallyHidden(game).Should().BeTrue();

        Directory.Delete(tempDir, true);
    }
}
