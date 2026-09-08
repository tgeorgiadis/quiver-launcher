using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

public static class CatalogPlatformFilterSettings
{
    /// <summary>
    /// On first catalog-review visit, default to the OS Quiver is running on and persist that choice.
    /// Later user changes are left as-is.
    /// </summary>
    public static bool EnsureDefault(AppSettings settings, string? runtimePlatform = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CatalogPlatformFilters ??= [];

        if (settings.CatalogPlatformFilterChosen)
        {
            settings.CatalogPlatformFilters = CatalogPlatformSupport.Normalize(settings.CatalogPlatformFilters).ToList();
            return false;
        }

        var platform = CatalogPlatformSupport.Canonical(runtimePlatform)
                       ?? CatalogPlatformSupport.DetectRuntimePlatform();
        settings.CatalogPlatformFilters = [platform];
        settings.CatalogPlatformFilterChosen = true;
        return true;
    }

    public static void SetFilters(AppSettings settings, IEnumerable<string>? filters)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CatalogPlatformFilters = CatalogPlatformSupport.Normalize(filters).ToList();
        settings.CatalogPlatformFilterChosen = true;
    }

    public static void SetAll(AppSettings settings) =>
        SetFilters(settings, []);

    public static void Toggle(AppSettings settings, string? platform)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SetFilters(settings, CatalogPlatformSupport.Toggle(settings.CatalogPlatformFilters, platform));
    }
}
