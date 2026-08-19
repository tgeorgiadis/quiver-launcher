using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReviewSearchTests
{
    [Fact]
    public void Matches_empty_query_matches_all()
    {
        var row = CreateRow(name: "Ocarina of Time", repository: "owner/oot");

        CatalogReviewSearch.Matches(row, null).Should().BeTrue();
        CatalogReviewSearch.Matches(row, "").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "   ").Should().BeTrue();
    }

    [Fact]
    public void Matches_name_project_repo_folder_and_tags()
    {
        var row = CreateRow(
            name: "Ocarina of Time",
            project: "Zelda64Recomp",
            repository: "Zelda64Recomp/Zelda64Recomp",
            folderName: "OcarinaOfTime-Zelda64Recomp",
            tags: ["n64", "recomp", "zelda"]);

        CatalogReviewSearch.Matches(row, "ocarina").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "zelda64").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "Zelda64Recomp/Zelda64Recomp").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "OcarinaOfTime-Zelda64Recomp").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "recomp").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "mario").Should().BeFalse();
    }

    [Fact]
    public void Matches_custom_and_composed_display_name()
    {
        var row = new CatalogSyncRowItem
        {
            Repository = "owner/game",
            DisplayName = "Majora's Mask (2 Ship 2 Harkinian)",
            External = new GameInfo
            {
                Name = "Majora's Mask",
                Project = "2 Ship 2 Harkinian",
                CustomDisplayName = "MM 2S2H",
                Repository = "owner/game",
                FolderName = "MajorasMask-2S2H",
            },
        };

        CatalogReviewSearch.Matches(row, "2 ship").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "mm 2s2h").Should().BeTrue();
    }

    [Fact]
    public void Matches_requires_every_token()
    {
        var row = CreateRow(
            name: "Ocarina of Time",
            tags: ["recomp", "n64"]);

        CatalogReviewSearch.Matches(row, "zelda recomp").Should().BeFalse();
        CatalogReviewSearch.Matches(row, "ocarina recomp").Should().BeTrue();
    }

    [Fact]
    public void Matches_hidden_from_chips_tags_still_searchable()
    {
        var row = CreateRow(
            name: "Banjo-Kazooie",
            tags: ["n64", "nintendo", "recomp"]);

        CatalogReviewSearch.Matches(row, "nintendo").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "n64").Should().BeTrue();
    }

    [Fact]
    public void Matches_local_side_when_external_missing_field()
    {
        var row = new CatalogSyncRowItem
        {
            Repository = "owner/local",
            DisplayName = "Local App",
            Local = new GameInfo
            {
                Name = "Local App",
                Project = "Harbour Masters",
                Repository = "owner/local",
                FolderName = "LocalApp",
                Tags = ["decomp"],
            },
        };

        CatalogReviewSearch.Matches(row, "harbour").Should().BeTrue();
        CatalogReviewSearch.Matches(row, "decomp").Should().BeTrue();
    }

    [Fact]
    public void Matches_game_empty_query_matches_all()
    {
        var app = new GameInfo { Name = "Ocarina of Time", FolderName = "OoT" };

        CatalogReviewSearch.Matches(app, null).Should().BeTrue();
        CatalogReviewSearch.Matches(app, "").Should().BeTrue();
        CatalogReviewSearch.Matches(app, "   ").Should().BeTrue();
        CatalogReviewSearch.Matches((GameInfo?)null, "ocarina").Should().BeFalse();
        CatalogReviewSearch.Matches((GameInfo?)null, "").Should().BeTrue();
    }

    [Fact]
    public void Matches_game_name_tags_and_display_name()
    {
        var app = new GameInfo
        {
            Name = "Ocarina of Time",
            Project = "Zelda64Recomp",
            CustomDisplayName = "OoT Recomp",
            FolderName = "OcarinaOfTime-Zelda64Recomp",
            Repository = "Zelda64Recomp/Zelda64Recomp",
            Tags = ["n64", "recomp", "zelda"],
        };

        CatalogReviewSearch.Matches(app, "ocarina").Should().BeTrue();
        CatalogReviewSearch.Matches(app, "oot").Should().BeTrue();
        CatalogReviewSearch.Matches(app, "recomp").Should().BeTrue();
        CatalogReviewSearch.Matches(app, "mario").Should().BeFalse();
    }

    [Fact]
    public void Matches_game_requires_every_token()
    {
        var app = new GameInfo
        {
            Name = "Ocarina of Time",
            Tags = ["recomp", "n64"],
        };

        CatalogReviewSearch.Matches(app, "zelda recomp").Should().BeFalse();
        CatalogReviewSearch.Matches(app, "ocarina recomp").Should().BeTrue();
    }

    private static CatalogSyncRowItem CreateRow(
        string name,
        string repository = "owner/app",
        string? project = null,
        string? folderName = null,
        IEnumerable<string>? tags = null) =>
        new()
        {
            Repository = repository,
            DisplayName = AppDisplayName.Resolve(name, project, null, LibraryNameStyle.NameAndProjectInTitle),
            External = new GameInfo
            {
                Name = name,
                Project = project,
                Repository = repository,
                FolderName = folderName ?? "Folder",
                Tags = tags?.ToList() ?? [],
            },
        };
}
