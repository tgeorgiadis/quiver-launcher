using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagChipHelperTests
{
    [Fact]
    public void SelectTagsForCardDisplay_returns_app_tags_unless_hidden()
    {
        var tags = new[] { "recomp", "n64", "zelda" };

        TagChipHelper.SelectTagsForCardDisplay(tags, 0).Should().BeEmpty();
        TagChipHelper.SelectTagsForCardDisplay(tags, 2).Should().Equal("recomp", "n64", "zelda");
        TagChipHelper.SelectTagsForCardDisplay(tags, TagChipHelper.UnlimitedLibraryCardTagMaxLines)
            .Should().Equal("recomp", "n64", "zelda");
    }

    [Fact]
    public void LibraryCardTagMaxLines_defaults_to_one_and_normalizes()
    {
        var settings = new AppSettings();
        settings.LibraryCardTagMaxLines.Should().Be(1);
        settings.LibraryCardTagZeroMeansHidden.Should().BeTrue();
        settings.EnsureInitialized();
        settings.LibraryCardTagMaxLines.Should().Be(1);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(-1).Should().Be(0);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(8).Should().Be(8);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(99)
            .Should().Be(TagChipHelper.UnlimitedLibraryCardTagMaxLines);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(100)
            .Should().Be(TagChipHelper.UnlimitedLibraryCardTagMaxLines);
    }

    [Fact]
    public void RefreshLibraryCardTags_keeps_all_app_tags_when_clipped_by_line_height()
    {
        var app = new GameInfo
        {
            Tags = ["recreation", "gb", "pokemon", "nintendo", "game boy"],
            LibraryCardTagMaxLines = 2,
        };

        app.RefreshLibraryCardTags();

        app.LibraryCardTags.Should().Equal("recreation", "gb", "pokemon", "nintendo", "game boy");
        app.LibraryCardTagsMaxHeight.Should().Be(2 * TagChipHelper.LibraryCardTagLineHeight);
    }

    [Fact]
    public void RefreshLibraryCardTags_clears_card_tags_when_max_lines_is_zero()
    {
        var app = new GameInfo
        {
            Tags = ["recreation", "gb", "pokemon", "nintendo", "game boy"],
            LibraryCardTagMaxLines = 0,
        };

        app.RefreshLibraryCardTags();

        app.LibraryCardTags.Should().BeEmpty();
        app.HasLibraryCardTags.Should().BeFalse();
        app.LibraryCardTagsMaxHeight.Should().Be(0);
    }

    [Fact]
    public void EnsureInitialized_migrates_hidden_mode_to_zero_lines()
    {
        var settings = new AppSettings
        {
            LibraryTagDisplayMode = LibraryTagDisplayMode.Hidden,
            LibraryCardTagMaxLines = 2,
            LibraryCardTagZeroMeansHidden = false,
        };

        settings.EnsureInitialized();

        settings.LibraryCardTagMaxLines.Should().Be(0);
        settings.LibraryCardTagZeroMeansHidden.Should().BeTrue();

        settings.EnsureInitialized();
        settings.LibraryCardTagMaxLines.Should().Be(0);
    }

    [Fact]
    public void EnsureInitialized_migrates_old_unlimited_zero_to_ninety_nine()
    {
        var settings = new AppSettings
        {
            LibraryTagDisplayMode = LibraryTagDisplayMode.Featured,
            LibraryCardTagMaxLines = 0,
            LibraryCardTagZeroMeansHidden = false,
        };

        settings.EnsureInitialized();

        settings.LibraryCardTagMaxLines.Should().Be(TagChipHelper.UnlimitedLibraryCardTagMaxLines);
        settings.LibraryCardTagZeroMeansHidden.Should().BeTrue();
    }
}
