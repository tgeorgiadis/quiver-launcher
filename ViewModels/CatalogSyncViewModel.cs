using System.Collections.ObjectModel;
using System.Text.Json;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public class CatalogSyncViewModel : ObservableViewModel
{
    // One presentation owns all derived values. Bindings and bulk controls read the
    // same results instead of independently traversing the catalog on every get.
    private Dictionary<string, object>? _presentation;
    public void BeginPresentation() => _presentation = new();
    private T Snapshot<T>(string key, Func<T> calculate) where T : notnull
    {
        if (_presentation == null) return calculate();
        if (_presentation.TryGetValue(key, out var value)) return (T)value;
        var result = calculate();
        _presentation[key] = result;
        return result;
    }
    private void InvalidatePresentation()
    {
        // Keep snapshot mode active during invalidation. A metadata notification
        // must not make each binding fall back to a separate catalog traversal.
        if (_presentation != null) _presentation = new();
    }
    private Dictionary<string, CatalogPlatformEntry?>? _presentedPlatforms;
    private bool _useLivePlatforms;
    private bool _awaitingPlatformChecks = true;
    public void ResetPlatformPresentation() { _presentedPlatforms = null; _awaitingPlatformChecks = true; InvalidatePresentation(); }
    private string MetadataKey(CatalogSyncRowItem row)
    {
        var game = row.External ?? row.Local;
        return CatalogPlatformIndex.Key(game?.EffectiveRepositorySource, game?.Repository ?? "", game?.PreferredVersion,
            game == null ? null : GetPlatformToken(game));
    }
    private CatalogPlatformEntry? LiveMetadata(CatalogSyncRowItem row)
    {
        var game = row.External ?? row.Local;
        CatalogPlatformIndex.TryGet(game?.EffectiveRepositorySource, game?.Repository, game?.PreferredVersion,
            game == null ? null : GetPlatformToken(game), out var entry);
        return entry;
    }
    public void AcceptPlatformDiscoveries()
    {
        InvalidatePresentation();
        _presentedPlatforms = AllRows.DistinctBy(MetadataKey).ToDictionary(MetadataKey, LiveMetadata);
        foreach (var row in AllRows)
        {
            var game = row.External ?? row.Local;
            if (game == null || string.IsNullOrWhiteSpace(game.Repository)) continue;
            var publicKey = CatalogPlatformIndex.Key(game.EffectiveRepositorySource, game.Repository, game.PreferredVersion);
            CatalogPlatformIndex.TryGet(game.EffectiveRepositorySource, game.Repository, game.PreferredVersion, null, out var publicEntry);
            _presentedPlatforms.TryAdd(publicKey, publicEntry);
        }
        RefreshCompatibilityLabels();
    }
    private bool IsVerifiedBulkCompatible(CatalogSyncRowItem row)
    {
        var entry = LiveMetadata(row);
        return entry != null && (CatalogPlatformSupport.IsAll(EffectivePlatformFilters) ||
            CatalogPlatformSupport.Matches(CatalogPlatformSupport.FromMetadata(entry, (row.External ?? row.Local)?.ReleaseAssetFilter),
                CatalogPlatformSupport.ParseFilters(EffectivePlatformFilters)));
    }

    public void RefreshCompatibilityLabels()
    {
        var platforms = string.Join(" / ", EffectivePlatformFilters.Select(p => p == "Mac" ? "macOS" : p));
        foreach (var row in AllRows)
        {
            var entry = LiveMetadata(row);
            if (entry == null)
            {
                var checking = (_awaitingPlatformChecks || IsCheckingPlatforms) && !string.IsNullOrWhiteSpace((row.External ?? row.Local)?.Repository);
                row.SetCompatibility(checking ? CatalogCompatibilityState.Checking : CatalogCompatibilityState.Unverified,
                    checking ? "Checking compatibility…" : "Compatibility unverified");
            }
            else if (IsVerifiedBulkCompatible(row)) row.SetCompatibility(CatalogCompatibilityState.Available, "");
            else row.SetCompatibility(CatalogCompatibilityState.Unavailable, $"Not available for {platforms}");
        }
    }
    private CatalogPlatformEntry? PresentedMetadata(CatalogSyncRowItem row)
    {
        if (_presentedPlatforms == null) return LiveMetadata(row);
        if (_presentedPlatforms.TryGetValue(MetadataKey(row), out var entry)) return entry;
        var game = row.External ?? row.Local;
        return game == null ? null : _presentedPlatforms.GetValueOrDefault(
            CatalogPlatformIndex.Key(game.EffectiveRepositorySource, game.Repository ?? "", game.PreferredVersion));
    }
    public void EnsurePlatformPresentation() { if (_presentedPlatforms == null) AcceptPlatformDiscoveries(); }
    public void DeferPlatformDiscoveries() { InvalidatePresentation(); RefreshCompatibilityLabels(); NotifyChanged(); }
    public int MoreAppsAvailable => Snapshot(nameof(MoreAppsAvailable), CalculateMoreAppsAvailable);
    private int CalculateMoreAppsAvailable()
    {
            if (_presentedPlatforms == null) return 0;
            var current = GetFilteredRows().Select(r => r.IdentityKey).ToHashSet();
            _useLivePlatforms = true;
            try { return GetFilteredRows().Count(r => !current.Contains(r.IdentityKey)); }
            finally { _useLivePlatforms = false; }
    }
    public bool HasPlatformDiscoveries => Snapshot(nameof(HasPlatformDiscoveries), () => _presentedPlatforms != null && AllRows.Any(row =>
    {
        var old = PresentedMetadata(row);
        var current = LiveMetadata(row);
        return old == null != (current == null) || old != null && current != null &&
            (old.ReleaseTag != current.ReleaseTag || !old.AssetNames.SequenceEqual(current.AssetNames));
    }));
    public string PlatformDiscoveriesText => MoreAppsAvailable is > 0 and var count
        ? $"{count} more {(count == 1 ? "app" : "apps")} available" : "Apply platform updates";
    public string PlatformRetryText => "Retry";
    public int StalePlatformTargets => Snapshot(nameof(StalePlatformTargets), () => CatalogReleaseIndexWarmup.CollectTargets(AllRows, GetPlatformToken).Count(t =>
        CatalogPlatformIndex.TryGet(t.RepositorySource, t.Repository, t.PreferredVersion, t.GetApiToken(), out _) &&
        !CatalogPlatformIndex.IsFresh(t.RepositorySource, t.Repository, t.PreferredVersion, t.GetApiToken())));
    public string? SharedMetadataError => PublishedPlatformCache.Error(Source?.PlatformMetadataUrl);
    public bool ShowPlatformExplanation => !string.IsNullOrEmpty(PlatformCheckExplanation);
    private CatalogReleaseWarmupProgress? _platformCheck;
    public bool IsCheckingPlatforms => _platformCheck?.Outcome == CatalogReleaseWarmupOutcome.Running;
    public int UnresolvedPlatformTargets => Snapshot(nameof(UnresolvedPlatformTargets), () => CatalogReleaseIndexWarmup.CollectTargets(AllRows, GetPlatformToken)
        .Count(t => Source?.IsCommunityManaged == true
            ? !CatalogPlatformIndex.TryGet(t.RepositorySource, t.Repository, t.PreferredVersion, t.GetApiToken(), out _)
            : !CatalogPlatformIndex.IsFresh(t.RepositorySource, t.Repository, t.PreferredVersion, t.GetApiToken())));
    public bool ShowPlatformCheck => IsCheckingPlatforms || UnverifiedPlatformCount > 0 || (Source == null && ShowPlatformFailure);
    public bool ShowCompatibilityStatus => ShowPlatformCheck || ShowHiddenPendingReviews;
    public bool ShowPlatformFailure => _platformCheck?.Outcome is CatalogReleaseWarmupOutcome.RateLimited or CatalogReleaseWarmupOutcome.Failed;
    public bool ShowPlatformRetry => ShowPlatformCheck && !_awaitingPlatformChecks && !IsCheckingPlatforms && UnresolvedPlatformTargets > 0 &&
        (_platformCheck?.Failure?.RetryAt is not { } retryAt || retryAt <= DateTimeOffset.UtcNow);
    public string PlatformProvider => _platformCheck?.Failure?.Provider ?? "github";
    public string PlatformTokenButtonText => PlatformProvider == "gitlab" ? "GitLab token settings" : "GitHub token settings";
    public bool ShowPlatformTokenShortcut => ShowPlatformFailure &&
        (_platformCheck?.Failure?.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
         _platformCheck?.Outcome == CatalogReleaseWarmupOutcome.RateLimited && _platformCheck?.Failure?.IsAuthenticated != true);
    public int UnverifiedPlatformCount => Snapshot(nameof(UnverifiedPlatformCount), () => Source == null ? 0 :
        ApplySearchFilter(ApplyTagChipFilter(CatalogCompareService.FilterByReviewFilter(AllRows, Source, ReviewFilter)))
            .Count(row => !HasPlatformMetadata(row)));
    private string GetPlatformToken(GameInfo game) => SettingsModel == null ? game.GetReleaseApiToken() : game.GetReleaseApiToken(SettingsModel.Current);
    private bool HasPlatformMetadata(CatalogSyncRowItem row)
    {
        var game = row.External ?? row.Local;
        return CatalogPlatformIndex.TryGet(game?.EffectiveRepositorySource ?? row.EffectiveRepositorySource,
            game?.Repository ?? row.Repository, game?.PreferredVersion, game == null ? null : GetPlatformToken(game), out _);
    }
    public string UnverifiedPlatformText => UnverifiedPlatformCount == 0 ? "" :
        "These apps remain available to browse and add individually. Add all includes only verified compatible apps.";
    public string PlatformEmptyText => AllRows.Count == 0 ? "No apps in this list yet." : "No apps match this filter.";
    public string PlatformCheckText => IsCheckingPlatforms || (_awaitingPlatformChecks && UnverifiedPlatformCount > 0)
        ? $"Checking compatibility for {Math.Max(UnverifiedPlatformCount, _platformCheck?.Unresolved ?? 0)} apps…"
        : _platformCheck?.Outcome == CatalogReleaseWarmupOutcome.RateLimited ? "Compatibility checks paused"
        : $"{UnverifiedPlatformCount} {(UnverifiedPlatformCount == 1 ? "app" : "apps")} unverified";
    public string PlatformCheckExplanation
    {
        get
        {
            var failure = _platformCheck?.Failure;
            var provider = PlatformProvider == "gitlab" ? "GitLab" : "GitHub";
            if (_platformCheck?.Outcome == CatalogReleaseWarmupOutcome.RateLimited)
            {
                var quota = failure?.IsAuthenticated == true ? "authenticated" : "unauthenticated";
                var kind = failure?.RateLimitKind == ReleaseRateLimitKind.Secondary ? "secondary request limit" : $"{quota} request limit";
                var retry = failure?.RetryAt is { } at ? $" Retry after {at.ToLocalTime():g}." : " Retry after the limit resets.";
                var automatic = _platformCheck?.WillRetry == true ? " Checks will resume automatically in the background." : " Select Retry platform checks to try again.";
                return $"{_platformCheck?.Unresolved ?? UnresolvedPlatformTargets} release checks remain unresolved. {provider}'s {kind} was reached.{retry}{automatic}" +
                    (ShowPlatformTokenShortcut ? $" Add a valid {provider} token to use the authenticated allowance." : "");
            }
            if (failure?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return $"{provider} rejected the saved token. Update it in token settings, then retry.";
            if (!ShowPlatformFailure && Source?.IsCommunityManaged == true)
                return SharedMetadataError ?? (UnresolvedPlatformTargets > 0
                    ? "Compatibility information is not available for every app yet. Missing information is checked automatically; you can still browse and add apps individually."
                    : StalePlatformTargets > 0 ? "Published metadata is over 24 hours old. Previously verified platforms remain available while the shared index is refreshed." : "");
            return ShowPlatformFailure ? $"{_platformCheck?.Unresolved ?? UnresolvedPlatformTargets} release checks remain unresolved. " + (failure == null ? "Some platform checks have not completed. Retry to continue." :
                $"Release metadata is unavailable ({provider}, HTTP {(int)failure.StatusCode}): {failure.ErrorMessage ?? "Request failed."}") +
                " Cached platform information is retained. Unverified apps remain available to browse and add individually." : "";
        }
    }
    public void SetPlatformCheck(CatalogReleaseWarmupProgress? progress)
    {
        var next = progress?.Outcome == CatalogReleaseWarmupOutcome.Completed ? null : progress;
        if (Equals(_platformCheck, next) && !_awaitingPlatformChecks) return;
        _awaitingPlatformChecks = false;
        _platformCheck = next;
        RefreshCompatibilityLabels();
        BeginPresentation();
        Notify(null);
    }
    public ResettableObservableCollection<CatalogSyncRowItem> Rows { get; } = new();
    public bool HasTagChips => TagChips.Count > 0;
    public void NotifyChanged() { if (_presentation != null) BeginPresentation(); Notify(null); }
    public void PublishPresentation() => Notify(null);
    public SettingsViewModel? SettingsModel { get; set; }
    public double CardPixelSize => PlatformCapabilities.IsMobile ? double.NaN : Math.Max(120, SettingsModel?.Current.SlotSize ?? 180);
    private readonly Dictionary<string, TagChipState> _tagChipStates =
        new(StringComparer.OrdinalIgnoreCase);

    public AppCatalogSource? Source { get; private set; }
    public IReadOnlyList<CatalogSyncRowItem> AllRows { get; private set; } = [];
    private Dictionary<string, (CatalogSyncRowItem Row, string Snapshot)> _rowSnapshots = [];
    public ObservableCollection<TagChipListItem> TagChips { get; } = new();
    private bool _ShowUpToDateApps = false;
    public bool ShowUpToDateApps { get => _ShowUpToDateApps; set { if (Equals(_ShowUpToDateApps, value)) return; _ShowUpToDateApps = value; InvalidatePresentation(); } }
    private CatalogReviewFilter _ReviewFilter = CatalogReviewFilter.All;
    public CatalogReviewFilter ReviewFilter { get => _ReviewFilter; set { if (Equals(_ReviewFilter, value)) return; _ReviewFilter = value; InvalidatePresentation(); } }
    private string _SortBy = "Name";
    public string SortBy { get => _SortBy; set { if (Equals(_SortBy, value)) return; _SortBy = value; InvalidatePresentation(); } }
    private bool _IgnoreArticlesWhenSorting = true;
    public bool IgnoreArticlesWhenSorting { get => _IgnoreArticlesWhenSorting; set { if (Equals(_IgnoreArticlesWhenSorting, value)) return; _IgnoreArticlesWhenSorting = value; InvalidatePresentation(); } }
    private string _SearchText = "";
    public string SearchText { get => _SearchText; set { if (Equals(_SearchText, value)) return; _SearchText = value; InvalidatePresentation(); } }
    private IReadOnlyList<string> _PlatformFilters = [];
    public IReadOnlyList<string> PlatformFilters { get => _PlatformFilters; set { if (Equals(_PlatformFilters, value)) return; _PlatformFilters = value; InvalidatePresentation(); } }
    private bool _RevealPendingPlatforms = false;
    public bool RevealPendingPlatforms { get => _RevealPendingPlatforms; set { if (Equals(_RevealPendingPlatforms, value)) return; _RevealPendingPlatforms = value; InvalidatePresentation(); } }
    public IReadOnlyList<string> EffectivePlatformFilters => RevealPendingPlatforms ? [] : PlatformFilters;
    private bool IsPendingForPlatform(CatalogSyncRowItem row) => Source != null &&
        CatalogReviewEligibility.IsPending(row, Source, EffectivePlatformFilters,
            (row.External ?? row.Local) is { } app ? GetPlatformToken(app) : null);
    public int VisiblePendingReviewCount => Snapshot(nameof(VisiblePendingReviewCount), () => Source == null ? 0 :
        ApplySearchFilter(ApplyPlatformFilter(ApplyTagChipFilter(
            AllRows.Where(IsPendingForPlatform)))).Count());
    public int FilteredOutPendingReviewCount => Math.Max(0, NeedsReviewCount - VisiblePendingReviewCount);
    public bool ShowHiddenPendingReviews => ReviewFilter == CatalogReviewFilter.NeedsReview && FilteredOutPendingReviewCount > 0;
    public string HiddenPendingReviewsText => FilteredOutPendingReviewCount == 1
        ? "1 review hidden by filters"
        : $"{FilteredOutPendingReviewCount} reviews hidden by filters";
    public void RevealAllPendingReviews()
    {
        ReviewFilter = CatalogReviewFilter.NeedsReview;
        SearchText = "";
        ClearTagChips();
        RevealPendingPlatforms = true;
        NotifyChanged();
    }

    public int ExternalOnlyCount => Snapshot(nameof(ExternalOnlyCount), () => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.InExternalOnly &&
        Source != null &&
        IsPendingForPlatform(r)));

    public int NotInLibraryCount => Snapshot(nameof(NotInLibraryCount), () => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.InExternalOnly &&
        Source != null &&
        !CatalogCompareService.IsHiddenFromReview(Source, r.ReviewKey) &&
        CatalogReviewEligibility.IsRelevant(r, EffectivePlatformFilters,
            (r.External ?? r.Local) is { } app ? GetPlatformToken(app) : null)));

    public int ChangedCount => Snapshot(nameof(ChangedCount), () => AllRows.Count(r =>
        r.Status == CatalogSyncStatus.Changed &&
        Source != null &&
        IsPendingForPlatform(r)));

    public int NeedsReviewCount => Snapshot(nameof(NeedsReviewCount), () => AllRows.Count(IsPendingForPlatform));

    public bool ShowNeedsReviewCompleteState =>
        ReviewFilter == CatalogReviewFilter.NeedsReview && NeedsReviewCount == 0;
    public string NeedsReviewCompleteText => CatalogPlatformSupport.IsAll(EffectivePlatformFilters)
        ? "You're all caught up — nothing needs review."
        : "Nothing needs review for the selected platform. Choose All platforms to review apps for other platforms.";

    public int HiddenUpToDateCount => Snapshot(nameof(HiddenUpToDateCount), () => Source == null
        ? 0
        : AllRows.Count(r =>
            r.Status == CatalogSyncStatus.Unchanged &&
            !CatalogCompareService.IsIgnoredForCurrentVersion(Source, r.ReviewKey)));

    public int HiddenCount => Snapshot(nameof(HiddenCount), () => Source == null
        ? 0
        : AllRows.Count(r => CatalogCompareService.IsHiddenFromReview(Source, r.ReviewKey)));

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

    public sealed record PreparedRow(CatalogSyncRowItem Row, string Snapshot);
    public static IReadOnlyList<PreparedRow> PrepareComparison(List<GameInfo> localApps, List<GameInfo> externalApps)
    {
        using var timing = CatalogPerformance.Measure("comparison", externalApps.Count);
        return CatalogCompareService.BuildCompareRows(localApps, externalApps).Select(row =>
        {
            var snapshot = JsonSerializer.Serialize(new
            {
                row.Status, row.DisplayName, row.AddBlockedReason,
                Local = row.Local == null ? null : AppCatalogService.SerializeApp(row.Local),
                External = row.External == null ? null : AppCatalogService.SerializeApp(row.External),
            });
            return new PreparedRow(row, snapshot);
        }).ToList();
    }

    public void Refresh(
        AppCatalogSource source,
        List<GameInfo> localApps,
        List<GameInfo> externalApps,
        AppSettings? settings = null, IReadOnlyList<PreparedRow>? prepared = null)
    {
        InvalidatePresentation();
        var sameSource = ReferenceEquals(Source, source);
        Source = source;
        source.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.HiddenFromReviewRepositories ??= new List<string>();
        source.FeaturedTags ??= [];
        source.PreferredTagFilters ??= [];
        source.HiddenTagFilters ??= [];
        var snapshots = new Dictionary<string, (CatalogSyncRowItem Row, string Snapshot)>();
        AllRows = (prepared ?? PrepareComparison(localApps, externalApps)).Select(item =>
        {
            var row = item.Row;
            var snapshot = item.Snapshot;
            if (sameSource && _rowSnapshots.TryGetValue(row.IdentityKey, out var previous) && previous.Snapshot == snapshot)
                row = previous.Row;
            snapshots[row.IdentityKey] = (row, snapshot);
            return row;
        }).ToList();
        _rowSnapshots = snapshots;
        CatalogCompareService.PruneHiddenRepositories(source, AllRows.Select(r => r.ReviewKey));
        RefreshTagChips(settings);
        RefreshCompatibilityLabels();
    }

    public void ReconcileAddition(List<GameInfo> localApps)
    {
        InvalidatePresentation();
        if (Source == null) return;
        var previous = AllRows.Where(r => r.External != null).ToDictionary(r => r.External!.InstanceKey, StringComparer.OrdinalIgnoreCase);
        var next = CatalogCompareService.BuildCompareRows(localApps, AllRows.Where(r => r.External != null).Select(r => r.External!).ToList(), previous);
        foreach (var row in next)
        {
            if (row.External == null || !previous.TryGetValue(row.External.InstanceKey, out var existing) || ReferenceEquals(row, existing)) continue;
            if (existing.IsAddPending) continue;
            // Keep row identity, hover and gamepad focus; no collection reset or icon reload.
            existing.UpdateComparison(row);
            _rowSnapshots.Remove(existing.IdentityKey);
        }
        Source.LibraryAppCount = AllRows.Count(r => r.Local != null);
        Source.ListAppCount = AllRows.Count;
        CatalogReviewEligibility.Reconcile(Source, AllRows, SettingsModel?.Current);
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

        if (TagChips.Select(chip => chip.Tag).SequenceEqual(ranked)) return;

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
        InvalidatePresentation();
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
        InvalidatePresentation();
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

    private IEnumerable<CatalogSyncRowItem> ApplyPlatformFilter(IEnumerable<CatalogSyncRowItem> rows)
    {
        if (CatalogPlatformSupport.IsAll(EffectivePlatformFilters))
            return rows;

        return rows.Where(MatchesPlatformFilter);
    }

    internal static bool MatchesPlatformFilter(CatalogSyncRowItem row, IReadOnlyList<string> platformFilters, string? token = null)
    {
        var game = row.External ?? row.Local;
        if (!CatalogPlatformIndex.TryGet(game?.EffectiveRepositorySource ?? row.EffectiveRepositorySource,
                game?.Repository ?? row.Repository, game?.PreferredVersion, token, out _)) return true;
        return CatalogPlatformSupport.AppMatches(
            game?.EffectiveRepositorySource ?? row.EffectiveRepositorySource,
            game?.Repository ?? row.Repository,
            game?.ReleaseAssetFilter,
            platformFilters, game?.PreferredVersion, token);
    }

    private bool MatchesPlatformFilter(CatalogSyncRowItem row)
    {
        if (_presentedPlatforms == null || _useLivePlatforms)
            return MatchesPlatformFilter(row, EffectivePlatformFilters, (row.External ?? row.Local) is { } game ? GetPlatformToken(game) : null);
        var entry = PresentedMetadata(row);
        if (entry == null) return true;
        return CatalogPlatformSupport.Matches(CatalogPlatformSupport.FromMetadata(entry, (row.External ?? row.Local)?.ReleaseAssetFilter), CatalogPlatformSupport.ParseFilters(EffectivePlatformFilters));
    }

    public IEnumerable<CatalogSyncRowItem> GetVisibleRows()
    {
        if (Source == null)
            return [];

        return CatalogCompareService.SortRows(
            ApplySearchFilter(
                ApplyPlatformFilter(
                    ApplyTagChipFilter(
                        CatalogCompareService.FilterVisibleRows(AllRows, Source, ShowUpToDateApps)))),
            SortBy,
            IgnoreArticlesWhenSorting);
    }

    public IEnumerable<CatalogSyncRowItem> GetFilteredRows()
        => Snapshot(_useLivePlatforms ? "liveRows" : "rows", () => CalculateFilteredRows().ToList());

    private IEnumerable<CatalogSyncRowItem> CalculateFilteredRows()
    {
        if (Source == null)
            return [];

        return CatalogCompareService.SortRows(
            ApplySearchFilter(
                ApplyPlatformFilter(
                    ApplyTagChipFilter(
                        CatalogCompareService.FilterByReviewFilter(AllRows, Source, ReviewFilter)))),
            SortBy,
            IgnoreArticlesWhenSorting);
    }

    /// <summary>
    /// Visible (review filter + tag chips + platform + search) rows that bulk "Add all new" would apply to.
    /// </summary>
    public IReadOnlyList<CatalogSyncRowItem> GetFilteredBulkAddRows()
    {
        if (Source == null)
            return [];

        return GetFilteredRows()
            .Where(r =>
                r.CanAdd && IsVerifiedBulkCompatible(r) &&
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
