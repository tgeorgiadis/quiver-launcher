using System.Net;
using System.Text.Json;
using AsyncImageLoader.Loaders;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogListIconTests
{
    [Theory]
    [InlineData("https://example.com/icon.png", "https://example.com/icon.png")]
    [InlineData("  http://example.com/icon.png  ", "http://example.com/icon.png")]
    [InlineData("/icon.png", null)]
    [InlineData("file:///C:/icon.png", null)]
    [InlineData("ftp://example.com/icon.png", null)]
    [InlineData("not a URL", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Metadata_accepts_only_absolute_web_urls(string? value, string? expected)
    {
        var source = new AppCatalogSource { IconUrl = "https://example.com/old.png" };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { iconUrl = value }));
        AppCatalogService.ApplyListMetadata(source, document.RootElement);
        source.IconUrl.Should().Be(expected);
        CatalogSourceListItem.FromSource(source).IconUrl.Should().Be(expected);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"iconUrl\":42}")]
    [InlineData("{\"iconUrl\":{}}")]
    public void Missing_or_invalid_metadata_removes_previous_icon_without_rejecting_list(string json)
    {
        var source = new AppCatalogSource { IconUrl = "https://example.com/old.png" };
        using var document = JsonDocument.Parse(json);
        AppCatalogService.ApplyListMetadata(source, document.RootElement);
        source.IconUrl.Should().BeNull();
    }

    [Fact]
    public async Task Registration_refresh_cached_fallback_and_settings_preserve_icon_lifecycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuiverCatalogIcons", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var reader = new Reader();
            var service = new AppCatalogService(locationReader: reader, dataDirectory: root);
            using var client = new HttpClient();
            var source = new AppCatalogSource { Id = "icons", Location = "https://example.com/apps.json" };
            await service.RegisterNewSourceAsync(client, source);
            source.IconUrl.Should().Be("https://example.com/first.png");

            reader.Json = """{"version":"1.1","iconUrl":"https://example.com/second.png","apps":[]}""";
            await service.FetchSourceAsync(client, source);
            source.IconUrl.Should().Be("https://example.com/second.png");
            var checkedAt = source.LastFetchedUtc;

            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Current.AppCatalogSources = [source];
            store.Save(store.Current);
            var restored = new FileSettingsStore(Path.Combine(root, "settings.json")).Current.AppCatalogSources.Single();
            restored.IconUrl.Should().Be(source.IconUrl);

            // Older settings can recover the metadata directly from the existing cache.
            restored.IconUrl = null;
            reader.Offline = true;
            (await service.FetchSourceAsync(client, restored)).Should().BeTrue();
            restored.IconUrl.Should().Be("https://example.com/second.png");
            restored.LastFetchedUtc.Should().Be(checkedAt);
            restored.LastError.Should().NotBeNullOrEmpty();

            reader.Offline = false;
            reader.Json = """{"version":"1.2","apps":[]}""";
            await service.FetchSourceAsync(client, restored);
            restored.IconUrl.Should().BeNull();
            restored.LastError.Should().BeNull();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Icon_changes_invalidate_card_presentation()
    {
        var source = new AppCatalogSource { IconUrl = "https://example.com/first.png" };
        var first = CatalogSourceListItem.FromSource(source);
        source.IconUrl = "https://example.com/second.png";
        first.HasSamePresentation(CatalogSourceListItem.FromSource(source)).Should().BeFalse();
        source.IconUrl = null;
        first.HasSamePresentation(CatalogSourceListItem.FromSource(source)).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Shared_artwork_cache_reopens_downloaded_icon_offline()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuiverCatalogArtwork", Guid.NewGuid().ToString("N"));
        var handler = new ImageHandler();
        using var client = new HttpClient(handler);
        const string url = "https://example.com/catalog-icon.png";
        try
        {
            using (var writer = new DiskCachedWebImageLoader(client, false, Path.Combine(root, "Images")))
                (await writer.ProvideImageAsync(url)).Should().NotBeNull();
            File.Exists(LauncherArtworkLoader.CachedPath(root, url)).Should().BeTrue();
            handler.Offline = true;
            using var reader = new DiskCachedWebImageLoader(client, false, Path.Combine(root, "Images"));
            (await reader.ProvideImageAsync(url)).Should().NotBeNull();
            handler.Requests.Should().Be(1, "a new loader should read the disk cache without a network request");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Reader : ICatalogLocationReader
    {
        public string Json = """{"version":"1.0","iconUrl":"https://example.com/first.png","apps":[]}""";
        public bool Offline;
        public Task<string> ReadAsync(HttpClient client, string location, CancellationToken cancellationToken = default) => Offline
            ? Task.FromException<string>(new IOException("Offline")) : Task.FromResult(Json);
    }

    private sealed class ImageHandler : HttpMessageHandler
    {
        public bool Offline;
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline) throw new HttpRequestException("Offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="))
            });
        }
    }
}
