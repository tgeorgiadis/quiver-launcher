using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;

namespace QuiverLauncher.Tests;

public class LibraryVersionOptionsTests
{
    [Theory]
    [InlineData(GameStatus.NotInstalled, true)]
    [InlineData(GameStatus.Installed, true)]
    [InlineData(GameStatus.UpdateAvailable, true)]
    [InlineData(GameStatus.Downloading, false)]
    [InlineData(GameStatus.Installing, false)]
    [InlineData(GameStatus.Updating, false)]
    public void Repository_app_can_choose_version_before_or_after_install(GameStatus status, bool available)
    {
        var game = new GameInfo { Repository = "owner/app", Status = status };

        game.CanChangeVersion.Should().Be(available);
        game.CanVersionOptions.Should().Be(available);
    }

    [Fact]
    public void Uninstalled_app_without_repository_has_no_release_picker()
    {
        var game = new GameInfo { Status = GameStatus.NotInstalled };

        game.CanChangeVersion.Should().BeFalse();
        game.CanVersionOptions.Should().BeFalse();
    }
}
