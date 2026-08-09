using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public class AppCatalogViewModel
{
    public CatalogReviewFilter ReviewFilter { get; set; } = CatalogReviewFilter.All;

    public string? SelectedSourceId { get; private set; }

    public void SelectSource(string? sourceId) => SelectedSourceId = sourceId;

    public void ClearSelection() => SelectedSourceId = null;

    public static int CountPendingReviews(AppSettings settings) =>
        settings.AppCatalogSources
            .Where(s => s.Enabled)
            .Sum(s => s.PendingReviewCount);
}
