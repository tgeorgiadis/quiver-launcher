using AsyncImageLoader.Loaders;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace QuiverLauncher.Services;

/// <summary>The URL cache used by both catalog and library images, including existing cached files.</summary>
/// <remarks>
/// Every image this loader decodes stays in memory for the rest of the session, so artwork is decoded
/// no larger than a card can show it: a 600x900 cover is 2 MB as pixels, and a library of a few hundred
/// apps reached 1 GB. Large views (the app page's backdrop, README images) ask for <see cref="FullSize"/>,
/// which is decoded as is and not kept.
/// </remarks>
public sealed class LauncherArtworkLoader(string imagesDirectory) : DiskCachedWebImageLoader(imagesDirectory)
{
    private const string FullSizeMarker = "#quiver-full-size";

    /// <summary>The longest side, in interface pixels, any card or thumbnail shows artwork at
    /// (the largest library cover setting).</summary>
    internal const int ThumbnailLongSide = 512;

    /// <summary>Screen pixels per interface pixel, including Windows/macOS display scaling and the
    /// launcher's own interface scale.</summary>
    public static Func<double> DisplayScale { get; set; } = () => 1;

    public static string CachedPath(string cacheDirectory, string url) =>
        Path.Combine(cacheDirectory, "Images", CreateMD5(url));

    /// <summary>Asks for the image at its own size, for views wider than a card.</summary>
    public static string? FullSize(string? url) =>
        string.IsNullOrWhiteSpace(url) || IsFullSize(url) ? url : url + FullSizeMarker;

    private static bool IsFullSize(string url) => url.EndsWith(FullSizeMarker, StringComparison.Ordinal);

    public override Task<Bitmap?> ProvideImageAsync(string url) =>
        IsFullSize(url) ? LoadAsync(url) : base.ProvideImageAsync(url);

    public override Task<Bitmap?> ProvideImageAsync(string url, IStorageProvider? storageProvider = null) =>
        IsFullSize(url) ? LoadAsync(url) : base.ProvideImageAsync(url, storageProvider);

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

        var cached = await LoadFromGlobalCache(url).ConfigureAwait(false);
        if (cached != null)
            return Fit(cached, fullSize);

        var bytes = await LoadDataFromExternalAsync(url).ConfigureAwait(false);
        if (bytes == null)
            return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = Fit(new Bitmap(stream), fullSize);
            await SaveToGlobalCache(url, bytes).ConfigureAwait(false);
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to decode image {url}: {ex.Message}");
            return null;
        }
    }

    private static Bitmap Fit(Bitmap bitmap, bool fullSize)
    {
        if (fullSize)
            return bitmap;
        var size = FittedSize(bitmap.PixelSize, (int)Math.Ceiling(ThumbnailLongSide * Math.Max(1, DisplayScale())));
        if (size == bitmap.PixelSize)
            return bitmap;
        using (bitmap)
            return bitmap.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>Shrinks a size, keeping its shape, so that neither side is longer than <paramref name="longSide"/>.</summary>
    internal static PixelSize FittedSize(PixelSize size, int longSide)
    {
        var longest = Math.Max(size.Width, size.Height);
        if (longest <= longSide)
            return size;
        var scale = (double)longSide / longest;
        return new(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
    }
}
