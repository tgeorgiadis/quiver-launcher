namespace QuiverLauncher.Services;

/// <summary>Navigation and host visibility needed when an update check presents results.</summary>
public interface IUpdatePresentation
{
    bool CanPresentResults { get; }
    bool CanShowFailureSummary { get; }
    void OpenAppUpdatesReview();
    void OpenCatalogSources();
    Task OpenCatalogReviewAsync();
    void UpdateStatusChanged();
}
