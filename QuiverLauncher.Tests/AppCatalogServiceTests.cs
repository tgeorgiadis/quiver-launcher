using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AppCatalogServiceTests
{
    [Theory]
    [InlineData("https://example.com/apps.json", true)]
    [InlineData("http://example.com/apps.json", true)]
    [InlineData("HTTPS://EXAMPLE.COM/apps.json", true)]
    [InlineData("community-app-catalog/N64-Recomps.json", false)]
    [InlineData(@"C:\Catalogs\apps.json", false)]
    public void IsRemoteLocation_classifies_locations(string location, bool expectedRemote)
    {
        AppCatalogService.IsRemoteLocation(location).Should().Be(expectedRemote);
    }

    [Fact]
    public void ResolveLocalPath_combines_relative_paths_with_user_data_root()
    {
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        var temp = Path.Combine(Path.GetTempPath(), "QuiverCatalogPaths_" + Guid.NewGuid().ToString("N"));
        try
        {
            QuiverLauncherPaths.OverrideUserDataRoot = temp;
            var resolved = AppCatalogService.ResolveLocalPath("Lists/apps.json");
            resolved.Should().Be(Path.Combine(Path.GetFullPath(temp), "Lists/apps.json"));
        }
        finally
        {
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
        }
    }

    [Fact]
    public void ResolveLocalPath_preserves_rooted_and_remote_paths()
    {
        const string remote = "https://example.com/index.json";
        AppCatalogService.ResolveLocalPath(remote).Should().Be(remote);

        var rooted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "apps.json"));
        AppCatalogService.ResolveLocalPath(rooted).Should().Be(rooted);
    }

    [Fact]
    public void ApplyUserAppTags_replaces_tags_when_override_exists()
    {
        var app = new GameInfo
        {
            Repository = "owner/app",
            Name = "Name",
            FolderName = "Folder",
            Tags = ["catalog"],
        };

        var settings = new AppSettings
        {
            UserAppTags = new Dictionary<string, List<string>>
            {
                ["owner/app"] = ["custom", "n64"],
            },
        };

        AppCatalogService.ApplyUserAppTags(app, settings);

        app.Tags.Should().BeEquivalentTo(["custom", "n64"]);
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

    [Fact]
    public void ParseAppsFromJson_reads_app_list_file()
    {
        var service = new AppCatalogService();
        var apps = service.ParseAppsFromJson(TestFixtures.ReadN64RecompListJson());

        apps.Should().OnlyContain(app => !string.IsNullOrWhiteSpace(app.Repository));
        var zelda = apps.Should().ContainSingle(a => a.Repository == "Zelda64Recomp/Zelda64Recomp").Subject;
        zelda.Name.Should().Be("Ocarina of Time & Majora's Mask");
        zelda.Project.Should().Be("Zelda64Recomp");
        zelda.FolderName.Should().Be("OcarinaOfTimeMajorasMask-Zelda64Recomp");
    }

    [Fact]
    public void SerializeApp_round_trips_autoUpdate()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "quiver-auto-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var (service, _) = TestFixtures.CreateIsolatedCatalogService(dataDirectory: tempDir);
            var apps = new List<GameInfo>
            {
                new()
                {
                    Name = "Auto App",
                    Repository = "owner/auto",
                    FolderName = "Auto",
                    AutoUpdate = true,
                },
                new()
                {
                    Name = "Manual App",
                    Repository = "owner/manual",
                    FolderName = "Manual",
                    AutoUpdate = false,
                },
            };

            service.SaveLocalApps(apps);
            var json = File.ReadAllText(Path.Combine(tempDir, "apps.json"));
            json.Should().Contain("\"autoUpdate\": true");
            json.Should().Contain("owner/manual");
            // false autoUpdate is omitted from JSON and defaults to false on load
            json.Should().NotContain("\"autoUpdate\": false");

            var loaded = service.ParseAppsFromJson(json);
            loaded.Should().ContainSingle(a => a.Repository == "owner/auto" && a.AutoUpdate);
            loaded.Should().ContainSingle(a => a.Repository == "owner/manual" && !a.AutoUpdate);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
