using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Tests;

public class GameManagerFilterTests
{
    private static (GameManager manager, FileSettingsStore store, string tempDir) CreateManager()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var store = new FileSettingsStore(Path.Combine(tempDir, "settings.json"));
        var manager = new GameManager(store);
        return (manager, store, tempDir);
    }

    private static GameInfo CreateGame(string name, string folder, GameStatus status, params string[] tags) =>
        new()
        {
            Name = name,
            FolderName = folder,
            Repository = $"owner/{folder}",
            Status = status,
            Tags = tags.ToList(),
        };

    [Fact]
    public void InstalledOnly_scope_excludes_not_installed_apps()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        settings.ListScope = AppListScope.InstalledOnly;

        var catalog = new List<GameInfo>
        {
            CreateGame("Installed", "installed", GameStatus.Installed, "n64"),
            CreateGame("Not Installed", "missing", GameStatus.NotInstalled, "n64"),
        };

        manager.SetCatalogAppsAndFilter(catalog, settings);

        manager.Games.Should().ContainSingle(g => g.FolderName == "installed");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Tag_filter_and_InstalledOnly_scope_compose()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        settings.ListScope = AppListScope.InstalledOnly;
        var filter = new TagDisplayFilter { Name = "N64", Tags = ["n64"] };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        var catalog = new List<GameInfo>
        {
            CreateGame("N64 Installed", "n64-installed", GameStatus.Installed, "n64"),
            CreateGame("N64 Missing", "n64-missing", GameStatus.NotInstalled, "n64"),
            CreateGame("Other Installed", "other", GameStatus.Installed, "pc"),
        };

        manager.SetCatalogAppsAndFilter(catalog, settings);

        manager.Games.Should().ContainSingle(g => g.FolderName == "n64-installed");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Tag_filter_preserves_ListScope_when_applied()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        settings.ListScope = AppListScope.InstalledOnly;
        var filter = new TagDisplayFilter { Name = "N64", Tags = ["n64"] };
        settings.TagDisplayFilters.Add(filter);

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("N64 Installed", "n64-installed", GameStatus.Installed, "n64"),
            CreateGame("Other Installed", "other", GameStatus.Installed, "pc"),
        ],
        settings);

        settings.ActiveTagDisplayFilterId = filter.Id;
        manager.ApplyTagDisplayFilter(settings);

        settings.ListScope.Should().Be(AppListScope.InstalledOnly);
        manager.Games.Should().ContainSingle(g => g.FolderName == "n64-installed");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void SetListScope_AllApps_preserves_ActiveTagDisplayFilterId()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        settings.ListScope = AppListScope.InstalledOnly;
        var filter = new TagDisplayFilter { Name = "N64", Tags = ["n64"] };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("N64 Installed", "n64-installed", GameStatus.Installed, "n64"),
            CreateGame("N64 Missing", "n64-missing", GameStatus.NotInstalled, "n64"),
        ],
        settings);

        manager.SetListScope(AppListScope.AllApps, settings);

        settings.ListScope.Should().Be(AppListScope.AllApps);
        settings.ActiveTagDisplayFilterId.Should().Be(filter.Id);
        manager.Games.Select(g => g.FolderName).Should().BeEquivalentTo("n64-installed", "n64-missing");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Manually_hidden_app_stays_hidden_in_all_and_installed_scopes()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        var game = CreateGame("Hidden", "hidden", GameStatus.Installed, "n64");
        settings.ManuallyHiddenApps.Add("folder:hidden");

        manager.SetCatalogAppsAndFilter([game, CreateGame("Visible", "visible", GameStatus.Installed)], settings);

        manager.Games.Should().ContainSingle(g => g.FolderName == "visible");
        game.IsManuallyHidden.Should().BeTrue();

        settings.ListScope = AppListScope.InstalledOnly;
        manager.SetCatalogAppsAndFilter(
        [
            game,
            CreateGame("Visible", "visible", GameStatus.Installed),
        ],
        settings);

        manager.Games.Should().ContainSingle(g => g.FolderName == "visible");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void HiddenOnly_scope_lists_only_manually_hidden_apps()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        var hiddenInstalled = CreateGame("Hidden Installed", "hidden-installed", GameStatus.Installed);
        var hiddenMissing = CreateGame("Hidden Missing", "hidden-missing", GameStatus.NotInstalled);
        var visible = CreateGame("Visible", "visible", GameStatus.Installed);
        settings.ManuallyHiddenApps.Add("folder:hidden-installed");
        settings.ManuallyHiddenApps.Add("folder:hidden-missing");
        settings.ListScope = AppListScope.HiddenOnly;

        manager.SetCatalogAppsAndFilter([hiddenInstalled, hiddenMissing, visible], settings);

        manager.Games.Select(g => g.FolderName).Should().BeEquivalentTo("hidden-installed", "hidden-missing");
        manager.Games.Should().OnlyContain(g => g.IsManuallyHidden);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void ToggleUserHide_unhide_removes_from_HiddenOnly_and_returns_under_AllApps()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        var game = CreateGame("Hidden", "hidden", GameStatus.Installed);
        var other = CreateGame("Visible", "visible", GameStatus.Installed);
        settings.ManuallyHiddenApps.Add("folder:hidden");
        settings.ListScope = AppListScope.HiddenOnly;

        manager.SetCatalogAppsAndFilter([game, other], settings);
        manager.Games.Should().ContainSingle(g => g.FolderName == "hidden");

        manager.ToggleUserHide(game);
        settings = store.Current;
        settings.ListScope = AppListScope.HiddenOnly;
        manager.SetCatalogAppsAndFilter([game, other], settings);
        manager.Games.Should().BeEmpty();

        settings.ListScope = AppListScope.AllApps;
        manager.SetCatalogAppsAndFilter([game, other], settings);
        manager.Games.Select(g => g.FolderName).Should().BeEquivalentTo("hidden", "visible");
        game.IsManuallyHidden.Should().BeFalse();

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void InstalledOnly_still_excludes_manually_hidden_apps()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        settings.ListScope = AppListScope.InstalledOnly;
        settings.ManuallyHiddenApps.Add("folder:hidden");

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("Hidden", "hidden", GameStatus.Installed),
            CreateGame("Visible", "visible", GameStatus.Installed),
            CreateGame("Missing", "missing", GameStatus.NotInstalled),
        ],
        settings);

        manager.Games.Should().ContainSingle(g => g.FolderName == "visible");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void GetLatestPlayedInstalledGame_is_not_affected_by_display_filters()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var appsFolder = Path.Combine(tempDir, "Apps");
        Directory.CreateDirectory(appsFolder);

        var store = new FileSettingsStore(Path.Combine(tempDir, "settings.json"));
        store.Current.AppsPath = appsFolder;
        store.Save(store.Current);

        var manager = new GameManager(store);
        var settings = store.Current;
        var continueGame = CreateGame("Continue Game", "continue-game", GameStatus.Installed, "n64");
        var continuePath = Path.Combine(appsFolder, "continue-game");
        Directory.CreateDirectory(continuePath);
        File.WriteAllText(Path.Combine(continuePath, "LastPlayed.txt"), "2025-06-01 12:00:00");

        settings.ListScope = AppListScope.InstalledOnly;
        var filter = new TagDisplayFilter { Name = "PC", Tags = ["pc"] };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        manager.SetCatalogAppsAndFilter(
        [
            continueGame,
            CreateGame("Other", "other", GameStatus.Installed, "pc"),
        ],
        settings);

        manager.Games.Should().NotContain(g => g.FolderName == "continue-game");
        manager.GetLatestPlayedInstalledGame()?.FolderName.Should().Be("continue-game");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Tag_filter_with_All_match_mode_requires_every_tag()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        var filter = new TagDisplayFilter
        {
            Name = "N64 Recomp",
            Tags = ["n64", "recomp"],
            MatchMode = TagFilterMatchMode.All,
        };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("Both", "both", GameStatus.Installed, "n64", "recomp"),
            CreateGame("N64 Only", "n64-only", GameStatus.Installed, "n64"),
            CreateGame("Recomp Only", "recomp-only", GameStatus.Installed, "recomp"),
        ],
        settings);

        manager.Games.Select(g => g.FolderName).Should().ContainSingle().Which.Should().Be("both");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Tag_filter_with_Any_match_mode_matches_either_tag()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        var filter = new TagDisplayFilter
        {
            Name = "N64 Recomp",
            Tags = ["n64", "recomp"],
            MatchMode = TagFilterMatchMode.Any,
        };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("Both", "both", GameStatus.Installed, "n64", "recomp"),
            CreateGame("N64 Only", "n64-only", GameStatus.Installed, "n64"),
            CreateGame("Recomp Only", "recomp-only", GameStatus.Installed, "recomp"),
            CreateGame("Other", "other", GameStatus.Installed, "pc"),
        ],
        settings);

        manager.Games.Select(g => g.FolderName)
            .Should()
            .BeEquivalentTo("both", "n64-only", "recomp-only");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Clearing_active_tag_filter_restores_scope_only_list()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        settings.ListScope = AppListScope.InstalledOnly;
        var filter = new TagDisplayFilter { Name = "N64", Tags = ["n64"] };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("N64 Installed", "n64-installed", GameStatus.Installed, "n64"),
            CreateGame("PC Installed", "pc-installed", GameStatus.Installed, "pc"),
            CreateGame("N64 Missing", "n64-missing", GameStatus.NotInstalled, "n64"),
        ],
        settings);

        manager.Games.Select(g => g.FolderName).Should().ContainSingle().Which.Should().Be("n64-installed");

        settings.ActiveTagDisplayFilterId = null;
        manager.ApplyTagDisplayFilter(settings);

        manager.Games.Select(g => g.FolderName)
            .Should()
            .BeEquivalentTo("n64-installed", "pc-installed");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Tag_filter_with_exclude_tags_hides_matching_apps()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        var filter = new TagDisplayFilter
        {
            Name = "N64 no AI",
            Tags = ["n64"],
            MatchMode = TagFilterMatchMode.Any,
            ExcludeTags = ["ai"],
            ExcludeMatchMode = TagFilterMatchMode.Any,
        };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("N64", "n64-plain", GameStatus.Installed, "n64"),
            CreateGame("N64 AI", "n64-ai", GameStatus.Installed, "n64", "ai"),
            CreateGame("PC", "pc", GameStatus.Installed, "pc"),
        ],
        settings);

        manager.Games.Select(g => g.FolderName).Should().ContainSingle().Which.Should().Be("n64-plain");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Library_search_applies_after_InstalledOnly_and_tag_filter()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;
        settings.ListScope = AppListScope.InstalledOnly;
        var filter = new TagDisplayFilter { Name = "N64", Tags = ["n64"] };
        settings.TagDisplayFilters.Add(filter);
        settings.ActiveTagDisplayFilterId = filter.Id;

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("Banjo Installed", "banjo-installed", GameStatus.Installed, "n64"),
            CreateGame("Banjo Missing", "banjo-missing", GameStatus.NotInstalled, "n64"),
            CreateGame("Zelda Installed", "zelda-installed", GameStatus.Installed, "n64"),
            CreateGame("Banjo PC", "banjo-pc", GameStatus.Installed, "pc"),
        ],
        settings);

        manager.Games.Select(g => g.FolderName)
            .Should()
            .BeEquivalentTo("banjo-installed", "zelda-installed");

        manager.LibrarySearchText = "banjo";
        manager.ApplyTagDisplayFilter(settings);

        manager.Games.Should().ContainSingle(g => g.FolderName == "banjo-installed");
        manager.Games.Should().NotContain(g => g.FolderName == "banjo-missing");
        manager.Games.Should().NotContain(g => g.FolderName == "banjo-pc");

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Library_search_with_no_matches_sets_HasNoLibrarySearchMatches()
    {
        var (manager, store, tempDir) = CreateManager();
        var settings = store.Current;

        manager.SetCatalogAppsAndFilter(
        [
            CreateGame("Banjo", "banjo", GameStatus.Installed, "n64"),
        ],
        settings);

        manager.IsLibraryEmpty.Should().BeFalse();
        manager.HasNoLibrarySearchMatches.Should().BeFalse();

        manager.LibrarySearchText = "zelda";
        manager.ApplyTagDisplayFilter(settings);

        manager.HasLibrarySearch.Should().BeTrue();
        manager.Games.Should().BeEmpty();
        manager.HasNoLibrarySearchMatches.Should().BeTrue();
        manager.IsLibraryEmpty.Should().BeFalse();

        manager.LibrarySearchText = "";
        manager.ApplyTagDisplayFilter(settings);

        manager.HasNoLibrarySearchMatches.Should().BeFalse();
        manager.Games.Should().ContainSingle(g => g.FolderName == "banjo");

        Directory.Delete(tempDir, true);
    }
}
