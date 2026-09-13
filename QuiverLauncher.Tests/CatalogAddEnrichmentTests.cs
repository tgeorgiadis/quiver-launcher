using System.Net;
using System.Text;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogAddEnrichmentTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    private sealed class Profile : IDisposable
    {
        private readonly string? _previous = QuiverLauncherPaths.OverrideUserDataRoot;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "quiver-enrichment", Guid.NewGuid().ToString("N"));
        public FileSettingsStore Store { get; }
        public Profile()
        {
            Directory.CreateDirectory(Root);
            QuiverLauncherPaths.OverrideUserDataRoot = Root;
            Store = new(Path.Combine(Root, "settings.json"));
            Store.Current.AppsPath = Path.Combine(Root, "Apps");
            Store.Save(Store.Current);
        }
        public void Dispose() { QuiverLauncherPaths.OverrideUserDataRoot = _previous; Directory.Delete(Root, true); }
    }
    private sealed class ImageHandler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
        }
    }

    [AvaloniaFact]
    public async Task Catalog_image_and_release_hint_are_visible_in_library_without_refetching()
    {
        using var profile = new Profile();
        var handler = new ImageHandler();
        using var client = new HttpClient(handler);
        using var manager = new GameManager(profile.Store, client);
        var url = "https://example.invalid/catalog-cover.png";
        // Populate the exact pre-existing catalog loader/cache, not the library's Icons cache.
        using var catalogLoader = new DiskCachedWebImageLoader(client, false, Path.Combine(manager.CacheFolder, "Images"));
        (await catalogLoader.ProvideImageAsync(url)).Should().NotBeNull();
        handler.Requests.Should().Be(1);
        var app = new GameInfo { Name = "Catalog app", FolderName = "LibraryFolder", Repository = "test/" + Guid.NewGuid().ToString("N"), GameIconUrl = url, PreferredVersion = "v1" };
        CatalogPlatformIndex.Set("github", app.Repository, "v1", null,
            new GitHubRelease { tag_name = "v1", assets = [] });
        var service = new CatalogReviewService(manager, new(profile.Store), () => throw new Exception("Unexpected sort"));
        await service.PresentAddedAsync(app);

        manager.Games.Should().ContainSingle().Which.Should().BeSameAs(app);
        app.IconUrl.Should().Be(LauncherArtworkLoader.CachedPath(manager.CacheFolder, url));
        File.Exists(app.IconUrl).Should().BeTrue();
        app.LatestVersion.Should().Be("v1");
        app.PreferredVersion.Should().Be("v1", "a browsing hint must not clear release selection");
        handler.Requests.Should().Be(1, "library artwork must reuse the catalog download");
        Directory.GetFiles(Path.Combine(manager.CacheFolder, "Icons")).Should().BeEmpty();
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Respond = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Respond.Task.WaitAsync(cancellationToken);
            var latest = "{\"tag_name\":\"v3.0.0\",\"assets\":[]}";
            var releases = "[" + latest + ",{\"tag_name\":\"v2.0.0\",\"assets\":[{\"name\":\"App-Windows.zip\",\"browser_download_url\":\"https://example.invalid/app.zip\"}]}]";
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/latest") ? latest : releases, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class DelayedArtwork : IAsyncImageLoader
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Bitmap?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Bitmap?> ProvideImageAsync(string url) { Started.TrySetResult(); return Result.Task; }
        public void Dispose() { }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Background_enrichment_does_not_block_add_and_survives_navigation(bool cancel)
    {
        using var profile = new Profile();
        var releases = new ReleaseHandler();
        using var client = new HttpClient(releases);
        using var manager = new GameManager(profile.Store, client);
        var session = new LauncherSession();
        var previousLoader = ImageLoader.AsyncImageLoader;
        var artwork = new DelayedArtwork();
        ImageLoader.AsyncImageLoader = artwork;
        try
        {
            var settings = new SettingsViewModel(profile.Store);
            var model = new CatalogSyncViewModel();
            var source = new AppCatalogSource { Id = "fixture", Location = Path.Combine(profile.Root, "catalog.json") };
            profile.Store.Current.AppCatalogSources = [source];
            await manager.CatalogService.SaveLocalAppsAsync([]);
            await manager.CatalogService.ExportLocalAppsToFileAsync(source.Location,
                [new GameInfo { Name = "New app", Repository = "test/" + Guid.NewGuid().ToString("N"), FolderName = "NewApp", GameIconUrl = "https://example.invalid/new-cover.png" }]);
            using var workspace = new CatalogReviewWorkspace(model, settings, new CatalogReviewService(manager, settings, () => { }, session),
                (m, _, _) => throw new Exception(m), () => Task.CompletedTask, session.CatalogMutations);
            await workspace.OpenAsync(source, CatalogReviewFilter.All, session.Token);
            await session.RunAsync(() => workspace.ExecuteAsync(CatalogReviewAction.Add, model.AllRows.Single().IdentityKey, session.Token)).WaitAsync(TimeSpan.FromSeconds(3));
            var app = manager.Games.Single();
            app.LatestVersion.Should().BeNull();
            (await manager.CatalogService.LoadLocalAppsAsync()).Should().ContainSingle();
            workspace.Close(); // Settings/library navigation must not cancel enrichment.
            await Task.WhenAll(releases.Started.Task, artwork.Started.Task).WaitAsync(TimeSpan.FromSeconds(3));
            if (cancel)
            {
                await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                app.LatestVersion.Should().BeNull();
            }
            else
            {
                var versionReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                app.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GameInfo.LatestVersion) && app.LatestVersion == "v2.0.0") versionReady.TrySetResult(); };
                releases.Respond.TrySetResult();
                await versionReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
                app.LatestVersion.Should().Be("v2.0.0", "normal release selection skips the assetless latest tag");
                // An image failure must not prevent the version from becoming available.
                artwork.Result.TrySetException(new IOException("Image server offline"));
                await session.DisposeAsync();
            }
        }
        finally
        {
            artwork.Result.TrySetResult(null);
            releases.Respond.TrySetResult();
            await session.DisposeAsync();
            ImageLoader.AsyncImageLoader = previousLoader;
        }
    }
}
