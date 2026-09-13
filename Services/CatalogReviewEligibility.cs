using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

/// <summary>Platform exclusions affect review presentation, never acknowledgement.</summary>
public static class CatalogReviewEligibility
{
    public static bool IsRelevant(CatalogSyncRowItem row, IEnumerable<string> platforms, string? token = null)
    {
        if (CatalogPlatformSupport.IsAll(platforms)) return true;
        var app = row.External ?? row.Local;
        // Unknown evidence remains unresolved, not proof of incompatibility.
        if (app == null || !CatalogPlatformIndex.TryGet(app.EffectiveRepositorySource,
                app.Repository, app.PreferredVersion, token, out var metadata)) return true;
        return CatalogPlatformSupport.Matches(CatalogPlatformSupport.FromMetadata(metadata!, app.ReleaseAssetFilter),
            CatalogPlatformSupport.ParseFilters(platforms));
    }

    public static bool IsPending(CatalogSyncRowItem row, AppCatalogSource source,
        IEnumerable<string> platforms, string? token = null) =>
        CatalogCompareService.IsActionableRow(row, source) && IsRelevant(row, platforms, token);

    public static void Reconcile(AppCatalogSource source, IReadOnlyList<CatalogSyncRowItem> rows,
        AppSettings? settings = null, string? devicePlatform = null)
    {
        var actionable = rows.Where(r => CatalogCompareService.IsActionableRow(r, source)).ToList();
        source.PendingReviewCount = actionable.Count(r => IsRelevant(r,
            [devicePlatform ?? CatalogPlatformSupport.DetectRuntimePlatform()],
            settings == null ? null : (r.External ?? r.Local)?.GetReleaseApiToken(settings)));
        source.PlatformExcludedReviewCount = actionable.Count - source.PendingReviewCount;
        source.UpdateAvailable = source.PendingReviewCount > 0;
        // Use the unfiltered count: changing platforms or gaining a new release
        // must make an excluded entry reviewable without changing the catalog version.
        AppCatalogService.TryAutoAcknowledgeIfReviewComplete(source, actionable.Count);
    }
}
