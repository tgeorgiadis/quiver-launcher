namespace QuiverLauncher.Services;

using QuiverLauncher;

public static class CommunityCatalogDefaults
{
    public const string DefaultSourceId = "7e036b19-0b6d-4978-92e3-d180e5e9b9cb";
    public const string DefaultSourceName = "Quiver Community App Catalog";
    public const string DefaultCatalogUrl =
        "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/quiver-community-apps-catalog.json";

    /// <summary>
    /// Remote registry of community catalog lists. Quiver Launcher fetches this on startup and refresh
    /// to discover list sources and their remoteLocation URLs.
    /// </summary>
    public const string RemoteIndexUrl =
        "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/index.json";

    public const string FirstRunWelcomeTitle = "Welcome to Quiver Launcher";

    public const string FirstRunWelcomeMessage =
        """
        Discover community apps and manage downloads and updates in one place.

        Browse the app catalog, choose what you'd like to add, then download it from your library.

        You'll need an internet connection to load the catalog and download apps.
        """;

    public static bool IsLegacyDefaultSource(AppCatalogSource source) =>
        string.Equals(source.Id, DefaultSourceId, StringComparison.Ordinal) ||
        (string.Equals(source.Name, DefaultSourceName, StringComparison.Ordinal) &&
         string.Equals(source.Location, DefaultCatalogUrl, StringComparison.OrdinalIgnoreCase));

    [Obsolete("Use CommunityCatalogDefaults.IsLegacyDefaultSource instead.")]
    public static bool IsDefaultSource(AppCatalogSource source) =>
        IsLegacyDefaultSource(source);

    [Obsolete("Community sources are discovered from RemoteIndexUrl at runtime.")]
    public static AppCatalogSource CreateDefaultSource() =>
        new()
        {
            Id = DefaultSourceId,
            Name = DefaultSourceName,
            Location = DefaultCatalogUrl,
            Enabled = true,
        };

    public static AppCatalogSource? GetFirstRunReviewSource(AppSettings settings)
    {
        settings.EnsureInitialized();

        return settings.AppCatalogSources
            .Where(s => s.Enabled && s.IsCommunityManaged && s.PendingReviewCount > 0)
            .OrderByDescending(s => s.PendingReviewCount)
            .FirstOrDefault()
            ?? settings.AppCatalogSources
                .Where(s => s.Enabled && s.IsCommunityManaged)
                .FirstOrDefault()
            ?? settings.AppCatalogSources
                .Where(s => s.Enabled && s.PendingReviewCount > 0)
                .OrderByDescending(s => s.PendingReviewCount)
                .FirstOrDefault();
    }
}
