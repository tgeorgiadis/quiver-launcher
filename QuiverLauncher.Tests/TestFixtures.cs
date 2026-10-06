namespace QuiverLauncher.Tests;

using QuiverLauncher.Services;

internal static class TestFixtures
{
    /// <summary>An app list file in the apps.json format, as people point Browse at.</summary>
    public static string N64RecompListPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "community-app-catalog", "N64-Recomps.json");

    public static string ReadN64RecompListJson() =>
        File.ReadAllText(N64RecompListPath);

    public static (AppCatalogService Service, string Directory) CreateIsolatedCatalogService(
        ICatalogLocationReader? locationReader = null,
        string? dataDirectory = null)
    {
        var dir = dataDirectory ?? Path.Combine(Path.GetTempPath(), "QuiverLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
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
