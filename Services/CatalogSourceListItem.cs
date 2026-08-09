namespace QuiverLauncher.Services
{
    using System.ComponentModel;

    public class CatalogSourceListItem : INotifyPropertyChanged
    {
        private bool _isGamepadFocused;

        public event PropertyChangedEventHandler? PropertyChanged;
        public string SourceId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Location { get; init; } = "";
        public string LocationToolTip { get; init; } = "";
        public string TitleToolTip { get; init; } = "";
        public string Description { get; init; } = "";

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

        public bool Enabled { get; set; }
        public bool UpdateAvailable { get; init; }
        public int PendingReviewCount { get; init; }
        public string ReviewButtonText { get; init; } = "Review";
        public bool IsAllReviewed { get; init; }
        public bool PendingReviewBadgeVisible => PendingReviewCount > 0;
        public bool AllReviewedVisible => IsAllReviewed;
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
                Enabled = source.Enabled,
                UpdateAvailable = source.UpdateAvailable,
                PendingReviewCount = source.PendingReviewCount,
                StatusText = GetStatusText(source),
                UsageStatsText = FormatUsageStats(source),
                UsageStatsShort = FormatUsageStatsShort(source.LibraryAppCount, source.ListAppCount),
                UsageStatsFullLibrary = source.ListAppCount > 0 &&
                                        source.LibraryAppCount == source.ListAppCount,
                FetchStatusText = statusWarning.HasValue ? "" : GetFetchStatusText(source),
                StatusWarningText = statusWarning.HasValue ? statusWarning.Value.Text : "",
                StatusWarningIsError = statusWarning.HasValue && statusWarning.Value.IsError,
                VersionLineText = GetVersionLineText(source, versionParts, isAllReviewed),
                VersionLineUnreviewed = versionParts.LastReviewedUnreviewed && !isAllReviewed,
                ReviewButtonText = GetReviewButtonText(source),
                ListVersionText = versionParts.ListVersionText,
                LastReviewedText = versionParts.LastReviewedText,
                LastReviewedUnreviewed = versionParts.LastReviewedUnreviewed,
                VersionRowVisible = versionParts.VersionRowVisible,
                IsAllReviewed = isAllReviewed,
            };
        }

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
                ? $"Review ({source.PendingReviewCount})"
                : "View";

        public static string GetTitleToolTip(AppCatalogSource source)
        {
            var name = source.Name?.Trim() ?? "";
            var location = source.Location?.Trim() ?? "";
            if (string.IsNullOrEmpty(location) || string.Equals(name, location, StringComparison.Ordinal))
                return name;

            return $"{name}\n{location}";
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

        public static string GetStatusText(AppCatalogSource source)
        {
            var warning = GetStatusWarningText(source);
            if (warning.HasValue)
                return warning.Value.Text;

            return GetFetchStatusText(source);
        }

        public static string GetFetchStatusText(AppCatalogSource source)
        {
            if (source.LastFetchedUtc.HasValue)
                return $"Updated {source.LastFetchedUtc.Value.ToLocalTime():g}";

            return "Not loaded yet";
        }

        public static StatusWarning? GetStatusWarningText(AppCatalogSource source)
        {
            if (source.UpdateAvailable)
                return new StatusWarning("Update available", IsError: false);

            if (!string.IsNullOrEmpty(source.LastError))
                return new StatusWarning(source.LastError, IsError: true);

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
                    return "Reviewed not yet";

                if (!string.IsNullOrWhiteSpace(versionParts.LastReviewedText))
                    return $"Reviewed v{versionParts.LastReviewedText}";

                return "";
            }

            var listVersion = $"List v{versionParts.ListVersionText}";
            if (isAllReviewed)
                return listVersion;

            if (versionParts.LastReviewedUnreviewed)
                return $"{listVersion} · Reviewed not yet";

            return $"{listVersion} · Reviewed v{versionParts.LastReviewedText}";
        }

        public readonly record struct StatusWarning(string Text, bool IsError);
    }
}
