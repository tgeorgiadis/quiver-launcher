using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher;

namespace QuiverLauncher.Tests;

public class SharedRepositoryAppsTests
{
    private static GameInfo CreateApp(
        string repository,
        string name,
        string folderName,
        string? releaseAssetFilter = null) =>
        new()
        {
            Repository = repository,
            Name = name,
            FolderName = folderName,
            ReleaseAssetFilter = releaseAssetFilter,
        };

    [Fact]
    public void InstanceKey_includes_folder_while_IdentityKey_stays_repo_only()
    {
        var exit1 = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue");
        var exit2 = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT 2", "EXIT2-ReXGlue");

        exit1.IdentityKey.Should().Be(exit2.IdentityKey);
        exit1.IdentityKey.Should().Be("github:FluffyQuack/ReXGlue-EXIT");
        exit1.InstanceKey.Should().Be("github:FluffyQuack/ReXGlue-EXIT:EXIT-ReXGlue");
        exit2.InstanceKey.Should().Be("github:FluffyQuack/ReXGlue-EXIT:EXIT2-ReXGlue");
        exit1.InstanceKey.Should().NotBe(exit2.InstanceKey);
    }

    [Fact]
    public async Task LoadLocalApps_keeps_same_repo_when_folders_differ()
    {
        var dir = Path.Combine(Path.GetTempPath(), "QuiverSharedRepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = new AppCatalogService(dataDirectory: dir);
            await catalog.SaveLocalAppsAsync(
            [
                CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1"),
                CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT 2", "EXIT2-ReXGlue", "EXIT2"),
            ]);

            var loaded = await catalog.LoadLocalAppsAsync();
            loaded.Should().HaveCount(2);
            loaded.Select(a => a.FolderName).Should().BeEquivalentTo("EXIT-ReXGlue", "EXIT2-ReXGlue");
            loaded.Should().ContainSingle(a => a.ReleaseAssetFilter == "EXIT1");
            loaded.Should().ContainSingle(a => a.ReleaseAssetFilter == "EXIT2");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task LoadLocalApps_dedupes_same_repo_and_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "QuiverSharedRepoDedupe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var jsonPath = Path.Combine(dir, "apps.json");
            await File.WriteAllTextAsync(jsonPath, """
            {
              "apps": [
                { "name": "First", "repository": "owner/shared", "folderName": "SharedFolder" },
                { "name": "Second", "repository": "owner/shared", "folderName": "SharedFolder" }
              ]
            }
            """);

            var catalog = new AppCatalogService(dataDirectory: dir);
            var loaded = await catalog.LoadLocalAppsAsync();
            loaded.Should().ContainSingle();
            loaded[0].Name.Should().Be("First");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void BuildCompareRows_shows_both_catalog_apps_from_the_same_repository()
    {
        var local = new List<GameInfo>
        {
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1"),
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT 2", "EXIT2-ReXGlue", "EXIT2"),
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);

        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(r =>
            r.External!.FolderName == "EXIT-ReXGlue" &&
            r.Status == CatalogSyncStatus.Unchanged);
        var addable = rows.Should().ContainSingle(r => r.External!.FolderName == "EXIT2-ReXGlue").Subject;
        addable.Status.Should().Be(CatalogSyncStatus.InExternalOnly);
        addable.CanAdd.Should().BeTrue();

        var updated = CatalogCompareService.ApplyRowAdd(local, addable);
        updated.Select(a => a.FolderName).Should().BeEquivalentTo("EXIT-ReXGlue", "EXIT2-ReXGlue");
    }

    [Fact]
    public void BuildCompareRows_matches_unique_repo_when_local_folder_was_renamed()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/game", "Game", "Game-CustomFolder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/game", "Game", "Game-CatalogFolder"),
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);

        rows.Should().ContainSingle();
        rows[0].Local!.FolderName.Should().Be("Game-CustomFolder");
        rows[0].External!.FolderName.Should().Be("Game-CatalogFolder");
        rows[0].Status.Should().Be(CatalogSyncStatus.Unchanged);
    }

    [Fact]
    public void IndexByInstanceKey_keeps_same_repo_with_different_folders()
    {
        var exit1 = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue");
        var exit2 = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT 2", "EXIT2-ReXGlue");

        var indexed = CatalogCompareService.IndexByInstanceKey([exit1, exit2]);

        indexed.Should().HaveCount(2);
        indexed[exit1.InstanceKey].Name.Should().Be("EXIT");
        indexed[exit2.InstanceKey].Name.Should().Be("EXIT 2");
    }

    [Fact]
    public void GetCatalogDiff_treats_same_repo_different_folders_as_distinct()
    {
        var baseline = new List<GameInfo>
        {
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1"),
        };
        var remote = new List<GameInfo>
        {
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1"),
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT 2", "EXIT2-ReXGlue", "EXIT2"),
        };

        var diff = AppCatalogService.GetCatalogDiff(baseline, remote);

        diff.Added.Should().ContainSingle(a => a.FolderName == "EXIT2-ReXGlue");
        diff.Removed.Should().BeEmpty();
        diff.Changed.Should().BeEmpty();
    }

    [Fact]
    public void CloneReplaceMerge_copy_release_asset_filter()
    {
        var external = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1");
        CatalogCompareService.CloneForLocal(external).ReleaseAssetFilter.Should().Be("EXIT1");

        var local = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue");
        CatalogCompareService.ReplaceFromExternal(local, external).ReleaseAssetFilter.Should().Be("EXIT1");
        CatalogCompareService.MergeExternalIntoLocal(local, external).ReleaseAssetFilter.Should().Be("EXIT1");
    }

    [Fact]
    public void GetDownloadableAssets_filters_by_release_asset_filter()
    {
        var release = new GitHubRelease
        {
            tag_name = "v0.9.1",
            assets =
            [
                new GitHubAsset { name = "EXIT1-Recomp.zip" },
                new GitHubAsset { name = "EXIT2-Recomp.zip" },
                new GitHubAsset { name = "rexglue-sdk-0.9.1-win-amd64.zip" },
            ],
        };

        GitHubReleaseService.GetDownloadableAssets(release, "EXIT1")
            .Select(a => a.name)
            .Should()
            .Equal("EXIT1-Recomp.zip");

        GitHubReleaseService.GetDownloadableAssets(release, "EXIT2")
            .Select(a => a.name)
            .Should()
            .Equal("EXIT2-Recomp.zip");

        GitHubReleaseService.GetDownloadableAssets(release)
            .Should()
            .HaveCount(3);
    }

    [Fact]
    public void TrySelectPlatformDownload_uses_release_asset_filter_before_platform_match()
    {
        var game = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1");
        var settings = new AppSettings { Platform = TargetOS.Windows };
        var release = new GitHubRelease
        {
            tag_name = "v0.9.1",
            assets =
            [
                new GitHubAsset { name = "EXIT2-Recomp.zip" },
                new GitHubAsset { name = "EXIT1-Recomp.zip" },
                new GitHubAsset { name = "rexglue-sdk-0.9.1-win-amd64.zip" },
            ],
        };

        GameDownloadService.TrySelectPlatformDownload(game, release, settings).Should().BeTrue();
        game.SelectedDownload!.name.Should().Be("EXIT1-Recomp.zip");
    }
}
