using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagHelperTests
{
    [Fact]
    public void ParseCommaSeparatedTags_normalizes_and_deduplicates()
    {
        var tags = TagHelper.ParseCommaSeparatedTags(" N64, Recomp, n64 , ");

        tags.Should().BeEquivalentTo(["n64", "recomp"]);
    }

    [Fact]
    public void MatchesAnyFilterTags_returns_true_when_any_tag_matches()
    {
        TagHelper.MatchesAnyFilterTags(["n64", "favorites"], ["recomp", "n64"]).Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyFilterTags_returns_false_when_no_tags_match()
    {
        TagHelper.MatchesAnyFilterTags(["n64"], ["recomp", "gc"]).Should().BeFalse();
    }

    [Fact]
    public void MatchesAnyFilterTags_returns_true_when_filter_has_no_tags()
    {
        TagHelper.MatchesAnyFilterTags(["n64"], []).Should().BeTrue();
    }

    [Fact]
    public void MergeTags_combines_and_deduplicates()
    {
        TagHelper.MergeTags(["n64", "recomp"], ["favorites", "N64"])
            .Should()
            .BeEquivalentTo(["n64", "recomp", "favorites"]);
    }

    [Fact]
    public void MatchesAllFilterTags_returns_true_when_app_has_every_tag()
    {
        TagHelper.MatchesAllFilterTags(["n64", "recomp", "favorites"], ["n64", "recomp"]).Should().BeTrue();
    }

    [Fact]
    public void MatchesAllFilterTags_returns_false_when_app_missing_a_tag()
    {
        TagHelper.MatchesAllFilterTags(["n64"], ["n64", "recomp"]).Should().BeFalse();
    }

    [Fact]
    public void MatchesAllFilterTags_returns_true_when_filter_has_no_tags()
    {
        TagHelper.MatchesAllFilterTags(["n64"], []).Should().BeTrue();
    }

    [Fact]
    public void MatchesFilterTags_routes_to_any_or_all()
    {
        var appTags = new[] { "n64", "recomp" };
        var filterTags = new[] { "n64", "gc" };

        TagHelper.MatchesFilterTags(appTags, filterTags, TagFilterMatchMode.Any).Should().BeTrue();
        TagHelper.MatchesFilterTags(appTags, filterTags, TagFilterMatchMode.All).Should().BeFalse();
    }

    [Fact]
    public void MatchesFilterTags_single_tag_behaves_same_for_any_and_all()
    {
        TagHelper.MatchesFilterTags(["n64"], ["n64"], TagFilterMatchMode.Any).Should().BeTrue();
        TagHelper.MatchesFilterTags(["n64"], ["n64"], TagFilterMatchMode.All).Should().BeTrue();
        TagHelper.MatchesFilterTags(["recomp"], ["n64"], TagFilterMatchMode.Any).Should().BeFalse();
        TagHelper.MatchesFilterTags(["recomp"], ["n64"], TagFilterMatchMode.All).Should().BeFalse();
    }

    [Fact]
    public void MatchesDisplayFilter_include_only_matches_include_rules()
    {
        TagHelper.MatchesDisplayFilter(
            ["n64", "recomp"],
            ["n64"],
            TagFilterMatchMode.Any,
            [],
            TagFilterMatchMode.Any).Should().BeTrue();

        TagHelper.MatchesDisplayFilter(
            ["pc"],
            ["n64"],
            TagFilterMatchMode.Any,
            [],
            TagFilterMatchMode.Any).Should().BeFalse();
    }

    [Fact]
    public void MatchesDisplayFilter_include_and_exclude_any_hides_matching_apps()
    {
        TagHelper.MatchesDisplayFilter(
            ["n64"],
            ["n64"],
            TagFilterMatchMode.Any,
            ["ai"],
            TagFilterMatchMode.Any).Should().BeTrue();

        TagHelper.MatchesDisplayFilter(
            ["n64", "ai"],
            ["n64"],
            TagFilterMatchMode.Any,
            ["ai"],
            TagFilterMatchMode.Any).Should().BeFalse();
    }

    [Fact]
    public void MatchesDisplayFilter_exclude_all_requires_every_exclude_tag()
    {
        TagHelper.MatchesDisplayFilter(
            ["n64", "ai"],
            ["n64"],
            TagFilterMatchMode.Any,
            ["ai", "wip"],
            TagFilterMatchMode.All).Should().BeTrue();

        TagHelper.MatchesDisplayFilter(
            ["n64", "ai", "wip"],
            ["n64"],
            TagFilterMatchMode.Any,
            ["ai", "wip"],
            TagFilterMatchMode.All).Should().BeFalse();
    }

    [Fact]
    public void MatchesDisplayFilter_exclude_only_shows_non_matching_apps()
    {
        TagHelper.MatchesDisplayFilter(
            ["pc"],
            [],
            TagFilterMatchMode.Any,
            ["ai"],
            TagFilterMatchMode.Any).Should().BeTrue();

        TagHelper.MatchesDisplayFilter(
            ["ai"],
            [],
            TagFilterMatchMode.Any,
            ["ai"],
            TagFilterMatchMode.Any).Should().BeFalse();
    }

    [Fact]
    public void MatchesDisplayFilter_empty_exclude_does_not_filter_out()
    {
        TagHelper.MatchesDisplayFilter(
            ["n64", "ai"],
            ["n64"],
            TagFilterMatchMode.Any,
            [],
            TagFilterMatchMode.Any).Should().BeTrue();
    }
}
