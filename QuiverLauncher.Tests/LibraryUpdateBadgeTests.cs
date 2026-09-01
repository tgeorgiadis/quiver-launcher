using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;

namespace QuiverLauncher.Tests;

public class LibraryUpdateBadgeTests
{
    [Fact]
    public void ShowUpdateBadge_true_when_catalog_changes_pending_and_badges_enabled()
    {
        var game = new GameInfo
        {
            HasPendingCatalogChanges = true,
            ShowLibraryUpdateBadges = true,
            Status = GameStatus.Installed,
        };

        game.ShowUpdateBadge.Should().BeTrue();
    }

    [Fact]
    public void ShowUpdateBadge_false_for_app_version_update_only()
    {
        var game = new GameInfo
        {
            Status = GameStatus.UpdateAvailable,
            HasPendingCatalogChanges = false,
            ShowLibraryUpdateBadges = true,
        };

        game.ShowUpdateBadge.Should().BeFalse();
    }

    [Fact]
    public void ShowUpdateBadge_false_when_badges_disabled()
    {
        var game = new GameInfo
        {
            HasPendingCatalogChanges = true,
            ShowLibraryUpdateBadges = false,
        };

        game.ShowUpdateBadge.Should().BeFalse();
    }

    [Fact]
    public void CanInfoOptions_true_when_repository_is_set()
    {
        new GameInfo { Repository = "owner/repo" }.CanInfoOptions.Should().BeTrue();
        new GameInfo { Repository = "" }.CanInfoOptions.Should().BeFalse();
        new GameInfo().CanInfoOptions.Should().BeFalse();
    }
}
