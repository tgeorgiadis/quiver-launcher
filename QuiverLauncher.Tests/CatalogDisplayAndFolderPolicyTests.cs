using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogDisplayAndFolderPolicyTests
{
    [Fact]
    public void ParseAppsFromJson_reads_project_and_featured_tags()
    {
        var service = new AppCatalogService();
        var nintendo64Path = Path.Combine(TestFixtures.CommunityAppCatalogDirectory, "Nintendo-64.json");
        var json = File.ReadAllText(nintendo64Path);
        var apps = service.ParseAppsFromJson(json);

        var zelda = apps.Should().ContainSingle(a => a.Repository == "Zelda64Recomp/Zelda64Recomp").Subject;
        zelda.Name.Should().Be("Ocarina of Time & Majora's Mask");
        zelda.Project.Should().Be("Zelda64Recomp");
        zelda.FolderName.Should().Be("OcarinaOfTimeMajorasMask-Zelda64Recomp");

        var source = new AppCatalogSource();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        AppCatalogService.ApplyListMetadata(source, document.RootElement);
        source.FeaturedTags.Should().Contain(["recomp", "decomp", "recreation", "ai"]);
    }

    [Fact]
    public void GetChangedFields_includes_project_but_not_folderName()
    {
        var local = new GameInfo
        {
            Name = "Game",
            Project = "OldProject",
            FolderName = "Game-OldProject",
            Repository = "owner/game",
        };
        var external = new GameInfo
        {
            Name = "Game",
            Project = "NewProject",
            FolderName = "Game-NewProject",
            Repository = "owner/game",
        };

        var changed = CatalogCompareService.GetChangedFields(local, external);
        changed.Should().Contain("project");
        changed.Should().NotContain("folderName");
    }

    [Fact]
    public void AreCatalogFieldsEquivalent_ignores_folderName_differences()
    {
        var local = new GameInfo
        {
            Name = "Game",
            Project = "Project",
            FolderName = "Game-Local",
            Repository = "owner/game",
            Tags = ["recomp"],
        };
        var external = new GameInfo
        {
            Name = "Game",
            Project = "Project",
            FolderName = "Game-Catalog",
            Repository = "owner/game",
            Tags = ["recomp"],
        };

        AppCatalogService.AreCatalogFieldsEquivalent(local, external).Should().BeTrue();
    }

    [Fact]
    public void ReplaceFromExternal_preserves_local_folder_and_custom_display_name()
    {
        var local = new GameInfo
        {
            Name = "Old",
            Project = "OldProject",
            CustomDisplayName = "My Name",
            FolderName = "InstalledFolder",
            Repository = "owner/game",
            AutoUpdate = true,
            Tags = ["old"],
        };
        var external = new GameInfo
        {
            Name = "New",
            Project = "NewProject",
            FolderName = "CatalogFolder",
            Repository = "owner/game",
            Tags = ["new"],
        };

        var replaced = CatalogCompareService.ReplaceFromExternal(local, external);
        replaced.Name.Should().Be("New");
        replaced.Project.Should().Be("NewProject");
        replaced.FolderName.Should().Be("InstalledFolder");
        replaced.CustomDisplayName.Should().Be("My Name");
        replaced.AutoUpdate.Should().BeTrue();
        replaced.Tags.Should().Equal("new");
    }

    [Fact]
    public void ApplyUserAppDisplayNames_applies_settings_override()
    {
        var app = new GameInfo
        {
            Name = "Game",
            Project = "Project",
            Repository = "owner/game",
        };
        var settings = new AppSettings
        {
            LibraryNameStyle = LibraryNameStyle.NameOnly,
            UserAppDisplayNames = new Dictionary<string, string>
            {
                ["owner/game"] = "Custom",
            },
        };

        AppCatalogService.ApplyUserAppDisplayNames(app, settings);
        app.CustomDisplayName.Should().Be("Custom");
        app.LibraryNameStyle.Should().Be(LibraryNameStyle.NameOnly);
        app.DisplayName.Should().Be("Custom");
    }
}
