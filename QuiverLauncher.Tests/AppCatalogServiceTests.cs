using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using System.Text.Json;

namespace QuiverLauncher.Tests;

public class AppCatalogServiceTests
{
    private static GameInfo CreateApp(
        string repository,
        string name = "Test App",
        string folderName = "TestFolder",
        string? gameIconUrl = null,
        string? installPath = null)
    {
        return new GameInfo
        {
            Repository = repository,
            Name = name,
            FolderName = folderName,
            GameIconUrl = gameIconUrl,
            InstallPath = installPath,
        };
    }

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
            var resolved = AppCatalogService.ResolveLocalPath("Fixtures/index.json");
            resolved.Should().Be(Path.Combine(Path.GetFullPath(temp), "Fixtures/index.json"));
        }
        finally
        {
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
        }
    }

    [Fact]
    public void MigrateLegacyCatalogSources_defaults_to_quiver_paths_cache()
    {
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        var temp = Path.Combine(Path.GetTempPath(), "QuiverCatalogMigrate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            QuiverLauncherPaths.OverrideUserDataRoot = temp;
            var settings = new AppSettings();
            settings.EnsureInitialized();

            AppCatalogService.MigrateLegacyCatalogSources(settings);

            Directory.Exists(Path.Combine(Path.GetFullPath(temp), "Cache", "CatalogSources")).Should().BeTrue();
        }
        finally
        {
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
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
    public void GetCatalogDiff_detects_added_removed_and_changed_apps()
    {
        var accepted = new List<GameInfo>
        {
            CreateApp("owner/unchanged", "Unchanged", "UnchangedFolder"),
            CreateApp("owner/removed", "Removed", "RemovedFolder"),
            CreateApp("owner/changed", "Old Name", "OldFolder"),
        };

        var remote = new List<GameInfo>
        {
            CreateApp("owner/unchanged", "Unchanged", "UnchangedFolder"),
            CreateApp("owner/changed", "New Name", "OldFolder"),
            CreateApp("owner/added", "Added", "AddedFolder"),
        };

        var diff = AppCatalogService.GetCatalogDiff(accepted, remote);

        diff.HasChanges.Should().BeTrue();
        diff.Added.Should().ContainSingle(a => a.Repository == "owner/added");
        diff.Removed.Should().ContainSingle(a => a.Repository == "owner/removed");
        diff.Changed.Should().ContainSingle(a => a.Repository == "owner/changed" && a.Name == "New Name");
        diff.AddedCount.Should().Be(1);
        diff.RemovedCount.Should().Be(1);
        diff.ChangedCount.Should().Be(1);
    }

    [Fact]
    public void GetCatalogDiff_is_case_insensitive_for_repository_keys()
    {
        var accepted = new List<GameInfo> { CreateApp("Owner/App", "Same", "Folder") };
        var remote = new List<GameInfo> { CreateApp("owner/app", "Same", "Folder") };

        var diff = AppCatalogService.GetCatalogDiff(accepted, remote);

        diff.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void GetCatalogDiff_reports_no_changes_when_identical()
    {
        var accepted = new List<GameInfo>
        {
            CreateApp("owner/app", "Name", "Folder", installPath: "C:\\Games\\App", gameIconUrl: "https://example.com/icon.png"),
        };
        var remote = new List<GameInfo>
        {
            CreateApp("owner/app", "Name", "Folder", installPath: "C:\\Games\\App", gameIconUrl: "https://example.com/icon.png"),
        };
        remote[0].PreferredVersion = "v1.0.0";
        remote[0].SkippedUpdateVersion = "v2.0.0";
        accepted[0].PreferredVersion = "v1.0.0";
        accepted[0].SkippedUpdateVersion = "v2.0.0";

        AppCatalogService.GetCatalogDiff(accepted, remote).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void GetCatalogDiff_detects_metadata_field_changes()
    {
        var accepted = new List<GameInfo>
        {
            CreateApp("owner/app", "Name", "Folder", installPath: null, gameIconUrl: null),
        };
        accepted[0].PreferredVersion = "v1.0.0";
        accepted[0].SkippedUpdateVersion = null;

        var remote = new List<GameInfo>
        {
            CreateApp("owner/app", "Name", "Folder", installPath: "D:\\Custom", gameIconUrl: "https://example.com/icon.png"),
        };
        remote[0].PreferredVersion = "v2.0.0";

        var diff = AppCatalogService.GetCatalogDiff(accepted, remote);

        diff.HasChanges.Should().BeTrue();
        diff.Changed.Should().ContainSingle(a => a.Repository == "owner/app");
        diff.Added.Should().BeEmpty();
        diff.Removed.Should().BeEmpty();
    }

    [Fact]
    public void GetCatalogDiff_detects_tag_changes()
    {
        var accepted = new List<GameInfo> { CreateApp("owner/app", "Name", "Folder") };
        accepted[0].Tags = ["n64"];

        var remote = new List<GameInfo> { CreateApp("owner/app", "Name", "Folder") };
        remote[0].Tags = ["n64", "recomp"];

        var diff = AppCatalogService.GetCatalogDiff(accepted, remote);

        diff.HasChanges.Should().BeTrue();
        diff.Changed.Should().ContainSingle(a => a.Repository == "owner/app");
    }

    [Fact]
    public void ApplyUserAppTags_replaces_tags_when_override_exists()
    {
        var app = CreateApp("owner/app", "Name", "Folder");
        app.Tags = ["catalog"];

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
    public void ComputeCatalogContentHash_is_stable_for_same_content_regardless_of_order()
    {
        var first = new List<GameInfo>
        {
            CreateApp("b/repo", "Beta", "BetaFolder"),
            CreateApp("a/repo", "Alpha", "AlphaFolder"),
        };

        var second = new List<GameInfo>
        {
            CreateApp("a/repo", "Alpha", "AlphaFolder"),
            CreateApp("b/repo", "Beta", "BetaFolder"),
        };

        AppCatalogService.ComputeCatalogContentHash(first)
            .Should()
            .Be(AppCatalogService.ComputeCatalogContentHash(second));
    }

    [Fact]
    public void ComputeCatalogContentHash_changes_when_app_metadata_changes()
    {
        var baseline = new List<GameInfo> { CreateApp("owner/app", "Name", "Folder") };
        var changed = new List<GameInfo> { CreateApp("owner/app", "Different Name", "Folder") };

        AppCatalogService.ComputeCatalogContentHash(baseline)
            .Should()
            .NotBe(AppCatalogService.ComputeCatalogContentHash(changed));
    }

    [Fact]
    public void ParseAppsFromJson_reads_community_n64_recomp_fixture()
    {
        var service = new AppCatalogService();
        var apps = service.ParseAppsFromJson(TestFixtures.ReadN64RecompListJson());

        apps.Should().NotBeEmpty();
        apps.Should().Contain(app => app.Repository == "Zelda64Recomp/Zelda64Recomp");
        apps.Should().OnlyContain(app => !string.IsNullOrWhiteSpace(app.Repository));
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

    [Fact]
    public async Task FetchSourceAsync_applies_name_and_description_from_list_json()
    {
        var sourceId = Guid.NewGuid().ToString();
        const string listUrl = "https://example.com/n64-recomps.json";
        var listJson = await File.ReadAllTextAsync(TestFixtures.N64RecompListPath);
        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [listUrl] = listJson,
        });
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService(locationReader: reader);

        try
        {
            var source = new AppCatalogSource
            {
                Id = sourceId,
                Name = "N64 Recomps",
                Location = listUrl,
                Enabled = true,
            };

            var fetched = await service.FetchSourceAsync(new HttpClient(), source);

            fetched.Should().BeTrue();
            source.Name.Should().Be("N64 Recomps");
            source.Description.Should().Contain("N64 recompilation ports");
            source.CachedListVersion.Should().Be("1.0.5");
            source.FeaturedTags.Should().Contain("recomp");
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public void ApplyListMetadata_falls_back_to_location_when_json_has_no_name()
    {
        using var document = JsonDocument.Parse("""{"apps":[]}""");
        var source = new AppCatalogSource
        {
            Name = "",
            Location = "https://example.com/my-cool-list.json",
        };

        AppCatalogService.ApplyListMetadata(source, document.RootElement);

        source.Name.Should().Be("my cool list");
    }

    [Fact]
    public void ApplyListMetadata_uses_json_name_when_present()
    {
        using var document = JsonDocument.Parse("""{"name":"Official List","apps":[]}""");
        var source = new AppCatalogSource
        {
            Name = "",
            Location = "https://example.com/my-cool-list.json",
        };

        AppCatalogService.ApplyListMetadata(source, document.RootElement);

        source.Name.Should().Be("Official List");
    }

    [Fact]
    public void ApplyListMetadata_reads_preferred_and_hidden_tag_filters()
    {
        using var document = JsonDocument.Parse("""
            {
              "name": "N64",
              "featuredTags": ["recomp", "decomp"],
              "preferredTagFilters": ["translation", "texture-pack"],
              "hiddenTagFilters": ["n64", "nintendo"],
              "apps": []
            }
            """);
        var source = new AppCatalogSource();

        AppCatalogService.ApplyListMetadata(source, document.RootElement);

        source.FeaturedTags.Should().Equal("recomp", "decomp");
        source.PreferredTagFilters.Should().Equal("translation", "texture-pack");
        source.HiddenTagFilters.Should().Equal("n64", "nintendo");
    }

    private sealed class FakeCatalogLocationReader(Dictionary<string, string> responses) : ICatalogLocationReader
    {
        public Task<string> ReadAsync(HttpClient httpClient, string location, CancellationToken cancellationToken = default)
        {
            if (responses.TryGetValue(location, out var json))
                return Task.FromResult(json);

            throw new InvalidOperationException($"No fake response for {location}");
        }
    }
}
