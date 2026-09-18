using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class ShellNavigationRouterTests
{
    private sealed class Handler : IFeatureNavigationHandler
    {
        public int Moves, Confirms, Cancels;
        public List<bool> Restores { get; } = [];
        public bool Navigate(NavigationDirection direction) { Moves++; return true; }
        public bool Confirm() { Confirms++; return true; }
        public bool Cancel() { Cancels++; return true; }
        public bool Options() => false;
        public void RestoreFocus() => RestoreFocus(true);
        public void RestoreFocus(bool bringIntoView) => Restores.Add(bringIntoView);
    }
    [Theory]
    [InlineData(GamepadNavigationZone.Library)]
    [InlineData(GamepadNavigationZone.CatalogSources)]
    [InlineData(GamepadNavigationZone.CatalogSourceCardActions)]
    [InlineData(GamepadNavigationZone.CatalogSourcesToolbar)]
    [InlineData(GamepadNavigationZone.CatalogSourcesFilters)]
    [InlineData(GamepadNavigationZone.CatalogReviewList)]
    [InlineData(GamepadNavigationZone.CatalogReviewRowActions)]
    [InlineData(GamepadNavigationZone.CatalogReviewFilters)]
    public void Passive_restoration_uses_current_feature_without_reentering_view(GamepadNavigationZone zone)
    {
        var sources = zone is GamepadNavigationZone.CatalogSources or GamepadNavigationZone.CatalogSourceCardActions
            or GamepadNavigationZone.CatalogSourcesToolbar or GamepadNavigationZone.CatalogSourcesFilters;
        var shell = new ShellViewModel
        {
            Mode = zone == GamepadNavigationZone.Library ? MainViewMode.Library : MainViewMode.AppCatalog,
            CatalogSubView = sources ? AppCatalogSubView.Sources : AppCatalogSubView.Review,
        };
        var navigation = new GamepadNavigationService { ActiveZone = zone };
        var handler = new Handler();
        var handlers = Enum.GetValues<GamepadNavigationZone>().ToDictionary(z => z, _ => (Func<IFeatureNavigationHandler>)(() => handler));
        var reentries = 0;
        var router = new ShellNavigationRouter(shell, navigation, handlers, () => false, () => { }, () => reentries++);
        router.RestoreCurrentFocus(bringIntoView: false);
        handler.Restores.Should().Equal(false);
        navigation.ActiveZone.Should().Be(zone);
        reentries.Should().Be(0);
        router.RestoreCurrentFocus();
        reentries.Should().Be(1);
    }

    [Theory]
    [InlineData("search", GamepadNavigationZone.TopBar)]
    [InlineData("sidebar", GamepadNavigationZone.Sidebar)]
    [InlineData("settings", GamepadNavigationZone.Settings)]
    [InlineData("editor", GamepadNavigationZone.EntryFormOverlay)]
    [InlineData("tags", GamepadNavigationZone.TagEditOverlay)]
    [InlineData("document", GamepadNavigationZone.ChangelogOverlay)]
    [InlineData("filter", GamepadNavigationZone.DisplayFilterOverlay)]
    [InlineData("details", GamepadNavigationZone.CatalogReviewDetailsOverlay)]
    public void Passive_restoration_does_not_steal_chrome_or_overlay_focus(string state, GamepadNavigationZone expected)
    {
        var shell = new ShellViewModel
        {
            SettingsOpen = state == "settings", EntryEditorOpen = state == "editor", TagEditorOpen = state == "tags",
            DocumentOpen = state == "document", CatalogDetailsOpen = state == "details",
        };
        var navigation = new GamepadNavigationService
        {
            ActiveZone = state is "search" or "sidebar" or "details" ? expected : GamepadNavigationZone.Library,
        };
        var features = Enum.GetValues<GamepadNavigationZone>().ToDictionary(z => z, _ => new Handler());
        var handlers = features.ToDictionary(p => p.Key, p => (Func<IFeatureNavigationHandler>)(() => p.Value));
        var router = new ShellNavigationRouter(shell, navigation, handlers, () => state == "filter", () => { },
            () => throw new Exception("Unexpected main-view restoration"));
        router.RestoreCurrentFocus(bringIntoView: false);
        features[expected].Restores.Should().ContainSingle();
        features[GamepadNavigationZone.Library].Restores.Should().BeEmpty();
    }
    [Fact]
    public void Nested_forms_own_directional_input_while_details_allow_chrome_transitions()
    {
        var shell = new ShellViewModel { SettingsOpen = true, EntryEditorOpen = true };
        var navigation = new GamepadNavigationService { ActiveZone = GamepadNavigationZone.Library };
        var features = Enum.GetValues<GamepadNavigationZone>().ToDictionary(z => z, _ => new Handler());
        var handlers = features.ToDictionary(p => p.Key, p => (Func<IFeatureNavigationHandler>)(() => p.Value));
        var filterOpen = true;
        var router = new ShellNavigationRouter(shell, navigation, handlers, () => filterOpen, () => { }, () => { });
        router.Navigate(NavigationDirection.Down);
        features[GamepadNavigationZone.DisplayFilterOverlay].Moves.Should().Be(1);
        filterOpen = false;
        router.Navigate(NavigationDirection.Down);
        features[GamepadNavigationZone.EntryFormOverlay].Moves.Should().Be(1);
        shell.EntryEditorOpen = shell.SettingsOpen = false;
        shell.CatalogDetailsOpen = true;
        navigation.ActiveZone = GamepadNavigationZone.TopBar;
        router.Navigate(NavigationDirection.Right);
        features[GamepadNavigationZone.TopBar].Moves.Should().Be(1);
        navigation.ActiveZone = GamepadNavigationZone.CatalogReviewList;
        router.Navigate(NavigationDirection.Down);
        navigation.ActiveZone.Should().Be(GamepadNavigationZone.CatalogReviewDetailsOverlay);
        features[GamepadNavigationZone.CatalogReviewDetailsOverlay].Moves.Should().Be(1);
    }
    [Fact]
    public void Confirmation_uses_open_settings_even_after_an_unrelated_zone_change()
    {
        var shell = new ShellViewModel { SettingsOpen = true };
        var navigation = new GamepadNavigationService { ActiveZone = GamepadNavigationZone.Library };
        var settings = new Handler();
        var library = new Handler();
        var router = new ShellNavigationRouter(shell, navigation, new Dictionary<GamepadNavigationZone, Func<IFeatureNavigationHandler>>
        {
            [GamepadNavigationZone.Settings] = () => settings,
            [GamepadNavigationZone.Library] = () => library,
        }, () => false, () => { }, () => { });
        router.ConfirmFeature(false).Should().BeTrue();
        settings.Confirms.Should().Be(1);
        library.Confirms.Should().Be(0);
        navigation.ActiveZone.Should().Be(GamepadNavigationZone.Settings);
    }
    [Fact]
    public void Cancel_returns_chrome_to_active_feature_and_routes_nested_actions_locally()
    {
        var shell = new ShellViewModel { AppUpdatesOpen = true };
        var navigation = new GamepadNavigationService { ActiveZone = GamepadNavigationZone.TopBar };
        var updates = new Handler();
        var clears = 0;
        var restores = 0;
        var router = new ShellNavigationRouter(shell, navigation, new Dictionary<GamepadNavigationZone, Func<IFeatureNavigationHandler>>
        {
            [GamepadNavigationZone.AppUpdatesReviewList] = () => updates,
            [GamepadNavigationZone.AppUpdatesReviewRowActions] = () => updates,
        }, () => false, () => clears++, () => restores++);
        router.CancelFeature(true).Should().BeTrue();
        navigation.ActiveZone.Should().Be(GamepadNavigationZone.AppUpdatesReviewList);
        clears.Should().Be(1); restores.Should().Be(1);
        navigation.ActiveZone = GamepadNavigationZone.AppUpdatesReviewRowActions;
        router.CancelFeature(true).Should().BeTrue();
        updates.Cancels.Should().Be(1);
    }
}
