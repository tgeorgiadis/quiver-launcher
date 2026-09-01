using System.Collections.ObjectModel;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public class CatalogSyncViewModel
{
    private readonly Dictionary<string, TagChipState> _tagChipStates =
        new(StringComparer.OrdinalIgnoreCase);

    public AppCatalogSource? Source { get; private set; }
    public IReadOnlyList<CatalogSyncRowItem> AllRows { get; private set; } = [];
    public ObservableCollection<TagChipListItem> TagChips { get; } = new();
    public bool ShowUpToDateApps { get; set; }
    public CatalogReviewFilter ReviewFilter { get; set; } = CatalogReviewFilter.All;
    public string SortBy { get; set; } = "Name";
    public bool IgnoreArticlesWhenSorting { get; set; } = true;
    public string SearchText { get; set; } = "";

    public int ExternalOnlyCount => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.InExternalOnly &&
        Source != null &&
        CatalogCompareService.IsActionableRow(r, Source));

    public int NotInLibraryCount => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.InExternalOnly &&
        Source != null &&
        !CatalogCompareService.IsHiddenFromReview(Source, r.ReviewKey));

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
            !CatalogCompareService.IsIgnoredForCurrentVersion(Source, r.ReviewKey));

    public int HiddenCount => Source == null
        ? 0
        : AllRows.Count(r => CatalogCompareService.IsHiddenFromReview(Source, r.ReviewKey));

    public bool HasApplicableChanges => ExternalOnlyCount > 0 || ChangedCount > 0;

    public bool ShowSkipReviewButton =>
        Source != null &&
        ReviewFilter is not CatalogReviewFilter.UpToDate and not CatalogReviewFilter.Hidden &&
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

    public string VersionLastReviewedText
    {
        get
        {
            if (Source == null)
                return "";

            if (CatalogCompareService.IsUnreviewedVersion(Source.AcknowledgedListVersion))
                return "Last reviewed: not yet";

            return string.IsNullOrWhiteSpace(Source.AcknowledgedListVersion)
                ? ""
                : $"Last reviewed: {CatalogCompareService.FormatVersionForDisplay(Source.AcknowledgedListVersion)}";
        }
    }

    public string VersionBannerTooltip
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(VersionLastReviewedText))
                parts.Add(VersionLastReviewedText);
            if (!string.IsNullOrWhiteSpace(UsageStatsText))
                parts.Add(UsageStatsText);
            return string.Join("\n", parts);
        }
    }

    /// <summary>
    /// One-line summary: list version plus library usage. Last-reviewed details go in the tooltip.
    /// </summary>
    public string VersionBannerCompactText
    {
        get
        {
            if (Source == null)
                return "";

            var version = string.IsNullOrWhiteSpace(Source.CachedListVersion)
                ? ""
                : CatalogCompareService.FormatVersionForDisplay(Source.CachedListVersion);
            var usage = CatalogSourceListItem.FormatUsageStatsShort(
                AllRows.Count(r => r.Local != null),
                AllRows.Count);

            if (string.IsNullOrWhiteSpace(version))
                return usage;
            if (string.IsNullOrWhiteSpace(usage))
                return version;
            return $"{version} · {usage}";
        }
    }

    public string VersionBannerText => VersionBannerCompactText;

    public bool ShowVersionBannerEmphasis =>
        Source != null &&
        (CatalogCompareService.IsUnreviewedVersion(Source.AcknowledgedListVersion) || HasApplicableChanges);

    public bool ShowVersionBanner => !string.IsNullOrWhiteSpace(VersionBannerCompactText);

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

    public void Refresh(
        AppCatalogSource source,
        List<GameInfo> localApps,
        List<GameInfo> externalApps,
        AppSettings? settings = null)
    {
        Source = source;
        source.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.HiddenFromReviewRepositories ??= new List<string>();
        source.FeaturedTags ??= [];
        source.PreferredTagFilters ??= [];
        source.HiddenTagFilters ??= [];
        AllRows = CatalogCompareService.BuildCompareRows(localApps, externalApps);
        CatalogCompareService.PruneHiddenRepositories(source, AllRows.Select(r => r.ReviewKey));
        RefreshTagChips(settings);
    }

    public void RefreshTagChips(AppSettings? settings)
    {
        settings?.EnsureInitialized();
        var preferred = Source?.PreferredTagFilters is { Count: > 0 }
            ? Source.PreferredTagFilters
            : Source?.FeaturedTags;
        var ranked = TagChipHelper.RankTagsByFrequency(
            AllRows.Select(r => (r.External ?? r.Local)?.Tags),
            featuredTags: preferred,
            pinnedTags: settings?.PinnedFilterTags,
            hiddenTags: Source?.HiddenTagFilters);

        var previous = _tagChipStates.ToDictionary(
            kv => kv.Key,
            kv => kv.Value,
            StringComparer.OrdinalIgnoreCase);

        _tagChipStates.Clear();
        TagChips.Clear();
        foreach (var tag in ranked)
        {
            var state = previous.TryGetValue(tag, out var prior) ? prior : TagChipState.Neutral;
            _tagChipStates[tag] = state;
            TagChips.Add(new TagChipListItem { Tag = tag, State = state });
        }
    }

    public void CycleTagChip(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        var normalized = tag.Trim().ToLowerInvariant();
        var current = _tagChipStates.TryGetValue(normalized, out var state)
            ? state
            : TagChipState.Neutral;
        var next = TagChipHelper.CycleState(current);
        _tagChipStates[normalized] = next;

        var item = TagChips.FirstOrDefault(c => c.Tag.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (item != null)
            item.State = next;
    }

    public void ClearTagChips()
    {
        foreach (var key in _tagChipStates.Keys.ToList())
            _tagChipStates[key] = TagChipState.Neutral;
        foreach (var chip in TagChips)
            chip.State = TagChipState.Neutral;
    }

    private IEnumerable<CatalogSyncRowItem> ApplyTagChipFilter(IEnumerable<CatalogSyncRowItem> rows) =>
        rows.Where(r => TagChipHelper.MatchesTriStateChips(
            (r.External ?? r.Local)?.Tags,
            _tagChipStates));

    private IEnumerable<CatalogSyncRowItem> ApplySearchFilter(IEnumerable<CatalogSyncRowItem> rows) =>
        rows.Where(r => CatalogReviewSearch.Matches(r, SearchText));

    public IEnumerable<CatalogSyncRowItem> GetVisibleRows()
    {
        if (Source == null)
            return [];

        return CatalogCompareService.SortRows(
            ApplySearchFilter(
                ApplyTagChipFilter(
                    CatalogCompareService.FilterVisibleRows(AllRows, Source, ShowUpToDateApps))),
            SortBy,
            IgnoreArticlesWhenSorting);
    }

    public IEnumerable<CatalogSyncRowItem> GetFilteredRows()
    {
        if (Source == null)
            return [];

        return CatalogCompareService.SortRows(
            ApplySearchFilter(
                ApplyTagChipFilter(
                    CatalogCompareService.FilterByReviewFilter(AllRows, Source, ReviewFilter))),
            SortBy,
            IgnoreArticlesWhenSorting);
    }

    /// <summary>
    /// Visible (review filter + tag chips + search) rows that bulk "Add all new" would apply to.
    /// </summary>
    public IReadOnlyList<CatalogSyncRowItem> GetFilteredBulkAddRows()
    {
        if (Source == null)
            return [];

        return GetFilteredRows()
            .Where(r =>
                r.CanAdd &&
                CatalogCompareService.IsActionableRow(r, Source))
            .ToList();
    }

    /// <summary>
    /// Visible new-in-catalog rows that cannot be added because the folder is already used.
    /// </summary>
    public IReadOnlyList<CatalogSyncRowItem> GetFilteredBlockedAddRows()
    {
        if (Source == null)
            return [];

        return GetFilteredRows()
            .Where(r =>
                r.HasAddBlockedReason &&
                CatalogCompareService.IsActionableRow(r, Source))
            .ToList();
    }

    /// <summary>
    /// Visible (review filter + tag chips + search) rows that bulk "Merge all changed" would apply to.
    /// </summary>
    public IReadOnlyList<CatalogSyncRowItem> GetFilteredBulkReplaceRows()
    {
        if (Source == null)
            return [];

        return GetFilteredRows()
            .Where(r =>
                r.Status == CatalogSyncStatus.Changed &&
                CatalogCompareService.IsActionableRow(r, Source))
            .ToList();
    }

    public int FilteredBulkAddCount => GetFilteredBulkAddRows().Count;

    public int FilteredBulkReplaceCount => GetFilteredBulkReplaceRows().Count;

    public bool ShowBulkAddButton => FilteredBulkAddCount > 0;

    public bool ShowBulkReplaceButton => FilteredBulkReplaceCount > 0;

    public int ActiveTagChipCount => TagChips.Count(c => !c.IsNeutral);
}
