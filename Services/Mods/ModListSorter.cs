namespace QuiverLauncher.Services.Mods;

public static class ModListSorter
{
    public const string InstalledFirst = "InstalledFirst";
    public const string NameAsc = "Name";
    public const string NameDesc = "NameDesc";
    public const string UpdatesFirst = "UpdatesFirst";
    public const string TopRated = "TopRated";
    public const string Newest = "Newest";
    public const string LastUpdated = "LastUpdated";
    public const string MostDownloaded = "MostDownloaded";

    public static bool IsRemoteSort(string? sortBy)
    {
        var mode = Normalize(sortBy);
        return mode is TopRated or Newest or LastUpdated or MostDownloaded;
    }

    public static IReadOnlyList<ModListItem> Sort(IEnumerable<ModListItem> items, string? sortBy)
    {
        var mode = Normalize(sortBy);
        var comparer = StringComparer.OrdinalIgnoreCase;

        IOrderedEnumerable<ModListItem> ordered = mode switch
        {
            NameDesc => items.OrderByDescending(i => i.DisplayName, comparer),
            NameAsc => items.OrderBy(i => i.DisplayName, comparer),
            UpdatesFirst => items
                .OrderBy(i => i.Status switch
                {
                    ModInstallStatus.UpdateAvailable => 0,
                    ModInstallStatus.Installed => 1,
                    _ => 2,
                })
                .ThenBy(i => i.DisplayName, comparer),
            TopRated => items
                .OrderByDescending(i => i.Package.RatingScore)
                .ThenBy(i => i.DisplayName, comparer),
            MostDownloaded => items
                .OrderByDescending(i => i.Package.DownloadCount)
                .ThenBy(i => i.DisplayName, comparer),
            LastUpdated => items
                .OrderByDescending(i => i.Package.UpdatedAtUnix ?? long.MinValue)
                .ThenBy(i => i.DisplayName, comparer),
            Newest => items
                .OrderByDescending(i => NewestTimestamp(i.Package))
                .ThenBy(i => i.DisplayName, comparer),
            _ => items
                .OrderBy(i => i.Status == ModInstallStatus.NotInstalled ? 1 : 0)
                .ThenBy(i => i.DisplayName, comparer),
        };

        return ordered.ToList();
    }

    /// <summary>Created time when known; otherwise last-updated as a fallback.</summary>
    private static long NewestTimestamp(ModPackage package) =>
        package.CreatedAtUnix ?? package.UpdatedAtUnix ?? long.MinValue;

    public static string Normalize(string? sortBy)
    {
        if (string.Equals(sortBy, NameAsc, StringComparison.OrdinalIgnoreCase))
            return NameAsc;
        if (string.Equals(sortBy, NameDesc, StringComparison.OrdinalIgnoreCase))
            return NameDesc;
        if (string.Equals(sortBy, UpdatesFirst, StringComparison.OrdinalIgnoreCase))
            return UpdatesFirst;
        if (string.Equals(sortBy, TopRated, StringComparison.OrdinalIgnoreCase))
            return TopRated;
        if (string.Equals(sortBy, Newest, StringComparison.OrdinalIgnoreCase))
            return Newest;
        if (string.Equals(sortBy, LastUpdated, StringComparison.OrdinalIgnoreCase))
            return LastUpdated;
        if (string.Equals(sortBy, MostDownloaded, StringComparison.OrdinalIgnoreCase))
            return MostDownloaded;
        return InstalledFirst;
    }

    /// <summary>Thunderstore cyberstorm <c>ordering</c> query value.</summary>
    public static string ToThunderstoreOrdering(string? sortBy) =>
        Normalize(sortBy) switch
        {
            TopRated => "top-rated",
            Newest => "newest",
            MostDownloaded => "most-downloaded",
            // LastUpdated and all local sorts → default listing order.
            _ => "last-updated",
        };

    /// <summary>GameBanana Mod/Index <c>_sSort</c> value (browse).</summary>
    public static string ToGameBananaIndexSort(string? sortBy) =>
        Normalize(sortBy) switch
        {
            TopRated => "Generic_MostLiked",
            Newest => "Generic_Newest",
            MostDownloaded => "Generic_MostDownloaded",
            // LastUpdated and local → NewAndUpdated (site default activity order).
            _ => "Generic_NewAndUpdated",
        };

    /// <summary>GameBanana Util/Search <c>_sOrder</c> value.</summary>
    public static string ToGameBananaSearchOrder(string? sortBy) =>
        Normalize(sortBy) switch
        {
            TopRated => "popularity",
            MostDownloaded => "popularity",
            Newest => "date",
            LastUpdated => "udate",
            _ => "udate",
        };
}
