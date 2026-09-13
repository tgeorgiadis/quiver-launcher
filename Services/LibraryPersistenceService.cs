using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public sealed class LibraryPersistenceService(GameManager manager)
{
    public async Task SaveVersionPreferencesAsync(GameInfo game, string? preferredVersion, string? skippedUpdateVersion)
    {
        game.SetVersionPreferences(preferredVersion, skippedUpdateVersion);
        var apps = await manager.CatalogService.LoadLocalAppsAsync();
        var saved = apps.FirstOrDefault(g => string.Equals(g.InstanceKey, game.InstanceKey, StringComparison.OrdinalIgnoreCase));
        if (saved == null) return;
        saved.PreferredVersion = game.PreferredVersion;
        saved.SkippedUpdateVersion = game.SkippedUpdateVersion;
        saved.AutoUpdate = game.AutoUpdate;
        saved.DeferUpdateTracking = game.DeferUpdateTracking;
        await SaveAsync(apps);
    }
    public async Task SaveAutoUpdateAsync(GameInfo game)
    {
        var apps = await manager.CatalogService.LoadLocalAppsAsync();
        var saved = apps.FirstOrDefault(g => string.Equals(g.InstanceKey, game.InstanceKey, StringComparison.OrdinalIgnoreCase));
        if (saved == null) return;
        saved.AutoUpdate = game.AutoUpdate;
        await SaveAsync(apps);
    }
    public async Task SaveInstallLocationAsync(GameInfo game)
    {
        var apps = await manager.CatalogService.LoadLocalAppsAsync();
        GameInstallLocationService.PersistTo(apps, game);
        await SaveAsync(apps);
    }
    private Task SaveAsync(List<GameInfo> apps)
    {
        foreach (var app in apps) app.GameManager ??= manager;
        return manager.CatalogService.SaveLocalAppsAsync(apps);
    }
}
