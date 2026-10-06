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
    public void Adding_manual_app_never_enables_auto_update()
    {
        var added = LibraryAddService.CloneForLocal(new GameInfo { Name = "Itch App", FolderName = "ItchApp" }, autoUpdate: true);

        added.IsManuallyManaged.Should().BeTrue();
        added.AutoUpdate.Should().BeFalse();
        added.FolderName.Should().Be("ItchApp");
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
