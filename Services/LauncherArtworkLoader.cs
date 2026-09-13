using AsyncImageLoader.Loaders;

namespace QuiverLauncher.Services;

/// <summary>The URL cache used by both catalog and library images, including existing cached files.</summary>
public sealed class LauncherArtworkLoader(string imagesDirectory) : DiskCachedWebImageLoader(imagesDirectory)
{
    public static string CachedPath(string cacheDirectory, string url) =>
        Path.Combine(cacheDirectory, "Images", CreateMD5(url));
}
