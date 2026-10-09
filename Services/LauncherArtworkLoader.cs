using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AsyncImageLoader.Loaders;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace QuiverLauncher.Services;

/// <summary>The URL cache used by both catalog and library images, including existing cached files.</summary>
/// <remarks>
/// Artwork is decoded no larger than the cards currently show it (<see cref="ThumbnailBox"/>): a decoded
/// image costs width x height x 4 bytes however small its file is, and a library of a few hundred apps
/// reached 1 GB at a fixed 512 px. Decoded images are kept only while something shows them, plus the
/// most recent few, so pages left behind (catalog pages, app pages) give their memory back.
/// Large views (the app page's backdrop, README images) ask for <see cref="FullSize"/>,
/// which is decoded as is and not kept.
/// SteamGridDB art, often full-size PNGs, is downloaded as the smaller copy quiverlauncher.com makes
/// for its own pages (Cloudflare image resizing), falling back to the original if that copy fails.
/// </remarks>
public sealed class LauncherArtworkLoader : DiskCachedWebImageLoader
{
    private const string FullSizeMarker = "#quiver-full-size";

    // The same copies the website asks for (src/images.ts on the website), so the launcher reuses
    // copies Cloudflare has already made instead of adding to the site's monthly resizing limit.
    private const string Resizer = "https://quiverlauncher.com/cdn-cgi/image/";
    private static readonly Regex ResizableArt =
        new(@"^https://cdn2\.steamgriddb\.com/(?:grid|hero)/[0-9a-f]+\.\w+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly int[] CardWidths = [240, 360, 480, 720, 960];
    private const int CardQuality = 70;
    private const int BannerWidth = 1920;
    private const int BannerQuality = 85;

    public LauncherArtworkLoader(string imagesDirectory) : this(imagesDirectory, new HttpClient()) { }

    internal LauncherArtworkLoader(string imagesDirectory, HttpClient httpClient) : base(httpClient, true, imagesDirectory)
    {
        // Cloudflare sends WebP when asked, which is smaller than the PNG or JPEG it would otherwise send.
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("image/webp,image/*;q=0.8");
    }

    /// <summary>The largest area, in interface pixels, any card or thumbnail currently shows artwork in.
    /// MainView sets this from the library card settings and the catalog's card size.</summary>
    public static Func<Size> ThumbnailBox { get; set; } = () => new(512, 512);

    // How many decoded images are kept after nothing shows them, so going back and forth between
    // pages doesn't decode the same art again.
    private const int RecentlyUsed = 64;
    private readonly object _gate = new();
    private readonly Dictionary<string, Decoded> _decoded = [];
    private readonly Dictionary<string, Task<Bitmap?>> _loading = [];
    private readonly LinkedList<Bitmap> _recent = [];
    private static readonly ConditionalWeakTable<Bitmap, Pressure> Pressures = new();

    private sealed record Decoded(WeakReference<Bitmap> Bitmap, Size Box);

    /// <summary>Screen pixels per interface pixel, including Windows/macOS display scaling and the
    /// launcher's own interface scale.</summary>
    public static Func<double> DisplayScale { get; set; } = () => 1;

    public static string CachedPath(string cacheDirectory, string url) =>
        Path.Combine(cacheDirectory, "Images", CreateMD5(url));

    /// <summary>The website's resized copy of a cover for a card, or the URL itself when it has none.</summary>
    public static string CardUrl(string url)
    {
        var needed = 480 * Math.Max(1, DisplayScale());
        return Resized(url, CardWidths.FirstOrDefault(w => w >= needed, CardWidths[^1]), CardQuality);
    }

    /// <summary>The website's resized copy of a full-width banner, or the URL itself when it has none.</summary>
    public static string BannerUrl(string url) => Resized(url, BannerWidth, BannerQuality);

    private static string Resized(string url, int width, int quality) =>
        ResizableArt.IsMatch(url) ? $"{Resizer}width={width},quality={quality},format=auto,fit=scale-down/{url}" : url;

    /// <summary>Asks for the image at its own size, for views wider than a card.</summary>
    public static string? FullSize(string? url) =>
        string.IsNullOrWhiteSpace(url) || IsFullSize(url) ? url : url + FullSizeMarker;

    private static bool IsFullSize(string url) => url.EndsWith(FullSizeMarker, StringComparison.Ordinal);

    public override Task<Bitmap?> ProvideImageAsync(string url) => ProvideImageAsync(url, null);

    public override Task<Bitmap?> ProvideImageAsync(string url, IStorageProvider? storageProvider = null)
    {
        if (IsFullSize(url))
            return LoadAsync(url);
        var box = ThumbnailBox();
        lock (_gate)
        {
            if (_decoded.TryGetValue(url, out var decoded) && decoded.Bitmap.TryGetTarget(out var bitmap) &&
                decoded.Box.Width >= box.Width && decoded.Box.Height >= box.Height)
            {
                Touch(bitmap);
                return Task.FromResult<Bitmap?>(bitmap);
            }
            if (_loading.TryGetValue(url, out var loading))
                return loading;
            var task = LoadAndKeepAsync(url, box, storageProvider);
            if (!task.IsCompleted)
                _loading[url] = task;
            return task;
        }
    }

    private async Task<Bitmap?> LoadAndKeepAsync(string url, Size box, IStorageProvider? storageProvider)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await LoadAsync(url, storageProvider).ConfigureAwait(false);
            return bitmap;
        }
        catch (Exception ex)
        {
            // The image loader drops failures silently, so report them to see why artwork is missing.
            Telemetry.Current.CaptureException(ex, handled: true, "artwork");
            throw;
        }
        finally
        {
            lock (_gate)
            {
                _loading.Remove(url);
                if (bitmap != null)
                {
                    if (_decoded.Count > 256)
                        foreach (var dead in _decoded.Where(d => !d.Value.Bitmap.TryGetTarget(out _)).Select(d => d.Key).ToList())
                            _decoded.Remove(dead);
                    _decoded[url] = new(new(bitmap), box);
                    Touch(bitmap);
                }
            }
        }
    }

    private void Touch(Bitmap bitmap)
    {
        _recent.Remove(bitmap);
        _recent.AddFirst(bitmap);
        while (_recent.Count > RecentlyUsed)
            _recent.RemoveLast();
    }

    // AdvancedImage loads through this overload, which would open plain file paths at full size.
    protected override Task<Bitmap?> LoadAsync(string url, IStorageProvider? storageProvider) =>
        IsFullSize(url) || File.Exists(url) || IsWeb(url) ? LoadAsync(url) : base.LoadAsync(url, storageProvider);

    private static bool IsWeb(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    protected override async Task<Bitmap?> LoadAsync(string url)
    {
        var fullSize = IsFullSize(url);
        if (fullSize) url = url[..^FullSizeMarker.Length];

        // Local covers (custom covers and cached library icons) are plain paths; file:// URIs
        // (the window background) and app assets keep the default behaviour.
        if (File.Exists(url))
            return Fit(new Bitmap(url), fullSize);
        if (!IsWeb(url))
            return await base.LoadAsync(url).ConfigureAwait(false);

        // A resized copy is cached under its own URL, so the original's entry only ever holds the original.
        var resized = fullSize ? BannerUrl(url) : CardUrl(url);
        string[] sources = resized == url ? [url] : [resized, url];
        foreach (var source in sources)
        {
            var cached = await LoadFromGlobalCache(source).ConfigureAwait(false);
            if (cached != null)
                return Fit(cached, fullSize);
        }

        foreach (var source in sources)
        {
            var bytes = await LoadDataFromExternalAsync(source).ConfigureAwait(false);
            if (bytes == null)
                continue;
            try
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = Fit(new Bitmap(stream), fullSize);
                await SaveToGlobalCache(source, bytes).ConfigureAwait(false);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to decode image {source}: {ex.Message}");
            }
        }
        return null;
    }

    private static Bitmap Fit(Bitmap bitmap, bool fullSize)
    {
        if (fullSize)
            return bitmap;
        var size = FittedSize(bitmap.PixelSize, ThumbnailBox() * Math.Max(1, DisplayScale()));
        if (size != bitmap.PixelSize)
            using (bitmap)
                bitmap = bitmap.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);
        // The pixels live outside .NET's heap, so tell the GC about them; otherwise images nothing shows
        // any more can wait a long time to be collected.
        Pressures.AddOrUpdate(bitmap, new((long)size.Width * size.Height * 4));
        return bitmap;
    }

    /// <summary>Shrinks a size, keeping its shape, to the smallest that still fills <paramref name="box"/>
    /// (cards crop artwork to fill), never enlarging it.</summary>
    internal static PixelSize FittedSize(PixelSize size, Size box)
    {
        var scale = Math.Max(box.Width / size.Width, box.Height / size.Height);
        if (scale >= 1)
            return size;
        return new(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
    }

    private sealed class Pressure
    {
        private readonly long _bytes;
        public Pressure(long bytes) => GC.AddMemoryPressure(_bytes = bytes);
        ~Pressure() => GC.RemoveMemoryPressure(_bytes);
    }
}
