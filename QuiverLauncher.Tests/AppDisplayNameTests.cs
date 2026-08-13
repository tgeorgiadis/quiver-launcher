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
