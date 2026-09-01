using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;

namespace QuiverLauncher.Tests;

public class GameUpdateStatusTests
{
    [Fact]
    public void Same_installed_and_latest_tag_is_not_an_update()
    {
        var game = Installed("v1.0.1", "v1.0.1");

        game.RefreshInstalledStatus();

        game.Status.Should().Be(GameStatus.Installed);
        game.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void V_prefix_equivalent_tags_are_not_an_update()
    {
        var game = Installed("1.0.1", "v1.0.1");

        game.RefreshInstalledStatus();

        game.Status.Should().Be(GameStatus.Installed);
        game.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Sentinel_0_0_0_to_itself_is_not_an_update()
    {
        var game = Installed("v0.0.0", "v0.0.0");

        game.RefreshInstalledStatus();

        game.Status.Should().Be(GameStatus.Installed);
        game.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Sentinel_0_0_0_to_real_latest_still_shows_update()
    {
        var game = Installed("0.0.0", "0.10.5");

        game.RefreshInstalledStatus();

        game.Status.Should().Be(GameStatus.UpdateAvailable);
        game.CanUpdate.Should().BeTrue();
    }

    [Fact]
    public void TryAcknowledgeAlreadyInstalledRelease_clears_update_badge()
    {
        var game = Installed("v1.0.1", "v1.0.1");
        game.Status = GameStatus.UpdateAvailable;

        game.TryAcknowledgeAlreadyInstalledRelease("v1.0.1").Should().BeTrue();

        game.Status.Should().Be(GameStatus.Installed);
        game.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void TryAcknowledgeAlreadyInstalledRelease_treats_v_prefix_as_same()
    {
        var game = Installed("1.0.1", "v1.0.1");
        game.Status = GameStatus.UpdateAvailable;

        game.TryAcknowledgeAlreadyInstalledRelease("v1.0.1").Should().BeTrue();
        game.Status.Should().Be(GameStatus.Installed);
    }

    [Fact]
    public void TryAcknowledgeAlreadyInstalledRelease_leaves_status_when_tag_differs()
    {
        var game = Installed("1.0.0", "v1.1.0");
        game.RefreshInstalledStatus();
        game.Status.Should().Be(GameStatus.UpdateAvailable);

        game.TryAcknowledgeAlreadyInstalledRelease("v1.1.0").Should().BeFalse();
        game.Status.Should().Be(GameStatus.UpdateAvailable);
    }

    private static GameInfo Installed(string installed, string latest) =>
        new()
        {
            Name = "Mario Party 4",
            FolderName = "MarioParty4",
            Repository = "owner/mp4",
            InstalledVersion = installed,
            LatestVersion = latest,
            Status = GameStatus.Installed,
        };
}
