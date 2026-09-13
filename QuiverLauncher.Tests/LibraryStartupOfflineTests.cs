using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LibraryStartupOfflineTests
{
    private sealed class BlockedNetwork : HttpMessageHandler
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Offline fixture") };
        }
    }

    [AvaloniaFact]
    public async Task Startup_displays_saved_library_and_source_counts_before_network_finishes()
    {
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "quiver-startup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var handler = new BlockedNetwork();
        MainView? view = null;
        Window? window = null;
        Task? startup = null;
        try
        {
            var store = new FileSettingsStore();
            store.Current.FirstStartup = false;
            store.Current.PromptCatalogUpdates = false;
            store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Current.AppCatalogSources = [new() { Id = "cached", Name = "Cached list",
                Location = "https://fixture.invalid/catalog.json", Enabled = true, CachedListVersion = "1" }];
            store.Save(store.Current);
            using var http = new HttpClient(handler);
            using var manager = new GameManager(store, http);
            var apps = Enumerable.Range(0, 125).Select(i => new GameInfo
                { Name = $"App {i}", FolderName = $"App{i}", Repository = $"startup-fixture/app{i}" }).ToList();
            await manager.CatalogService.SaveLocalAppsAsync(apps);
            await manager.CatalogService.ExportLocalAppsToFileAsync(
                Path.Combine(manager.CatalogService.CatalogSourcesCacheFolder, "cached.json"), apps);
            view = new MainView(new() { SettingsStore = store, GameManager = manager,
                InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
            window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show();
            startup = view.InitializeGamesAsync();
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            startup.IsCompleted.Should().BeFalse("the network is still blocked");
            manager.Games.Should().HaveCount(125);
            var originals = manager.Games.ToArray();
            store.Current.AppCatalogSources.Single(s => s.Id == "cached").LibraryAppCount.Should().Be(125);
            store.Current.AppCatalogSources.Single(s => s.Id == "cached").ListAppCount.Should().Be(125);
            handler.Release.TrySetResult();
            await startup.WaitAsync(TimeSpan.FromSeconds(15));
            manager.Games.Should().Equal(originals, "online failure must preserve loaded app instances");
            (await manager.CatalogService.LoadLocalAppsAsync()).Should().HaveCount(125);
            await view.ShutdownAsync();
        }
        finally
        {
            handler.Release.TrySetResult();
            window?.Close();
            if (view != null) await view.ShutdownAsync();
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
            Directory.Delete(root, true);
        }
    }
}
