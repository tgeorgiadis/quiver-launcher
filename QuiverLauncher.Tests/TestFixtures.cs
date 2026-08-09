namespace QuiverLauncher.Tests;

using QuiverLauncher.Services;

internal static class TestFixtures
{
    public static string CommunityAppCatalogDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "community-app-catalog");

    public static string CommunityIndexPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "index.json");

    public static string N64RecompListPath =>
        Path.Combine(CommunityAppCatalogDirectory, "N64-Recomps.json");

    public static string ReadCommunityIndexJson() =>
        File.ReadAllText(CommunityIndexPath);

    public static string ReadN64RecompListJson() =>
        File.ReadAllText(N64RecompListPath);

    public static string CommunityCatalogPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "quiver-community-apps-catalog.json");

    public static (AppCatalogService Service, string Directory) CreateIsolatedCatalogService(
        ICatalogLocationReader? locationReader = null,
        string? dataDirectory = null)
    {
        var dir = dataDirectory ?? Path.Combine(Path.GetTempPath(), "QuiverLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Cache", "CatalogSources"));
        return (new AppCatalogService(null, locationReader, dir), dir);
    }

    public static void CleanupDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temp test directories.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup for temp test directories.
        }
    }
}
