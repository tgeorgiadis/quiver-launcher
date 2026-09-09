using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GameInstallLocationServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public GameInstallLocationServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "QuiverLocateInstall_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void ApplyLocatedPath_sets_installPath_without_changing_folder_identity()
    {
        var game = CreateCatalogApp();
        var originalKey = game.InstanceKey;

        GameInstallLocationService.ApplyLocatedPath(game, @"D:\Games\Zelda");

        game.InstallPath.Should().Be(@"D:\Games\Zelda");
        game.FolderName.Should().Be("OcarinaOfTimeMajorasMask-Zelda64Recomp");
        game.InstanceKey.Should().Be(originalKey);
        game.GetInstallPath(@"C:\Quiver\Apps").Should().Be(@"D:\Games\Zelda");
    }

    [Fact]
    public void PersistTo_updates_matching_saved_row_without_a_duplicate()
    {
        var saved = CreateCatalogApp();
        var live = CreateCatalogApp();
        GameInstallLocationService.ApplyLocatedPath(live, @"D:\Games\Zelda");

        var apps = new List<GameInfo> { saved };
        GameInstallLocationService.PersistTo(apps, live);

        apps.Should().ContainSingle();
        saved.InstallPath.Should().Be(@"D:\Games\Zelda");
        saved.FolderName.Should().Be("OcarinaOfTimeMajorasMask-Zelda64Recomp");
    }

    [Fact]
    public void PersistTo_upserts_when_instance_key_is_missing()
    {
        var live = CreateCatalogApp();
        GameInstallLocationService.ApplyLocatedPath(live, @"D:\Games\Zelda");
        var apps = new List<GameInfo>();

        GameInstallLocationService.PersistTo(apps, live);

        apps.Should().ContainSingle().Which.Should().BeSameAs(live);
        live.InstallPath.Should().Be(@"D:\Games\Zelda");
    }

    [Fact]
    public async Task Save_and_load_round_trip_keeps_located_installPath()
    {
        var live = CreateCatalogApp();
        GameInstallLocationService.ApplyLocatedPath(live, @"D:\Games\Zelda");
        var catalog = new AppCatalogService(dataDirectory: _tempDirectory);

        var saved = new List<GameInfo> { CreateCatalogApp() };
        GameInstallLocationService.PersistTo(saved, live);
        await catalog.SaveLocalAppsAsync(saved);

        var reloaded = await catalog.LoadLocalAppsAsync();
        var app = reloaded.Should().ContainSingle().Subject;
        app.FolderName.Should().Be("OcarinaOfTimeMajorasMask-Zelda64Recomp");
        app.InstallPath.Should().Be(@"D:\Games\Zelda");
        app.GetInstallPath(Path.Combine(_tempDirectory, "Apps")).Should().Be(@"D:\Games\Zelda");
    }

    private static GameInfo CreateCatalogApp() =>
        new()
        {
            Name = "Ocarina of Time & Majora's Mask",
            Repository = "Zelda64Recomp/Zelda64Recomp",
            FolderName = "OcarinaOfTimeMajorasMask-Zelda64Recomp",
        };
}
