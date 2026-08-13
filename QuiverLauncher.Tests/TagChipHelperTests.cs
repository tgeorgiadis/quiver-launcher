using FluentAssertions;
using QuiverLauncher;
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
    public void SelectTagsForCardDisplay_respects_modes()
    {
        var tags = new[] { "recomp", "n64", "zelda" };
        var featured = new[] { "recomp", "decomp" };

        TagChipHelper.SelectTagsForCardDisplay(tags, LibraryTagDisplayMode.Hidden, featured)
            .Should().BeEmpty();
        TagChipHelper.SelectTagsForCardDisplay(tags, LibraryTagDisplayMode.All, featured)
            .Should().Equal("recomp", "n64", "zelda");
        TagChipHelper.SelectTagsForCardDisplay(tags, LibraryTagDisplayMode.Featured, featured)
            .Should().Equal("recomp");
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
        new AppSettings().LibraryCardTagMaxLines.Should().Be(2);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(-1).Should().Be(0);
        TagChipHelper.NormalizeLibraryCardTagMaxLines(99)
            .Should().Be(TagChipHelper.MaxLibraryCardTagMaxLines);
    }

    [Fact]
    public void GetLibraryCardTagsMaxHeight_unlimited_when_zero()
    {
        TagChipHelper.GetLibraryCardTagsMaxHeight(0).Should().Be(double.PositiveInfinity);
        TagChipHelper.GetLibraryCardTagsMaxHeight(2)
            .Should().Be(2 * TagChipHelper.LibraryCardTagLineHeight);
    }
}
