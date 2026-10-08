using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogLibrarySyncTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuiverLauncher.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => TestFixtures.CleanupDirectory(_directory);

    private static QuiverCatalogApp Entry(string name, string project, string? artwork, params string[] tags) =>
        new() { Name = name, ProjectName = project, Artwork = artwork, Tags = [.. tags] };

    [Fact]
    public void Library_apps_follow_the_catalog_but_keep_what_the_player_changed()
    {
        // Added from an old app list, with a tag the player added themselves and their own display name.
        var app = new GameInfo
        {
            Name = "Pokemon Red", Project = "Gen1Recomp", GameIconUrl = "https://old.test/icon.png", CustomDisplayName = "My Pokemon",
            Tags = ["recreation", "gb", "favourites"],
        };

        // The first sync takes the catalog's name, project and icon, and adds its tags to the player's.
        CatalogLibrarySync.Apply(app, Entry("Pokémon Red and Blue", "G1R Deluxe", "https://site.test/g1r.png", "recomp", "gb")).Should().BeTrue();
        (app.Name, app.Project, app.GameIconUrl, app.CustomDisplayName).Should().Be(
            ("Pokémon Red and Blue", "G1R Deluxe", "https://site.test/g1r.png", "My Pokemon"));
        app.Tags.Should().Equal("recreation", "gb", "favourites", "recomp");

        // The player renames the project, and removes a catalog tag; then the catalog changes everything.
        app.Project = "My G1R";
        app.Tags.Remove("recomp");
        CatalogLibrarySync.Apply(app, Entry("Pokémon Red, Blue and Yellow", "G1R", "https://site.test/g1r-2.png", "recomp", "pokemon"))
            .Should().BeTrue();
        (app.Name, app.Project, app.GameIconUrl).Should().Be(("Pokémon Red, Blue and Yellow", "My G1R", "https://site.test/g1r-2.png"));
        // "gb" was the catalog's and it dropped it; "pokemon" is new; "recomp" stays removed; "favourites" is theirs.
        app.Tags.Should().Equal("recreation", "favourites", "pokemon");

        // Nothing new from the catalog: nothing to save.
        CatalogLibrarySync.Apply(app, Entry("Pokémon Red, Blue and Yellow", "G1R", "https://site.test/g1r-2.png", "recomp", "pokemon"))
            .Should().BeFalse();
        // An icon the catalog borrows from the game isn't the app's own.
        CatalogLibrarySync.Apply(app, new QuiverCatalogApp
        {
            Name = "Pokémon Red, Blue and Yellow", ProjectName = "G1R", Artwork = "https://site.test/game-box.png", ArtworkFromGame = true,
            Tags = ["recomp", "pokemon"],
        });
        app.GameIconUrl.Should().Be("https://site.test/g1r-2.png");
    }

    [Fact]
    public async Task What_the_catalog_last_set_is_kept_in_apps_json()
    {
        Directory.CreateDirectory(_directory);
        var service = new AppCatalogService(dataDirectory: _directory);
        var app = new GameInfo { Name = "Alpha", FolderName = "Alpha", Repository = "owner/alpha", Tags = ["mine"] };
        var entry = Entry("Alpha", "Alpha Port", "https://site.test/alpha.png", "port");
        entry.Id = "k1alpha";
        CatalogLibrarySync.Apply(app, entry);

        await service.SaveLocalAppsAsync([app]);
        var loaded = (await service.LoadLocalAppsAsync()).Single();

        loaded.CatalogSnapshot.Should().BeEquivalentTo(new CatalogSnapshot("Alpha", "Alpha Port", "https://site.test/alpha.png", ["port"]));
        loaded.Tags.Should().Equal("mine", "port");
        // The catalog entry it belongs to, which links it first next time.
        loaded.CatalogEntryId.Should().Be("k1alpha");
        // So a player's later change is still told apart from the catalog's after a restart.
        loaded.Project = "Renamed";
        CatalogLibrarySync.Apply(loaded, Entry("Alpha", "Alpha Port 2", null, "port"));
        loaded.Project.Should().Be("Renamed");
    }
}
