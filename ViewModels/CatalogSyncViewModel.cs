using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public class CatalogSyncViewModel
{
    public AppCatalogSource? Source { get; private set; }
    public IReadOnlyList<CatalogSyncRowItem> AllRows { get; private set; } = [];
    public bool ShowUpToDateApps { get; set; }
    public CatalogReviewFilter ReviewFilter { get; set; } = CatalogReviewFilter.All;
    public string SortBy { get; set; } = "Name";
    public bool IgnoreArticlesWhenSorting { get; set; } = true;

    public int ExternalOnlyCount => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.InExternalOnly &&
        Source != null &&
        CatalogCompareService.IsActionableRow(r, Source));

    public int NotInLibraryCount => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.InExternalOnly &&
        Source != null &&
        !CatalogCompareService.IsHiddenFromReview(Source, r.Repository));

    public int ChangedCount => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.Changed &&
        Source != null &&
        CatalogCompareService.IsActionableRow(r, Source));

    public int NeedsReviewCount => AllRows.Count(r =>
        Source != null && CatalogCompareService.IsActionableRow(r, Source));

    public bool ShowNeedsReviewCompleteState =>
        ReviewFilter == CatalogReviewFilter.NeedsReview && NeedsReviewCount == 0;

    public int HiddenUpToDateCount => Source == null
        ? 0
        : AllRows.Count(r =>
            r.Status == CatalogSyncStatus.Unchanged &&
            !CatalogCompareService.IsIgnoredForCurrentVersion(Source, r.Repository));

    public int HiddenCount => Source == null
        ? 0
        : AllRows.Count(r => CatalogCompareService.IsHiddenFromReview(Source, r.Repository));

    public bool HasApplicableChanges => ExternalOnlyCount > 0 || ChangedCount > 0;

    public bool ShowSkipReviewButton =>
        Source != null &&
        HasApplicableChanges &&
        !string.IsNullOrWhiteSpace(Source.CachedListVersion) &&
        (CatalogCompareService.IsUnreviewedVersion(Source.AcknowledgedListVersion) ||
         !string.Equals(Source.CachedListVersion, Source.AcknowledgedListVersion, StringComparison.Ordinal));

    public string VersionSummary =>
        Source == null
            ? ""
            : CatalogCompareService.FormatCatalogVersionSummary(
                Source.CachedListVersion,
                Source.AcknowledgedListVersion);

    public string UsageStatsText
    {
        get
        {
            if (Source == null || AllRows.Count == 0)
                return "";

            return CatalogSourceListItem.FormatUsageStats(
                AllRows.Count(r => r.Local != null),
                AllRows.Count);
        }
    }

    public string VersionBannerText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(VersionSummary))
                parts.Add(VersionSummary);
            if (!string.IsNullOrWhiteSpace(UsageStatsText))
                parts.Add(UsageStatsText);
            return string.Join("\n", parts);
        }
    }

    public string FilterSummary
    {
        get
        {
            if (ShowUpToDateApps || HiddenUpToDateCount == 0)
                return "";

            return HiddenUpToDateCount == 1
                ? "1 up-to-date app hidden"
                : $"{HiddenUpToDateCount} up-to-date apps hidden";
        }
    }

    public void Refresh(AppCatalogSource source, List<GameInfo> localApps, List<GameInfo> externalApps)
    {
        Source = source;
        source.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.HiddenFromReviewRepositories ??= new List<string>();
        AllRows = CatalogCompareService.BuildCompareRows(localApps, externalApps);
        CatalogCompareService.PruneHiddenRepositories(source, AllRows.Select(r => r.Repository));
    }

    public IEnumerable<CatalogSyncRowItem> GetVisibleRows()
    {
        if (Source == null)
            return [];

        return CatalogCompareService.SortRows(
            CatalogCompareService.FilterVisibleRows(AllRows, Source, ShowUpToDateApps),
            SortBy,
            IgnoreArticlesWhenSorting);
    }

    public IEnumerable<CatalogSyncRowItem> GetFilteredRows()
    {
        if (Source == null)
            return [];

        return CatalogCompareService.SortRows(
            CatalogCompareService.FilterByReviewFilter(AllRows, Source, ReviewFilter),
            SortBy,
            IgnoreArticlesWhenSorting);
    }
}
