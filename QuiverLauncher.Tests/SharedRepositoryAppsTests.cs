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
    public void PlanAdd_adds_another_filter_of_a_shared_repository_but_not_the_same_app_twice()
    {
        var exit1 = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-Custom", "EXIT1");
        var local = new List<GameInfo> { exit1 };

        var second = LibraryAddService.PlanAdd(local,
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT 2", "EXIT2-ReXGlue", "EXIT2"), autoUpdate: false);
        second.Outcome.Should().Be(LibraryAddOutcome.Added);
        second.Library.Select(a => a.FolderName).Should().Equal("EXIT-Custom", "EXIT2-ReXGlue");

        // Same repository and filter is the same app, even when the library copy lives in another folder.
        var again = LibraryAddService.PlanAdd(local,
            CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", " EXIT1 "), autoUpdate: false);
        again.Outcome.Should().Be(LibraryAddOutcome.AlreadyAdded);
        again.App.Should().BeSameAs(exit1);
        again.Library.Should().Equal(local);
    }

    [Fact]
    public void CloneForLocal_copies_release_asset_filter()
    {
        var external = CreateApp("FluffyQuack/ReXGlue-EXIT", "EXIT", "EXIT-ReXGlue", "EXIT1");
        LibraryAddService.CloneForLocal(external).ReleaseAssetFilter.Should().Be("EXIT1");
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
