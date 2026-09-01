using FluentAssertions;
using QuiverLauncher.Models;

namespace QuiverLauncher.Tests;

public class LibraryCardTitleScrollTests
{
    [Fact]
    public void ShouldScrollTitle_false_until_hovered_or_gamepad_focused()
    {
        var game = new GameInfo { TruncateLibraryCardTitles = true };

        game.ShouldScrollTitle.Should().BeFalse();

        game.IsHovered = true;
        game.ShouldScrollTitle.Should().BeTrue();

        game.IsHovered = false;
        game.IsGamepadFocused = true;
        game.ShouldScrollTitle.Should().BeTrue();

        game.IsGamepadFocused = false;
        game.ShouldScrollTitle.Should().BeFalse();
    }

    [Fact]
    public void ShouldScrollTitle_false_when_truncate_is_off()
    {
        var game = new GameInfo
        {
            TruncateLibraryCardTitles = false,
            IsHovered = true,
            IsGamepadFocused = true,
        };

        game.ShouldScrollTitle.Should().BeFalse();
    }

    [Fact]
    public void Project_subtitle_visibility_follows_truncate_and_style()
    {
        var game = new GameInfo
        {
            Name = "Majora's Mask",
            Project = "2 Ship 2 Harkinian",
            TruncateLibraryCardTitles = true,
        };

        game.HasProjectSubtitle.Should().BeTrue();
        game.ShowTruncatedProjectSubtitle.Should().BeTrue();
        game.ShowWrappedProjectSubtitle.Should().BeFalse();

        game.TruncateLibraryCardTitles = false;
        game.ShowTruncatedProjectSubtitle.Should().BeFalse();
        game.ShowWrappedProjectSubtitle.Should().BeTrue();
    }

    [Fact]
    public void Version_labels_and_visibility_follow_truncate()
    {
        var game = new GameInfo
        {
            Repository = "owner/app",
            LatestVersion = "v1.2.3-nightly",
            PreferredVersion = "v1.0.0",
            TruncateLibraryCardTitles = true,
        };

        game.LatestVersionLabel.Should().Be("Latest: v1.2.3-nightly");
        game.PreferredVersionLabel.Should().Be("Preferred: v1.0.0");
        game.ShowTruncatedReleaseVersion.Should().BeTrue();
        game.ShowWrappedReleaseVersion.Should().BeFalse();
        game.ShowTruncatedPreferredVersion.Should().BeTrue();
        game.ShowWrappedPreferredVersion.Should().BeFalse();

        game.TruncateLibraryCardTitles = false;
        game.ShowTruncatedReleaseVersion.Should().BeFalse();
        game.ShowWrappedReleaseVersion.Should().BeTrue();
        game.ShowTruncatedPreferredVersion.Should().BeFalse();
        game.ShowWrappedPreferredVersion.Should().BeTrue();
    }
}
