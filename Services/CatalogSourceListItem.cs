namespace QuiverLauncher.Services
{
    using System.ComponentModel;
    using QuiverLauncher.Core.Services;

    public class CatalogSourceListItem : INotifyPropertyChanged
    {
        private bool _isGamepadFocused;
        private bool _enabled;

        public event PropertyChangedEventHandler? PropertyChanged;
        public string SourceId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Location { get; init; } = "";
        public string LocationToolTip { get; init; } = "";
        public string TitleToolTip { get; init; } = "";
        public string Description { get; init; } = "";
        public string? IconUrl { get; init; }
        public string ReviewStatusText { get; init; } = "";
        public bool ReviewStatusVisible => !string.IsNullOrEmpty(ReviewStatusText);

        public bool DescriptionVisible => !string.IsNullOrWhiteSpace(Description);

        public string StatusText { get; init; } = "";
        public string UsageStatsText { get; init; } = "";
        public bool UsageStatsVisible => !string.IsNullOrEmpty(UsageStatsText);
        public string UsageStatsShort { get; init; } = "";
        public bool UsageStatsFullLibrary { get; init; }
        public string FetchStatusText { get; init; } = "";
        public bool FetchStatusVisible => !string.IsNullOrEmpty(FetchStatusText);
        public string StatusWarningText { get; init; } = "";
        public bool StatusWarningVisible => !string.IsNullOrEmpty(StatusWarningText);
        public bool StatusWarningIsError { get; init; }
        public bool StatusWarningIsWarning => StatusWarningVisible && !StatusWarningIsError;
        public string VersionLineText { get; init; } = "";
        public bool VersionLineVisible => !string.IsNullOrEmpty(VersionLineText);
        public bool VersionLineUnreviewed { get; init; }
        public bool MetaStripVisible =>
            !string.IsNullOrEmpty(UsageStatsShort) ||
            FetchStatusVisible ||
            StatusWarningVisible ||
            VersionLineVisible;

        public string ListVersionText { get; init; } = "";
        public string LastReviewedText { get; init; } = "";
        public bool LastReviewedUnreviewed { get; init; }
        public bool VersionRowVisible { get; init; }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowReviewPendingStyle)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NeedsReviewHighlight)));
            }
        }
        public bool UpdateAvailable { get; init; }
        public int PendingReviewCount { get; init; }
        public string ReviewButtonText { get; init; } = "Browse apps";
        public bool IsAllReviewed { get; init; }
        public bool IsPlatformReviewed { get; init; }
        private static string DeviceOperatingSystem => CatalogPlatformSupport.DetectRuntimePlatform() is "Mac" ? "macOS" : CatalogPlatformSupport.DetectRuntimePlatform();
        public string ReviewedToolTip => IsPlatformReviewed
            ? $"All apps available for {DeviceOperatingSystem} have been reviewed. Other platforms still have apps to review."
            : "All reviewed";
        public string? ReviewStatusToolTip => AllReviewedVisible ? ReviewedToolTip : null;
        public bool PendingReviewBadgeVisible => PendingReviewCount > 0;
        public bool AllReviewedVisible => IsAllReviewed || IsPlatformReviewed;
        public bool ShowReviewPendingStyle => Enabled && PendingReviewCount > 0;
        public bool NeedsReviewHighlight => Enabled && (UpdateAvailable || PendingReviewCount > 0);

        public bool IsGamepadFocused
        {
            get => _isGamepadFocused;
            set
            {
                if (_isGamepadFocused == value)
                    return;

                _isGamepadFocused = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGamepadFocused)));
            }
        }

        public static CatalogSourceListItem FromSource(AppCatalogSource source)
        {
            var versionParts = CatalogCompareService.FormatCatalogVersionParts(
                source.CachedListVersion,
                source.AcknowledgedListVersion);
            var isAllReviewed = IsAllReviewedSource(source);
            var statusWarning = GetStatusWarningText(source);

            return new()
            {
                SourceId = source.Id,
                Name = source.Name,
                Location = source.Location,
                LocationToolTip = string.IsNullOrWhiteSpace(source.Location) ? "" : source.Location,
                TitleToolTip = GetTitleToolTip(source),
                Description = source.Description?.Trim() ?? "",
                IconUrl = CatalogListMetadata.NormalizeIconUrl(source.IconUrl),
                ReviewStatusText = GetReviewStatusText(source),
                Enabled = source.Enabled,
                UpdateAvailable = source.UpdateAvailable,
                PendingReviewCount = source.PendingReviewCount,
                StatusText = GetStatusText(source),
                UsageStatsText = FormatUsageStats(source),
                UsageStatsShort = FormatLibraryMembership(source.LibraryAppCount, source.ListAppCount),
                UsageStatsFullLibrary = source.ListAppCount > 0 &&
                                        source.LibraryAppCount == source.ListAppCount,
                FetchStatusText = GetFetchStatusText(source),
                StatusWarningText = statusWarning.HasValue ? statusWarning.Value.Text : "",
                StatusWarningIsError = statusWarning.HasValue && statusWarning.Value.IsError,
                VersionLineText = GetVersionLineText(source, versionParts, isAllReviewed),
                VersionLineUnreviewed = versionParts.LastReviewedUnreviewed && !isAllReviewed &&
                    !(source.PendingReviewCount == 0 && source.PlatformExcludedReviewCount > 0),
                ReviewButtonText = GetReviewButtonText(source),
                ListVersionText = versionParts.ListVersionText,
                LastReviewedText = versionParts.LastReviewedText,
                LastReviewedUnreviewed = versionParts.LastReviewedUnreviewed,
                VersionRowVisible = versionParts.VersionRowVisible,
                IsAllReviewed = isAllReviewed,
                IsPlatformReviewed = source.PendingReviewCount == 0 && !source.UpdateAvailable &&
                    source.PlatformExcludedReviewCount > 0 && !string.IsNullOrWhiteSpace(source.CachedListVersion),
            };
        }

        internal bool HasSamePresentation(CatalogSourceListItem other) =>
            IconUrl == other.IconUrl && ReviewStatusText == other.ReviewStatusText &&
            (SourceId, Name, Location, LocationToolTip, TitleToolTip, Description, Enabled,
                UpdateAvailable, PendingReviewCount, StatusText, UsageStatsText, UsageStatsShort,
                UsageStatsFullLibrary, FetchStatusText, StatusWarningText, StatusWarningIsError,
                VersionLineText, VersionLineUnreviewed, ReviewButtonText, ListVersionText,
                LastReviewedText, LastReviewedUnreviewed, VersionRowVisible, IsAllReviewed, IsPlatformReviewed)
            .Equals((other.SourceId, other.Name, other.Location, other.LocationToolTip, other.TitleToolTip,
                other.Description, other.Enabled, other.UpdateAvailable, other.PendingReviewCount, other.StatusText,
                other.UsageStatsText, other.UsageStatsShort, other.UsageStatsFullLibrary, other.FetchStatusText,
                other.StatusWarningText, other.StatusWarningIsError, other.VersionLineText, other.VersionLineUnreviewed,
                other.ReviewButtonText, other.ListVersionText, other.LastReviewedText, other.LastReviewedUnreviewed,
                other.VersionRowVisible, other.IsAllReviewed, other.IsPlatformReviewed));

        public static bool IsAllReviewedSource(AppCatalogSource source) =>
            source.PendingReviewCount == 0 &&
            !source.UpdateAvailable &&
            !string.IsNullOrWhiteSpace(source.CachedListVersion) &&
            CatalogCompareService.IsReviewedVersion(source.AcknowledgedListVersion) &&
            string.Equals(
                source.CachedListVersion,
                source.AcknowledgedListVersion,
                StringComparison.Ordinal);

        public static string GetReviewButtonText(AppCatalogSource source) =>
            source.PendingReviewCount > 0
                ? "Review apps"
                : "Browse apps";

        public static string GetReviewStatusText(AppCatalogSource source)
        {
            if (source.PendingReviewCount > 0)
                return $"{source.PendingReviewCount} {(source.PendingReviewCount == 1 ? "app" : "apps")} to review";
            if (source.PlatformExcludedReviewCount > 0 && !source.UpdateAvailable && !string.IsNullOrWhiteSpace(source.CachedListVersion))
                return "All reviewed";
            if (IsAllReviewedSource(source))
                return "All reviewed";
            // The persisted flag can be present before the live counts finish loading.
            if (source.UpdateAvailable)
                return "Review pending";
            if (!string.IsNullOrWhiteSpace(source.CachedListVersion))
                return "Not reviewed yet";
            return "";
        }

        public static string GetTitleToolTip(AppCatalogSource source)
        {
            var name = source.Name?.Trim() ?? "";
            var location = source.Location?.Trim() ?? "";
            var lines = new List<string> { name };
            if (!string.IsNullOrEmpty(location) && !string.Equals(name, location, StringComparison.Ordinal))
                lines.Add(location);
            if (!string.IsNullOrWhiteSpace(source.CachedListVersion))
                lines.Add($"Catalog version: {source.CachedListVersion}");
            lines.Add(CatalogCompareService.IsReviewedVersion(source.AcknowledgedListVersion)
                ? $"Last reviewed version: {source.AcknowledgedListVersion}"
                : "Not reviewed yet");
            return string.Join("\n", lines.Where(line => !string.IsNullOrEmpty(line)));
        }

        public static string FormatUsageStats(AppCatalogSource source) =>
            FormatUsageStats(source.LibraryAppCount, source.ListAppCount);

        public static string FormatUsageStats(int libraryAppCount, int listAppCount)
        {
            if (listAppCount <= 0)
                return "";

            var appLabel = listAppCount == 1 ? "app" : "apps";
            return $"Using {libraryAppCount}/{listAppCount} {appLabel} from this list";
        }

        public static string FormatUsageStatsShort(int libraryAppCount, int listAppCount)
        {
            if (listAppCount <= 0)
                return "";

            var appLabel = listAppCount == 1 ? "app" : "apps";
            return $"{libraryAppCount}/{listAppCount} {appLabel} in library";
        }

        public static string FormatLibraryMembership(int libraryAppCount, int listAppCount)
        {
            if (listAppCount <= 0)
                return "";

            var appLabel = listAppCount == 1 ? "app" : "apps";
            return $"{libraryAppCount} of {listAppCount} {appLabel} in your library";
        }

        public static string GetStatusText(AppCatalogSource source)
        {
            var warning = GetStatusWarningText(source);
            if (warning.HasValue)
                return warning.Value.Text;

            var reviewStatus = GetReviewStatusText(source);
            return string.IsNullOrEmpty(reviewStatus) ? GetFetchStatusText(source) : reviewStatus;
        }

        public static string GetFetchStatusText(AppCatalogSource source)
        {
            if (source.LastFetchedUtc.HasValue)
                return $"Last checked {source.LastFetchedUtc.Value.ToLocalTime():g}";

            return "Not loaded yet";
        }

        public static StatusWarning? GetStatusWarningText(AppCatalogSource source)
        {
            if (!string.IsNullOrEmpty(source.LastError))
                return new StatusWarning(AppCatalogService.FormatCatalogFetchError(source.LastError), IsError: true);

            return null;
        }

        public static string GetVersionLineText(
            AppCatalogSource source,
            CatalogVersionParts versionParts,
            bool isAllReviewed)
        {
            if (string.IsNullOrWhiteSpace(versionParts.ListVersionText))
            {
                if (versionParts.LastReviewedUnreviewed)
                    return "Not reviewed yet";

                if (!string.IsNullOrWhiteSpace(versionParts.LastReviewedText))
                    return $"Reviewed v{versionParts.LastReviewedText}";

                return "";
            }

            var listVersion = $"List v{versionParts.ListVersionText}";
            if (source.PendingReviewCount == 0 && source.PlatformExcludedReviewCount > 0)
                return $"{listVersion} · No pending reviews for {DeviceOperatingSystem}";
            if (isAllReviewed)
                return listVersion;

            if (versionParts.LastReviewedUnreviewed)
                return $"{listVersion} · Not reviewed yet";

            return $"{listVersion} · Reviewed v{versionParts.LastReviewedText}";
        }

        public readonly record struct StatusWarning(string Text, bool IsError);
    }
}
