using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using System.Text.Json;

namespace QuiverLauncher.Tests;

public class ManualManagedAppsTests
{
    [Fact]
    public void IdentityKey_uses_manual_folder_and_does_not_collapse()
    {
        var first = new GameInfo { Name = "One", FolderName = "AppOne" };
        var second = new GameInfo { Name = "Two", FolderName = "AppTwo" };

        first.IsManuallyManaged.Should().BeTrue();
        first.IdentityKey.Should().Be("manual:AppOne");
        second.IdentityKey.Should().Be("manual:AppTwo");
        first.IdentityKey.Should().NotBe(second.IdentityKey);
    }

    [Fact]
    public async Task Serialize_omits_repository_for_manual_apps_and_dedupes_separately()
    {
        var dir = Path.Combine(Path.GetTempPath(), "QuiverManual_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = new AppCatalogService(dataDirectory: dir);
            var apps = new List<GameInfo>
            {
                new() { Name = "Manual A", FolderName = "ManualA", Tags = ["offline"] },
                new() { Name = "Manual B", FolderName = "ManualB" },
                new() { Name = "GitHub App", Repository = "owner/app", FolderName = "GitHubApp" },
            };

            await catalog.SaveLocalAppsAsync(apps);
            var json = await File.ReadAllTextAsync(Path.Combine(dir, "apps.json"));
            using var document = JsonDocument.Parse(json);
            var array = document.RootElement.GetProperty("apps");

            array[0].TryGetProperty("repository", out _).Should().BeFalse();
            array[0].TryGetProperty("autoUpdate", out _).Should().BeFalse();
            array[2].GetProperty("repository").GetString().Should().Be("owner/app");

            var loaded = await catalog.LoadLocalAppsAsync();
            loaded.Should().HaveCount(3);
            loaded.Should().ContainSingle(a => a.FolderName == "ManualA" && a.IsManuallyManaged);
            loaded.Should().ContainSingle(a => a.FolderName == "ManualB" && a.IsManuallyManaged);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task SaveLocalAppsAsync_can_drop_manual_app_by_identity()
    {
        var dir = Path.Combine(Path.GetTempPath(), "QuiverManualRemove_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = new AppCatalogService(dataDirectory: dir);
            var manual = new GameInfo { Name = "Dummy", FolderName = "DummyFolder" };
            var hosted = new GameInfo { Name = "GitHub App", Repository = "owner/app", FolderName = "GitHubApp" };
            await catalog.SaveLocalAppsAsync([manual, hosted]);

            var loaded = await catalog.LoadLocalAppsAsync();
            var toRemove = loaded.FirstOrDefault(a =>
                string.Equals(a.IdentityKey, manual.IdentityKey, StringComparison.OrdinalIgnoreCase));
            toRemove.Should().NotBeNull();
            loaded.Remove(toRemove!);
            await catalog.SaveLocalAppsAsync(loaded);

            var remaining = await catalog.LoadLocalAppsAsync();
            remaining.Should().ContainSingle();
            remaining[0].Repository.Should().Be("owner/app");
            remaining.Should().NotContain(a => a.FolderName == "DummyFolder");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Catalog_compare_includes_manual_rows_and_promotes_by_folder()
    {
        var local = new List<GameInfo>
        {
            new() { Name = "Offline Port", FolderName = "OfflinePort", Tags = ["n64"] },
        };
        var external = new List<GameInfo>
        {
            new()
            {
                Name = "Offline Port",
                FolderName = "OfflinePort",
                Repository = "owner/offline-port",
                Tags = ["n64"],
            },
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);
        rows.Should().ContainSingle();
        var row = rows[0];
        row.Status.Should().Be(CatalogSyncStatus.Changed);
        row.StatusShortLabel.Should().Be("Promote");
        row.IdentityChangeKind.Should().Be(CatalogIdentityChangeKind.Promote);
        row.ChangedFields.Should().Contain("repository");
        row.Local!.IsManuallyManaged.Should().BeTrue();
        row.External!.IsManuallyManaged.Should().BeFalse();

        var replaced = CatalogCompareService.ApplyRowReplace(local, row);
        replaced.Should().ContainSingle();
        var promoted = replaced[0];
        promoted.Repository.Should().Be("owner/offline-port");
        promoted.AutoUpdate.Should().BeFalse();
        promoted.DeferUpdateTracking.Should().BeTrue();
        promoted.FolderName.Should().Be("OfflinePort");
    }

    [Fact]
    public void Catalog_compare_demotes_repository_apps_to_manual_by_folder()
    {
        var local = new List<GameInfo>
        {
            new()
            {
                Name = "Donkey Kong Land [Remake]",
                Project = "Donkey Kong Land Remake",
                FolderName = "DonkeyKongLand-DonkeyKongLandRemake",
                Repository = "owner/donkey-kong-land-remake",
                AutoUpdate = true,
                Tags = ["gb"],
            },
        };
        var external = new List<GameInfo>
        {
            new()
            {
                Name = "Donkey Kong Land [Remake]",
                Project = "Donkey Kong Land Remake",
                FolderName = "DonkeyKongLand-DonkeyKongLandRemake",
                Tags = ["recreation", "gb", "donkey kong"],
            },
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);
        rows.Should().ContainSingle();
        var row = rows[0];
        row.Status.Should().Be(CatalogSyncStatus.Changed);
        row.StatusShortLabel.Should().Be("Demote");
        row.IdentityChangeKind.Should().Be(CatalogIdentityChangeKind.Demote);
        row.ChangedFields.Should().Contain("repository");
        row.CanAdd.Should().BeFalse();
        row.CanReplace.Should().BeTrue();
        row.CanMerge.Should().BeTrue();
        row.Local!.IsManuallyManaged.Should().BeFalse();
        row.External!.IsManuallyManaged.Should().BeTrue();

        var replaced = CatalogCompareService.ApplyRowReplace(local, row);
        replaced.Should().ContainSingle();
        var demoted = replaced[0];
        demoted.IsManuallyManaged.Should().BeTrue();
        demoted.Repository.Should().BeEmpty();
        demoted.AutoUpdate.Should().BeFalse();
        demoted.FolderName.Should().Be("DonkeyKongLand-DonkeyKongLandRemake");

        var merged = CatalogCompareService.ApplyRowMerge(local, row);
        merged.Should().ContainSingle();
        merged[0].IsManuallyManaged.Should().BeTrue();
        merged[0].Repository.Should().BeEmpty();
        merged[0].AutoUpdate.Should().BeFalse();
        merged[0].FolderName.Should().Be("DonkeyKongLand-DonkeyKongLandRemake");
        TagHelper.NormalizeTags(merged[0].Tags).Should().Contain("gb");
        TagHelper.NormalizeTags(merged[0].Tags).Should().Contain("recreation");
    }

    [Fact]
    public void Catalog_compare_retargets_repository_by_folder()
    {
        var local = new List<GameInfo>
        {
            new()
            {
                Name = "Donkey Kong Land [Remake]",
                Project = "Donkey Kong Land Remake",
                FolderName = "DonkeyKongLand-DonkeyKongLandRemake",
                Repository = "mgba-emu/mgba",
                AutoUpdate = true,
                PreferredVersion = "old",
                SkippedUpdateVersion = "skip",
                Tags = ["gb"],
            },
        };
        var external = new List<GameInfo>
        {
            new()
            {
                Name = "Donkey Kong Land [Remake]",
                Project = "Donkey Kong Land Remake",
                FolderName = "DonkeyKongLand-DonkeyKongLandRemake",
                Repository = "stenzek/duckstation",
                Tags = ["recreation", "gb", "donkey kong"],
            },
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);
        rows.Should().ContainSingle();
        var row = rows[0];
        row.Status.Should().Be(CatalogSyncStatus.Changed);
        row.StatusShortLabel.Should().Be("Retarget");
        row.IdentityChangeKind.Should().Be(CatalogIdentityChangeKind.Retarget);
        row.ChangedFields.Should().Contain("repository");
        row.CanAdd.Should().BeFalse();
        row.CanReplace.Should().BeTrue();
        row.CanMerge.Should().BeTrue();
        row.Local!.Repository.Should().Be("mgba-emu/mgba");
        row.External!.Repository.Should().Be("stenzek/duckstation");

        var replaced = CatalogCompareService.ApplyRowReplace(local, row);
        replaced.Should().ContainSingle();
        var retargeted = replaced[0];
        retargeted.Repository.Should().Be("stenzek/duckstation");
        retargeted.FolderName.Should().Be("DonkeyKongLand-DonkeyKongLandRemake");
        retargeted.AutoUpdate.Should().BeFalse();
        retargeted.DeferUpdateTracking.Should().BeTrue();
        retargeted.PreferredVersion.Should().BeNull();
        retargeted.SkippedUpdateVersion.Should().BeNull();

        var merged = CatalogCompareService.ApplyRowMerge(local, row);
        merged.Should().ContainSingle();
        merged[0].Repository.Should().Be("stenzek/duckstation");
        merged[0].FolderName.Should().Be("DonkeyKongLand-DonkeyKongLandRemake");
        merged[0].AutoUpdate.Should().BeFalse();
        merged[0].DeferUpdateTracking.Should().BeTrue();
        merged[0].PreferredVersion.Should().BeNull();
        merged[0].SkippedUpdateVersion.Should().BeNull();
        TagHelper.NormalizeTags(merged[0].Tags).Should().Contain("gb");
        TagHelper.NormalizeTags(merged[0].Tags).Should().Contain("recreation");
    }

    [Fact]
    public void Catalog_compare_shows_new_manual_catalog_apps()
    {
        var local = new List<GameInfo>();
        var external = new List<GameInfo>
        {
            new()
            {
                Name = "Itch App",
                FolderName = "ItchApp",
                Tags = ["itch"],
                GameIconUrl = "https://example.com/icon.png",
            },
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);
        rows.Should().ContainSingle(r =>
            r.Status == CatalogSyncStatus.InExternalOnly &&
            r.External!.IsManuallyManaged &&
            r.Subtitle == "Manually managed");

        var added = CatalogCompareService.ApplyRowAdd(local, rows[0], autoUpdateNewlyAdded: true);
        added.Should().ContainSingle();
        added[0].IsManuallyManaged.Should().BeTrue();
        added[0].AutoUpdate.Should().BeFalse();
        added[0].FolderName.Should().Be("ItchApp");
    }

    [Fact]
    public async Task Status_waits_for_executable_and_skips_version_file()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(tempRoot, "ManualApp");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ManualAppFolderService.InstructionFileName), "placeholder");

        try
        {
            var game = new GameInfo
            {
                Name = "Manual",
                FolderName = "ManualApp",
            };

            using var httpClient = new HttpClient();
            await GameStatusService.CheckStatusAsync(game, httpClient, tempRoot);

            game.Status.Should().Be(GameStatus.NotInstalled);
            game.CanDownload.Should().BeFalse();
            game.CanOpenFolder.Should().BeTrue();
            game.CanToggleAutoUpdate.Should().BeFalse();
            game.CanVersionOptions.Should().BeFalse();
            game.ButtonText.Should().Be("Open Folder");
            game.StatusText.Should().Be("Waiting for files");
            File.Exists(Path.Combine(folder, "version.txt")).Should().BeFalse();

            CreateDummyLaunchable(folder);
            await GameStatusService.CheckStatusAsync(game, httpClient, tempRoot);

            game.Status.Should().Be(GameStatus.Installed);
            game.CanLaunch.Should().BeTrue();
            game.CanUpdate.Should().BeFalse();
            game.StatusText.Should().Be("Manually managed");
            File.Exists(Path.Combine(folder, "version.txt")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Replace_from_catalog_does_not_enable_auto_update_on_promotion()
    {
        var local = new GameInfo
        {
            Name = "Port",
            FolderName = "PortFolder",
            AutoUpdate = true,
        };
        var external = new GameInfo
        {
            Name = "Port",
            FolderName = "PortFolder",
            Repository = "owner/port",
            AutoUpdate = true,
        };

        var replaced = CatalogCompareService.ReplaceFromExternal(local, external);
        replaced.Repository.Should().Be("owner/port");
        replaced.AutoUpdate.Should().BeFalse();
        replaced.DeferUpdateTracking.Should().BeTrue();
        replaced.FolderName.Should().Be("PortFolder");
    }

    [Fact]
    public void Deferred_promotion_with_sentinel_0_0_0_shows_Update()
    {
        var game = new GameInfo
        {
            Name = "Donkey Kong Land Remake",
            FolderName = "DonkeyKongLandRemake",
            Repository = "owner/dkl-remake",
            DeferUpdateTracking = true,
            AutoUpdate = false,
            InstalledVersion = "0.0.0",
            LatestVersion = "0.10.5",
            Status = GameStatus.Installed,
        };

        game.RefreshInstalledStatus();

        game.CanUpdate.Should().BeTrue();
        game.Status.Should().Be(GameStatus.UpdateAvailable);
        game.ButtonText.Should().Be("Update");
        game.DeferUpdateTracking.Should().BeTrue();
        game.AutoUpdate.Should().BeFalse();
    }

    [Fact]
    public void Deferred_promotion_with_sentinel_0_0_0_honors_skipped_latest()
    {
        var game = new GameInfo
        {
            Name = "Port",
            FolderName = "PortFolder",
            Repository = "owner/port",
            DeferUpdateTracking = true,
            AutoUpdate = false,
            InstalledVersion = "0.0.0",
            LatestVersion = "0.10.5",
            SkippedUpdateVersion = "0.10.5",
            Status = GameStatus.Installed,
        };

        game.RefreshInstalledStatus();

        game.CanUpdate.Should().BeFalse();
        game.Status.Should().Be(GameStatus.Installed);
        game.DeferUpdateTracking.Should().BeTrue();
    }

    [Fact]
    public void Deferred_promotion_with_real_installed_version_still_suppresses_Update()
    {
        var game = new GameInfo
        {
            Name = "Port",
            FolderName = "PortFolder",
            Repository = "owner/port",
            DeferUpdateTracking = true,
            AutoUpdate = false,
            InstalledVersion = "0.9.0",
            LatestVersion = "0.10.5",
            Status = GameStatus.Installed,
        };

        game.RefreshInstalledStatus();

        game.CanUpdate.Should().BeFalse();
        game.Status.Should().Be(GameStatus.Installed);
        game.DeferUpdateTracking.Should().BeTrue();
    }

    [Fact]
    public void Newly_added_app_with_latest_stays_not_installed()
    {
        var game = new GameInfo
        {
            Name = "Silent Hill: Downpour",
            FolderName = "SilentHillDownpour-DownpourRecomp",
            Repository = "owner/downpour-recomp",
            InstalledVersion = "",
            LatestVersion = "v1.1.6",
            Status = GameStatus.NotInstalled,
        };

        game.RefreshInstalledStatus();

        game.Status.Should().Be(GameStatus.NotInstalled);
        game.CanDownload.Should().BeTrue();
        game.CanUpdate.Should().BeFalse();
        game.ButtonText.Should().Be("Download");
        game.StatusText.Should().Be("Not installed");
    }

    [Fact]
    public void Installed_older_version_still_shows_Update()
    {
        var game = new GameInfo
        {
            Name = "Port",
            FolderName = "PortFolder",
            Repository = "owner/port",
            InstalledVersion = "1.0.0",
            LatestVersion = "v1.1.6",
            Status = GameStatus.Installed,
        };

        game.RefreshInstalledStatus();

        game.CanUpdate.Should().BeTrue();
        game.Status.Should().Be(GameStatus.UpdateAvailable);
        game.ButtonText.Should().Be("Update");
    }

    [Fact]
    public void Downloading_status_is_not_promoted_to_update_available()
    {
        var game = new GameInfo
        {
            Name = "Port",
            FolderName = "PortFolder",
            Repository = "owner/port",
            InstalledVersion = "",
            LatestVersion = "v1.1.6",
            Status = GameStatus.Downloading,
        };

        game.RefreshInstalledStatus();

        game.Status.Should().Be(GameStatus.Downloading);
        game.CanUpdate.Should().BeFalse();
    }

    private static void CreateDummyLaunchable(string folder)
    {
        if (OperatingSystem.IsWindows())
            File.WriteAllText(Path.Combine(folder, "game.exe"), "fake");
        else if (OperatingSystem.IsMacOS())
            Directory.CreateDirectory(Path.Combine(folder, "game.app"));
        else
            File.WriteAllText(Path.Combine(folder, "game.x86_64"), "fake");
    }
}
