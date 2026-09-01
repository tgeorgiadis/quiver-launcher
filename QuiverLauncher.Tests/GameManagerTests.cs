using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GameManagerTests
{
    [Fact]
    public void HideGame_adds_hidden_key_and_filters_collection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var store = new FileSettingsStore(Path.Combine(tempDir, "settings.json"));
        var manager = new GameManager(store);
        var game = new GameInfo
        {
            Name = "Hidden Game",
            FolderName = "HiddenFolder",
            Repository = "owner/hidden",
        };

        manager.Games.Add(game);
        manager.HideGame(game);

        store.Current.ManuallyHiddenApps.Should().Contain("folder:HiddenFolder");
        manager.Games.Should().BeEmpty();

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void IsManuallyHidden_reflects_manual_hide_list()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var store = new FileSettingsStore(Path.Combine(tempDir, "settings.json"));
        var manager = new GameManager(store);
        var game = new GameInfo { Name = "Manual", FolderName = "ManualFolder" };

        manager.ToggleUserHide(game);
        manager.IsManuallyHidden(game).Should().BeTrue();

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task ReloadLibraryFromDiskAsync_does_not_fetch_catalog_sources()
    {
        var previousInvoker = GameManager.UiThreadInvoker;
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        var tempDir = Path.Combine(Path.GetTempPath(), "QuiverLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        GameManager.UiThreadInvoker = null;
        QuiverLauncherPaths.OverrideUserDataRoot = tempDir;

        try
        {
            var reader = new ThrowingCatalogLocationReader();
            var catalog = new AppCatalogService(null, reader, tempDir);
            var store = new FileSettingsStore(Path.Combine(tempDir, "settings.json"));
            store.Current.AppsPath = Path.Combine(tempDir, "Apps");
            store.Current.AppCatalogSources =
            [
                new AppCatalogSource
                {
                    Id = "nintendo",
                    Name = "Nintendo",
                    Location = "https://example.test/nintendo.json",
                    Enabled = true,
                },
            ];
            store.Save(store.Current);

            var added = new GameInfo { Name = "Added", Repository = "owner/added", FolderName = "Added" };
            await catalog.SaveLocalAppsAsync([
                new GameInfo { Name = "Existing", Repository = "owner/existing", FolderName = "Existing" },
                added,
            ]);

            using var manager = new GameManager(
                store,
                httpClient: new HttpClient { Timeout = TimeSpan.FromSeconds(2) },
                catalogService: catalog);
            await manager.ReloadLibraryFromDiskAsync([added.InstanceKey])
                .WaitAsync(TimeSpan.FromSeconds(15));

            reader.FetchCount.Should().Be(0);
            manager.Games.Select(g => g.Repository).Should().BeEquivalentTo(["owner/existing", "owner/added"]);
        }
        finally
        {
            GameManager.UiThreadInvoker = previousInvoker;
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private sealed class ThrowingCatalogLocationReader : ICatalogLocationReader
    {
        public int FetchCount { get; private set; }

        public Task<string> ReadAsync(HttpClient httpClient, string location, CancellationToken cancellationToken = default)
        {
            FetchCount++;
            throw new InvalidOperationException($"Unexpected catalog fetch: {location}");
        }
    }
}
