using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public interface ICatalogSourcesService
{
    Task RefreshUsageAsync(AppSettings settings);
    Task<(List<GameInfo> Apps, string? Version, string? Error)> LoadAsync(string location);
    Task RegisterAsync(AppCatalogSource source);
    void DeleteCache(string sourceId);
    Task RefreshAvailabilityAsync(AppCatalogSource source);
    Task RefreshAllAsync(AppSettings settings);
}

public sealed class CatalogSourcesService(GameManager manager) : ICatalogSourcesService
{
    public Task RefreshUsageAsync(AppSettings settings) => manager.CatalogService.RefreshAllSourcesUsageStatsAsync(settings);
    public async Task<(List<GameInfo> Apps, string? Version, string? Error)> LoadAsync(string location)
    {
        var (apps, version, error) = await manager.CatalogService.TryLoadSourceAsync(manager.HttpClient, location);
        return (apps, version, error);
    }
    public async Task RegisterAsync(AppCatalogSource source)
    {
        string? rawJson = null;
        try { rawJson = await CatalogLocationReader.Default.ReadAsync(manager.HttpClient, source.Location); }
        catch { /* Registration retries reading the source if the cached read fails. */ }
        await manager.CatalogService.RegisterNewSourceAsync(manager.HttpClient, source, rawJson);
    }
    public void DeleteCache(string sourceId) => manager.CatalogService.DeleteSourceCache(sourceId);
    public Task RefreshAvailabilityAsync(AppCatalogSource source) => manager.CatalogService.RefreshUpdateAvailableAsync(source);
    public Task RefreshAllAsync(AppSettings settings) => manager.CatalogService.RefreshAllSourcesAsync(manager.HttpClient, settings);
}
