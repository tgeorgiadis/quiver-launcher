using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

/// <summary>A card the controller can highlight in the App Catalog: an app, or a game a search matched.</summary>
public interface IBrowseCard
{
    bool IsGamepadFocused { get; set; }
}

/// <summary>An original game a search matched, as on the website: its art, title and how many apps play it.</summary>
public sealed class BrowseGame(string slug, string title, string? art, int ways) : ObservableViewModel, IBrowseCard
{
    private bool _isGamepadFocused;

    public string Slug { get; } = slug;
    public string Title { get; } = title;
    public string? Art { get; } = art;
    public int Ways { get; } = ways;
    public string WaysText => Ways == 1 ? "1 way to play \u2192" : $"{Ways} ways to play \u2192";
    public bool IsGamepadFocused { get => _isGamepadFocused; set => Set(ref _isGamepadFocused, value); }

    /// <summary>The game's box art, else its other artwork.</summary>
    public static string? ArtFor(QuiverCatalogGameInfo game) =>
        new[] { game.LibraryArt?.Capsule, game.Artwork, game.LibraryArt?.Header }.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
}
