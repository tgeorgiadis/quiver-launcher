using QuiverLauncher.Core.Models;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class LibraryFiltersViewModelTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(Path.GetTempPath(), "quiver-filter-tests", Guid.NewGuid().ToString("N")) };
        public int Saves { get; private set; }
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
    [Fact]
    public void Changing_scope_and_deleting_active_filter_refresh_visible_apps()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        var filters = new LibraryFiltersViewModel(manager, library);
        var filter = new TagDisplayFilter { Name = "Favorites", Tags = ["favorite"] };
        store.Current.TagDisplayFilters.Add(filter);
        manager.SetCatalogAppsAndFilter([
            new GameInfo { Name = "Favorite", Repository = "owner/favorite", FolderName = "favorite", Tags = ["favorite"], Status = GameStatus.Installed },
            new GameInfo { Name = "Other", Repository = "owner/other", FolderName = "other", Status = GameStatus.Installed },
            new GameInfo { Name = "Missing", Repository = "owner/missing", FolderName = "missing", Status = GameStatus.NotInstalled }
        ], store.Current);
        filters.Toggle(filter.Id);
        manager.Games.Should().ContainSingle(g => g.Name == "Favorite");
        filters.SetScope(AppListScope.InstalledOnly);
        store.Current.ActiveTagDisplayFilterId.Should().Be(filter.Id);
        filters.Delete(filter.Id);
        store.Current.ActiveTagDisplayFilterId.Should().BeNull();
        manager.Games.Select(g => g.Name).Should().BeEquivalentTo(["Favorite", "Other"]);
        store.Saves.Should().Be(3);
    }
    [Fact]
    public void Drag_preview_moves_existing_rows_and_only_persists_when_committed()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        var filters = new LibraryFiltersViewModel(manager, library);
        store.Current.TagDisplayFilters = [new() { Name = "First", Tags = ["one"] }, new() { Name = "Second", Tags = ["two"] }];
        library.RefreshFilters();
        var row = library.TagDisplayFilters[0];
        filters.PreviewMove(row.Id, 1);
        library.TagDisplayFilters[1].Should().BeSameAs(row);
        store.Current.TagDisplayFilters[1].Id.Should().Be(row.Id);
        store.Saves.Should().Be(0);
        filters.Save();
        store.Saves.Should().Be(1);
    }
}
