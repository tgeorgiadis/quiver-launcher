using FluentAssertions;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using System.Text.Json;

namespace QuiverLauncher.Tests;

public class AppIdentityMigrationTests
{
    [Fact]
    public void HasIdentityChanged_detects_repo_or_source_change()
    {
        AppIdentityMigration.HasIdentityChanged("github", "owner/a", "github", "owner/a")
            .Should().BeFalse();
        AppIdentityMigration.HasIdentityChanged(null, "owner/a", "github", "owner/a")
            .Should().BeFalse();
        AppIdentityMigration.HasIdentityChanged(null, "owner/a", "gitlab", "owner/a")
            .Should().BeTrue();
        AppIdentityMigration.HasIdentityChanged(null, "owner/old", "gitlab", "bighead.0/ladxhd_updated")
            .Should().BeTrue();
    }

    [Fact]
    public void MigrateUserAppTags_moves_tags_to_new_repository()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.UserAppTags["owner/old"] = ["n64", "recomp"];

        AppIdentityMigration.MigrateUserAppTags(settings, "owner/old", "bighead.0/ladxhd_updated")
            .Should().BeTrue();

        settings.UserAppTags.Should().NotContainKey("owner/old");
        settings.UserAppTags["bighead.0/ladxhd_updated"].Should().BeEquivalentTo("n64", "recomp");
    }

    [Fact]
    public void MigrateUserAppTags_does_not_overwrite_existing_destination()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.UserAppTags["owner/old"] = ["old-tag"];
        settings.UserAppTags["owner/new"] = ["keep-me"];

        AppIdentityMigration.MigrateUserAppTags(settings, "owner/old", "owner/new")
            .Should().BeTrue();

        settings.UserAppTags.Should().NotContainKey("owner/old");
        settings.UserAppTags["owner/new"].Should().BeEquivalentTo("keep-me");
    }

    [Fact]
    public void MigrateCatalogSourceMaps_remaps_ignore_and_hide()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        var source = new AppCatalogSource
        {
            Name = "Test",
            Location = "https://example.com/list.json",
            IgnoredChangesAtVersion = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["owner/old"] = "v2",
            },
            HiddenFromReviewRepositories = ["owner/old", "other/app"],
        };
        settings.AppCatalogSources.Add(source);

        AppIdentityMigration.MigrateCatalogSourceMaps(settings, "owner/old", "owner/new")
            .Should().BeTrue();

        source.IgnoredChangesAtVersion.Should().NotContainKey("owner/old");
        source.IgnoredChangesAtVersion["owner/new"].Should().Be("v2");
        source.HiddenFromReviewRepositories.Should().BeEquivalentTo("owner/new", "other/app");
    }

    [Fact]
    public void MigrateIdentity_source_only_change_clears_old_cache_without_settings_change()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "QuiverIdMig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        try
        {
            GitHubApiCache.Initialize(cacheDir);
            GitHubApiCache.SetCache(null, "owner/app", "v1.0.0", "etag");

            var settings = new AppSettings();
            settings.EnsureInitialized();

            var changed = AppIdentityMigration.MigrateIdentity(
                settings,
                oldRepositorySource: null,
                oldRepository: "owner/app",
                newRepositorySource: "gitlab",
                newRepository: "owner/app");

            changed.Should().BeFalse();
            GitHubApiCache.TryGetCachedVersion(null, "owner/app", out _).Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(cacheDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Edit_persist_writes_new_repository_and_source()
    {
        var dir = Path.Combine(Path.GetTempPath(), "QuiverEditRepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = new AppCatalogService(dataDirectory: dir);
            await catalog.SaveLocalAppsAsync(
            [
                new GameInfo
                {
                    Name = "LA DX HD",
                    Repository = "OldOwner/LADXHD",
                    FolderName = "LADXHD",
                },
            ]);

            var apps = await catalog.LoadLocalAppsAsync();
            var app = apps.Should().ContainSingle().Subject;
            var oldIdentity = app.IdentityKey;

            app.Repository = "bighead.0/ladxhd_updated";
            app.RepositorySource = "gitlab";

            oldIdentity.Should().NotBe(app.IdentityKey);

            var settings = new AppSettings();
            settings.EnsureInitialized();
            settings.UserAppTags["OldOwner/LADXHD"] = ["zelda"];

            AppIdentityMigration.MigrateIdentity(
                settings,
                null,
                "OldOwner/LADXHD",
                app.RepositorySource,
                app.Repository).Should().BeTrue();

            await catalog.SaveLocalAppsAsync(apps);
            var json = await File.ReadAllTextAsync(Path.Combine(dir, "apps.json"));
            using var document = JsonDocument.Parse(json);
            var entry = document.RootElement.GetProperty("apps")[0];
            entry.GetProperty("repository").GetString().Should().Be("bighead.0/ladxhd_updated");
            entry.GetProperty("repositorySource").GetString().Should().Be("gitlab");

            settings.UserAppTags.Should().ContainKey("bighead.0/ladxhd_updated");
            settings.UserAppTags.Should().NotContainKey("OldOwner/LADXHD");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Duplicate_identity_on_edit_is_detectable()
    {
        var existing = new GameInfo
        {
            Name = "Existing",
            Repository = "bighead.0/ladxhd_updated",
            RepositorySource = "gitlab",
            FolderName = "Other",
        };
        var editing = new GameInfo
        {
            Name = "LA DX HD",
            Repository = "OldOwner/LADXHD",
            FolderName = "LADXHD",
        };

        var targetKey = RepositorySourceHelper.GetIdentityKey("gitlab", "bighead.0/ladxhd_updated");
        var games = new List<GameInfo> { existing, editing };
        var duplicate = games.Any(g =>
            !ReferenceEquals(g, editing) &&
            string.Equals(g.IdentityKey, targetKey, StringComparison.OrdinalIgnoreCase));

        duplicate.Should().BeTrue();
    }
}
