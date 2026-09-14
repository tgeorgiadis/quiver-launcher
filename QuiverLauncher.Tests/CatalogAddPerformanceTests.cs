using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogAddPerformanceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Add_persists_and_updates_library_without_waiting_for_releases_or_icons(int count)
    {
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "quiver-add-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        try
        {
            using var icons = new TcpListener(IPAddress.Loopback, 0);
            icons.Start();
            var iconPort = ((IPEndPoint)icons.LocalEndpoint).Port;
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Current.AppsPath = Path.Combine(root, "Apps");
            var source = new AppCatalogSource { Id = "test", Enabled = true, Location = Path.Combine(root, "source.json") };
            store.Current.AppCatalogSources = [source];
            store.Save(store.Current);
            var handler = new RejectNetwork();
            using var http = new HttpClient(handler);
            using var manager = new GameManager(store, http);
            await manager.CatalogService.SaveLocalAppsAsync([]);
            var apps = Enumerable.Range(0, count).Select(i => new GameInfo
            {
                Name = "App " + i, Repository = $"test-{Guid.NewGuid():N}/app", FolderName = "App" + i,
                GameIconUrl = i == 0 ? $"http://127.0.0.1:{iconPort}/icon.png" : null,
            }).ToList();
            foreach (var app in apps) QuiverLauncher.Core.Services.CatalogPlatformIndex.Set("github", app.Repository, null, null,
                new() { tag_name = "v1", assets = [new() { name = "app-Windows.zip" }] });
            await manager.CatalogService.ExportLocalAppsToFileAsync(source.Location, apps);
            // An existing installation must still be recognized by the local-only path.
            var installed = Path.Combine(manager.GamesFolder, "App0");
            Directory.CreateDirectory(installed);
            await File.WriteAllTextAsync(Path.Combine(installed, "version.txt"), "1.2.3");
            await File.WriteAllTextAsync(Path.Combine(installed, OperatingSystem.IsMacOS() ? "app" : "app.exe"),
                "#!/bin/sh\nexit 0\n", TestContext.Current.CancellationToken);
            var model = new CatalogSyncViewModel();
            var settings = new SettingsViewModel(store);
            var refreshes = 0;
            using var workspace = new CatalogReviewWorkspace(model, settings,
                new CatalogReviewService(manager, settings, () => { }),
                (message, _, _) => throw new InvalidOperationException(message), async () =>
                {
                    refreshes++;
                    await manager.CatalogService.RefreshAllSourcesUsageStatsAsync(store.Current);
                    await manager.CatalogService.ApplyPendingCatalogChangeFlagsAsync(manager.Games, store.Current);
                });
            await workspace.OpenAsync(source, CatalogReviewFilter.All, CancellationToken.None);
            workspace.Error.Should().BeNull();
            await workspace.ExecuteAsync(count == 1 ? CatalogReviewAction.Add : CatalogReviewAction.AddAll,
                count == 1 ? model.AllRows[0].IdentityKey : null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            handler.Requests.Should().Be(0);
            icons.Pending().Should().BeFalse("uncached icons must not download during Add");
            refreshes.Should().Be(1);
            workspace.IsBusy.Should().BeFalse();
            manager.Games.Should().HaveCount(count);
            var game = manager.Games.Single(g => g.FolderName == "App0");
            game.Status.Should().Be(GameStatus.Installed);
            game.InstalledVersion.Should().Be("1.2.3");
            if (count == 1) game.IconUrl.Should().NotStartWith("http", "presenting the new card must not trigger the image loader's own HTTP client");
            (await manager.CatalogService.LoadLocalAppsAsync()).Should().HaveCount(count);
            model.GetFilteredBulkAddRows().Should().BeEmpty();
        }
        finally
        {
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            Directory.Delete(root, true);
        }
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            throw new InvalidOperationException("Adding a catalog entry must not request remote metadata.");
        }
    }
}
