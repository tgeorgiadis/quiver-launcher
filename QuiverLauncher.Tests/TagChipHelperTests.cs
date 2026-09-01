using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagChipHelperTests
{
    [Fact]
    public void RankTagsByFrequency_prefers_pinned_then_featured_then_frequency()
    {
        var ranked = TagChipHelper.RankTagsByFrequency(
            [
                ["recomp", "n64", "zelda"],
                ["recomp", "n64"],
                ["decomp", "n64"],
                ["cli", "utility"],
            ],
            featuredTags: ["decomp", "ai"],
            pinnedTags: ["cli"],
            maxChips: 5);

        ranked.Should().StartWith("cli");
        ranked.Should().Contain("decomp");
        ranked.Should().Contain("recomp");
        ranked.Should().NotContain("ai");
    }

    [Fact]
    public void RankTagsByFrequency_omits_hidden_tags_even_when_pinned_or_featured()
    {
        var ranked = TagChipHelper.RankTagsByFrequency(
            [
                ["n64", "recomp", "zelda"],
                ["n64", "recomp"],
                ["n64", "decomp"],
                ["cli"],
            ],
            featuredTags: ["decomp"],
            pinnedTags: ["n64"],
            hiddenTags: ["n64", "zelda"],
            maxChips: 5);

        ranked.Should().NotContain("n64");
        ranked.Should().NotContain("zelda");
        ranked.Should().StartWith("decomp");
        ranked.Should().Contain("recomp");
        ranked.Should().Contain("cli");
    }

    [Fact]
    public void RankTagsByFrequency_orders_preferred_group_by_frequency_then_the_rest()
    {
        var ranked = TagChipHelper.RankTagsByFrequency(
            [
                ["n64", "recomp", "translation"],
                ["n64", "recomp"],
                ["n64", "recomp"],
                ["n64", "decomp"],
            ],
            featuredTags: ["translation", "decomp", "recomp"],
            hiddenTags: ["n64"],
            maxChips: 5);

        ranked.Should().Equal("recomp", "decomp", "translation");
    }

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
    public void MatchesTriStateChips_supports_include_and_exclude()
    {
        var states = new Dictionary<string, TagChipState>(StringComparer.OrdinalIgnoreCase)
        {
            ["recomp"] = TagChipState.Include,
            ["ai"] = TagChipState.Exclude,
        };

        TagChipHelper.MatchesTriStateChips(["recomp", "n64"], states).Should().BeTrue();
        TagChipHelper.MatchesTriStateChips(["recomp", "ai"], states).Should().BeFalse();
        TagChipHelper.MatchesTriStateChips(["decomp"], states).Should().BeFalse();
    }

    [Fact]
    public void CycleState_cycles_neutral_include_exclude()
    {
        TagChipHelper.CycleState(TagChipState.Neutral).Should().Be(TagChipState.Include);
        TagChipHelper.CycleState(TagChipState.Include).Should().Be(TagChipState.Exclude);
        TagChipHelper.CycleState(TagChipState.Exclude).Should().Be(TagChipState.Neutral);
    }

    [Fact]
    public void LibraryCardTagMaxLines_defaults_to_two_and_normalizes()
    {
        var settings = new AppSettings();
        settings.LibraryCardTagMaxLines.Should().Be(2);
        settings.LibraryCardTagZeroMeansHidden.Should().BeTrue();
        settings.EnsureInitialized();
        settings.LibraryCardTagMaxLines.Should().Be(2);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(-1).Should().Be(0);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(8).Should().Be(8);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(99)
            .Should().Be(TagChipHelper.UnlimitedLibraryCardTagMaxLines);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(100)
            .Should().Be(TagChipHelper.UnlimitedLibraryCardTagMaxLines);
    }

    [Fact]
    public void GetLibraryCardTagsMaxHeight_zero_hides_and_ninety_nine_is_unlimited()
    {
        TagChipHelper.GetLibraryCardTagsMaxHeight(0).Should().Be(0);
        TagChipHelper.GetLibraryCardTagsMaxHeight(2)
            .Should().Be(2 * TagChipHelper.LibraryCardTagLineHeight);
        TagChipHelper.GetLibraryCardTagsMaxHeight(TagChipHelper.UnlimitedLibraryCardTagMaxLines)
            .Should().Be(double.PositiveInfinity);
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
    public void AppCatalogService_RefreshLibraryCardTags_uses_each_apps_own_tags()
    {
        var pokemon = new GameInfo { Tags = ["recreation", "gb", "pokemon", "nintendo", "game boy"] };
        var other = new GameInfo { Tags = ["nintendo", "n64"] };
        var settings = new AppSettings { LibraryCardTagMaxLines = 2 };

        AppCatalogService.RefreshLibraryCardTags([pokemon, other], settings);

        pokemon.LibraryCardTags.Should().Equal("recreation", "gb", "pokemon", "nintendo", "game boy");
        other.LibraryCardTags.Should().Equal("nintendo", "n64");
    }

    [Fact]
    public void AppCatalogService_RefreshLibraryCardTags_hides_when_max_lines_is_zero()
    {
        var app = new GameInfo { Tags = ["recreation", "gb", "pokemon"] };
        var settings = new AppSettings
        {
            LibraryCardTagMaxLines = 0,
            LibraryCardTagZeroMeansHidden = true,
        };

        AppCatalogService.RefreshLibraryCardTags([app], settings);

        app.LibraryCardTags.Should().BeEmpty();
        app.LibraryCardTagMaxLines.Should().Be(0);
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
