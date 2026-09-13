using Avalonia.Platform.Storage;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public sealed class LibraryCustomizationService(GameManager manager, LibraryViewModel library, LauncherSession session,
    Func<IStorageProvider> storage, Func<string, string, bool, Task<bool>> message)
{
    public Task SetIconAsync(GameInfo? game) => session.RunAsync(async () =>
    {
        if (game == null) { await message("Unable to identify the selected app.", "Error", false); return; }
        var replacing = !string.IsNullOrEmpty(game.CustomIconPath);
        if (replacing && !await message($"Replace the existing custom icon for {game.Name}?", "Confirm Icon Replacement", true)) return;
        session.Token.ThrowIfCancellationRequested();
        var files = await storage().OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select Custom Icon for {game.Name}",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.ico" } },
                new FilePickerFileType("PNG Files") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG Files") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                new FilePickerFileType("WebP Files") { Patterns = new[] { "*.webp" } },
                new FilePickerFileType("Bitmap Files") { Patterns = new[] { "*.bmp" } },
                new FilePickerFileType("Icon Files") { Patterns = new[] { "*.ico" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
            },
            AllowMultiple = false,
        }).WaitAsync(session.Token);
        session.Token.ThrowIfCancellationRequested();
        if (files.Count == 0) return;
        try { game.SetCustomIcon(files[0].Path.LocalPath, manager.CacheFolder); }
        catch (Exception ex) { await message($"Failed to {(replacing ? "replace" : "set")} custom icon: {ex.Message}", "Error", false); }
    });

    public Task RemoveIconAsync(GameInfo? game) => session.RunAsync(async () =>
    {
        if (game == null) { await message("Unable to identify the selected app.", "Error", false); return; }
        if (string.IsNullOrEmpty(game.CustomIconPath))
        {
            await message($"{game.Name} is already using the default icon.", "No Custom Icon", false);
            return;
        }
        var confirmed = await message($"Remove custom icon for {game.Name}?", "Confirm Removal", true);
        session.Token.ThrowIfCancellationRequested();
        if (!confirmed) return;
        try { game.RemoveCustomIcon(); }
        catch (Exception ex) { await message($"Failed to remove custom icon: {ex.Message}", "Error", false); }
    });

    public Task ToggleHiddenAsync(GameInfo? game) => session.RunAsync(async () =>
    {
        if (game == null) { await message("Unable to identify the selected app.", "Error", false); return; }
        try
        {
            var wasHidden = manager.IsManuallyHidden(game);
            manager.ToggleUserHide(game);
            await manager.LoadGamesAsync();
            session.Token.ThrowIfCancellationRequested();
            library.ApplySorting();
            if (!wasHidden) await message("App hidden from the library.\n\nYou can find it again under Show → Hidden, then choose Customize → Unhide App.", "App Hidden", false);
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested) { }
        catch (Exception ex) { if (!session.IsClosed) await message($"Failed to update app visibility: {ex.Message}", "Error", false); }
    });
}
