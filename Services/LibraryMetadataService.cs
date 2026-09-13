using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public sealed class LibraryMetadataService(GameManager manager, SettingsViewModel settings)
{
    public async Task SaveAsync(GameInfo game, MetadataEditMode mode, string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tags = mode == MetadataEditMode.Tags ? TagHelper.ParseCommaSeparatedTags(text) : null;
        var name = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (game.IsInLocalAppsJson)
        {
            var apps = await manager.CatalogService.LoadLocalAppsAsync();
            token.ThrowIfCancellationRequested();
            var saved = apps.FirstOrDefault(app => string.Equals(app.InstanceKey, game.InstanceKey, StringComparison.OrdinalIgnoreCase));
            if (saved != null)
            {
                if (mode == MetadataEditMode.Tags) saved.Tags = tags!;
                else saved.CustomDisplayName = name;
                foreach (var app in apps) app.GameManager ??= manager;
                await manager.CatalogService.SaveLocalAppsAsync(apps);
            }
            if (!string.IsNullOrWhiteSpace(game.Repository))
            {
                if (mode == MetadataEditMode.Tags) settings.Current.UserAppTags.Remove(game.Repository);
                else settings.Current.UserAppDisplayNames.Remove(game.Repository);
            }
        }
        else if (!string.IsNullOrWhiteSpace(game.Repository))
        {
            if (mode == MetadataEditMode.Tags) settings.Current.UserAppTags[game.Repository] = tags!;
            else if (name == null) settings.Current.UserAppDisplayNames.Remove(game.Repository);
            else settings.Current.UserAppDisplayNames[game.Repository] = name;
        }
        settings.Save(settings.Current);
        if (token.IsCancellationRequested) return;
        if (mode == MetadataEditMode.Tags) game.Tags = tags!;
        else game.CustomDisplayName = name;
    }
}
