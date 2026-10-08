using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AppDisplayNameTests
{
    [Theory]
    [InlineData(LibraryNameStyle.NameOnly, "Majora's Mask", "2 Ship 2 Harkinian", "Majora's Mask")]
    [InlineData(LibraryNameStyle.NameAndProject, "Majora's Mask", "2 Ship 2 Harkinian", "Majora's Mask")]
    [InlineData(LibraryNameStyle.NameAndProjectInTitle, "Majora's Mask", "2 Ship 2 Harkinian", "Majora's Mask (2 Ship 2 Harkinian)")]
    [InlineData(LibraryNameStyle.ProjectOnly, "Majora's Mask", "2 Ship 2 Harkinian", "2 Ship 2 Harkinian")]
    [InlineData(LibraryNameStyle.NameAndProject, "Banjo-Kazooie", null, "Banjo-Kazooie")]
    [InlineData(LibraryNameStyle.ProjectOnly, "Banjo-Kazooie", null, "Banjo-Kazooie")]
    public void Resolve_composes_from_style(
        LibraryNameStyle style,
        string? name,
        string? project,
        string expected)
    {
        AppDisplayName.Resolve(name, project, customDisplayName: null, style).Should().Be(expected);
    }

    [Fact]
    public void ResolveProjectSubtitle_shows_for_name_and_project_style()
    {
        AppDisplayName.ResolveProjectSubtitle(
                "Banjo-Kazooie",
                "BanjoRecomp",
                customDisplayName: null,
                LibraryNameStyle.NameAndProject)
            .Should().Be("BanjoRecomp");
    }

    [Fact]
    public void Project_and_name_style_titles_the_project_with_the_game_below_as_in_the_catalog()
    {
        AppDisplayName.Resolve("Banjo-Kazooie", "BanjoRecomp", null, LibraryNameStyle.ProjectAndName).Should().Be("BanjoRecomp");
        AppDisplayName.ResolveProjectSubtitle("Banjo-Kazooie", "BanjoRecomp", null, LibraryNameStyle.ProjectAndName)
            .Should().Be("Banjo-Kazooie");
        // No project: the name is the title, with nothing under it.
        AppDisplayName.Resolve("Banjo-Kazooie", null, null, LibraryNameStyle.ProjectAndName).Should().Be("Banjo-Kazooie");
        AppDisplayName.ResolveProjectSubtitle("Banjo-Kazooie", null, null, LibraryNameStyle.ProjectAndName).Should().BeEmpty();
        // The player's own display name still wins.
        AppDisplayName.Resolve("Banjo-Kazooie", "BanjoRecomp", "My Banjo", LibraryNameStyle.ProjectAndName).Should().Be("My Banjo");
        AppDisplayName.ResolveProjectSubtitle("Banjo-Kazooie", "BanjoRecomp", "My Banjo", LibraryNameStyle.ProjectAndName).Should().BeEmpty();
    }

    [Fact]
    public void ResolveProjectSubtitle_hidden_for_in_title_and_custom_name()
    {
        AppDisplayName.ResolveProjectSubtitle(
                "Banjo-Kazooie",
                "BanjoRecomp",
                customDisplayName: null,
                LibraryNameStyle.NameAndProjectInTitle)
            .Should().BeEmpty();

        AppDisplayName.ResolveProjectSubtitle(
                "Banjo-Kazooie",
                "BanjoRecomp",
                "My Banjo",
                LibraryNameStyle.NameAndProject)
            .Should().BeEmpty();
    }

    [Fact]
    public void Resolve_custom_display_name_wins()
    {
        AppDisplayName.Resolve(
                "Majora's Mask",
                "2 Ship 2 Harkinian",
                "My MM",
                LibraryNameStyle.NameAndProject)
            .Should().Be("My MM");
    }
}
