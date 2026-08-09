using FluentAssertions;

using QuiverLauncher.Services;



namespace QuiverLauncher.Tests;



public class GamepadNavigationServiceTests

{

    private readonly GamepadNavigationService _service = new();



    [Fact]

    public void MoveListIndex_moves_down_and_wraps()

    {

        _service.MoveListIndex(0, NavigationDirection.Down, 3).Should().Be(1);

        _service.MoveListIndex(2, NavigationDirection.Down, 3).Should().Be(0);

    }



    [Fact]

    public void MoveListIndex_moves_up_and_wraps()

    {

        _service.MoveListIndex(0, NavigationDirection.Up, 3).Should().Be(2);

        _service.MoveListIndex(1, NavigationDirection.Up, 3).Should().Be(0);

    }



    [Fact]

    public void MoveListIndex_does_not_wrap_when_disabled()

    {

        _service.MoveListIndex(2, NavigationDirection.Down, 3, wrap: false).Should().Be(2);

        _service.MoveListIndex(0, NavigationDirection.Up, 3, wrap: false).Should().Be(0);

        _service.MoveListIndex(1, NavigationDirection.Down, 3, wrap: false).Should().Be(2);

        _service.MoveListIndex(1, NavigationDirection.Up, 3, wrap: false).Should().Be(0);

    }



    [Fact]

    public void MoveHorizontalIndex_moves_right_and_wraps()

    {

        _service.MoveHorizontalIndex(0, NavigationDirection.Right, 4).Should().Be(1);

        _service.MoveHorizontalIndex(3, NavigationDirection.Right, 4).Should().Be(0);

    }



    [Fact]

    public void MoveHorizontalIndex_moves_left_and_wraps()

    {

        _service.MoveHorizontalIndex(0, NavigationDirection.Left, 4).Should().Be(3);

        _service.MoveHorizontalIndex(2, NavigationDirection.Left, 4).Should().Be(1);

    }



    [Fact]

    public void MoveHorizontalIndex_clamps_stale_high_index_before_moving_left()

    {

        // Past-the-end index must move to count-2, not wrap as if from 0.
        _service.MoveHorizontalIndex(99, NavigationDirection.Left, 4).Should().Be(2);

    }



    [Fact]

    public void ClampIndex_clamps_when_collection_shrinks()

    {

        _service.ClampIndex(5, 3).Should().Be(2);

        _service.ClampIndex(-1, 3).Should().Be(0);

        _service.ClampIndex(1, 0).Should().Be(-1);

    }



    [Fact]

    public void SelectSpatialNextIndex_picks_nearest_card_in_direction()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.SelectSpatialNextIndex(0, NavigationDirection.Right, positions).Should().Be(1);

        _service.SelectSpatialNextIndex(0, NavigationDirection.Down, positions).Should().Be(2);

        _service.SelectSpatialNextIndex(3, NavigationDirection.Left, positions).Should().Be(2);

    }



    [Fact]

    public void SelectSpatialNextIndex_wraps_up_from_top_row_to_bottom_row_same_column()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.SelectSpatialNextIndex(0, NavigationDirection.Up, positions).Should().Be(2);

        _service.SelectSpatialNextIndex(1, NavigationDirection.Up, positions).Should().Be(3);

    }



    [Fact]

    public void SelectSpatialNextIndex_wraps_down_from_bottom_row_to_top_row_same_column()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.SelectSpatialNextIndex(2, NavigationDirection.Down, positions).Should().Be(0);

        _service.SelectSpatialNextIndex(3, NavigationDirection.Down, positions).Should().Be(1);

    }



    [Fact]

    public void TryGetZoneTransition_moves_right_from_sidebar_to_main_zone()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Right,

            GamepadNavigationZone.Sidebar,

            GamepadNavigationZone.Library,

            isListLayout: true,

            positions: null,

            currentIndex: -1);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Library);

        transition.Value.SelectedIndex.Should().Be(0);

    }



    [Fact]

    public void TryGetZoneTransition_moves_right_from_sidebar_to_catalog_review_list()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Right,

            GamepadNavigationZone.Sidebar,

            GamepadNavigationZone.CatalogReviewList,

            isListLayout: true,

            positions: null,

            currentIndex: 0);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.CatalogReviewList);

    }



    [Fact]

    public void TryGetZoneTransition_moves_left_from_list_layout_to_sidebar()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.Library,

            GamepadNavigationZone.Library,

            isListLayout: true,

            positions: null,

            currentIndex: 2);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Sidebar);

    }



    [Fact]

    public void TryGetZoneTransition_moves_left_from_leftmost_grid_column_to_sidebar()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

        };



        _service.IsLeftmostColumn(positions, 0).Should().BeTrue();

        _service.IsLeftmostColumn(positions, 1).Should().BeFalse();



        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.Library,

            GamepadNavigationZone.Library,

            isListLayout: false,

            positions,

            currentIndex: 0);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Sidebar);



        _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.Library,

            GamepadNavigationZone.Library,

            isListLayout: false,

            positions,

            currentIndex: 1)

            .Should().BeNull();

    }



    [Fact]

    public void TryGetZoneTransition_moves_up_from_top_library_row_to_top_bar()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Up,

            GamepadNavigationZone.Library,

            GamepadNavigationZone.Library,

            isListLayout: true,

            positions: null,

            currentIndex: 0,

            itemCount: 5);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.TopBar);

    }



    [Fact]

    public void TryGetZoneTransition_moves_down_from_top_bar_to_library()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Down,

            GamepadNavigationZone.TopBar,

            GamepadNavigationZone.Library,

            isListLayout: true,

            positions: null,

            currentIndex: 0);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Library);

        transition.Value.SelectedIndex.Should().Be(0);

    }



    [Fact]

    public void TryGetZoneTransition_does_not_leave_top_bar_on_up()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Up,

            GamepadNavigationZone.TopBar,

            GamepadNavigationZone.Library,

            isListLayout: true,

            positions: null,

            currentIndex: 3);



        transition.Should().BeNull();

    }



    [Fact]

    public void TryGetZoneTransition_moves_left_from_catalog_sources_to_sidebar()

    {

        var positions = new List<(double X, double Y)> { (0, 0), (100, 0) };



        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.CatalogSources,

            GamepadNavigationZone.CatalogSources,

            isListLayout: false,

            positions,

            currentIndex: 0);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Sidebar);

    }



    [Fact]

    public void TryGetZoneTransition_moves_left_from_catalog_review_list_to_sidebar()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.CatalogReviewList,

            GamepadNavigationZone.CatalogReviewList,

            isListLayout: true,

            positions: null,

            currentIndex: 2,

            itemCount: 5);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Sidebar);

    }



    [Fact]

    public void TryGetZoneTransition_moves_up_from_top_review_row_to_filters()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Up,

            GamepadNavigationZone.CatalogReviewList,

            GamepadNavigationZone.CatalogReviewList,

            isListLayout: true,

            positions: null,

            currentIndex: 0,

            itemCount: 5);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.CatalogReviewFilters);

    }



    [Fact]

    public void TryGetZoneTransition_moves_down_from_review_filters_to_list()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Down,

            GamepadNavigationZone.CatalogReviewFilters,

            GamepadNavigationZone.CatalogReviewList,

            isListLayout: true,

            positions: null,

            currentIndex: 0);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.CatalogReviewList);

        transition.Value.SelectedIndex.Should().Be(0);

    }



    [Fact]

    public void TryGetZoneTransition_moves_left_from_first_review_filter_to_sidebar()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.CatalogReviewFilters,

            GamepadNavigationZone.CatalogReviewList,

            isListLayout: true,

            positions: null,

            currentIndex: 0,

            itemCount: 7);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Sidebar);

    }



    [Fact]

    public void TryGetZoneTransition_moves_up_from_catalog_sources_top_edge_to_filters()

    {

        var positions = new List<(double X, double Y)> { (0, 0), (100, 0), (0, 100) };



        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Up,

            GamepadNavigationZone.CatalogSources,

            GamepadNavigationZone.CatalogSources,

            isListLayout: false,

            positions,

            currentIndex: 0,

            itemCount: 3);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.CatalogSourcesFilters);

    }



    [Fact]

    public void TryGetZoneTransition_moves_down_from_top_bar_to_catalog_sources_toolbar()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Down,

            GamepadNavigationZone.TopBar,

            GamepadNavigationZone.CatalogSources,

            isListLayout: true,

            positions: null,

            currentIndex: 0);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.CatalogSourcesToolbar);

        transition.Value.SelectedIndex.Should().Be(0);

    }



    [Fact]

    public void TryGetZoneTransition_moves_down_from_catalog_sources_toolbar_to_filters()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Down,

            GamepadNavigationZone.CatalogSourcesToolbar,

            GamepadNavigationZone.CatalogSources,

            isListLayout: true,

            positions: null,

            currentIndex: 0,

            itemCount: 2);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.CatalogSourcesFilters);

    }



    [Fact]

    public void TryGetZoneTransition_moves_down_from_catalog_sources_filters_to_cards()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Down,

            GamepadNavigationZone.CatalogSourcesFilters,

            GamepadNavigationZone.CatalogSources,

            isListLayout: true,

            positions: null,

            currentIndex: 0,

            itemCount: 3);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.CatalogSources);

        transition.Value.SelectedIndex.Should().Be(0);

    }



    [Fact]

    public void TryGetZoneTransition_does_not_exit_card_actions_on_left_at_enabled()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.CatalogSourceCardActions,

            GamepadNavigationZone.CatalogSources,

            isListLayout: true,

            positions: null,

            currentIndex: 0,

            itemCount: 3);



        transition.Should().BeNull();

    }



    [Fact]

    public void TryGetZoneTransition_moves_up_from_review_filters_to_top_bar()

    {

        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Up,

            GamepadNavigationZone.CatalogReviewFilters,

            GamepadNavigationZone.CatalogReviewList,

            isListLayout: true,

            positions: null,

            currentIndex: 2,

            itemCount: 7);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.TopBar);

    }



    [Fact]

    public void TryGetZoneTransition_moves_left_from_grid_without_left_neighbor_to_sidebar()

    {

        var positions = new List<(double X, double Y)>

        {

            (200, 0),

            (200, 100),

        };



        _service.IsLeftmostColumn(positions, 0).Should().BeTrue();

        _service.HasSpatialNeighbor(positions, 0, NavigationDirection.Left).Should().BeFalse();

        _service.IsAtContentEdge(NavigationDirection.Left, isListLayout: false, positions, 0, 2).Should().BeTrue();



        var transition = _service.TryGetZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.Library,

            GamepadNavigationZone.Library,

            isListLayout: false,

            positions,

            currentIndex: 0,

            itemCount: 2);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Sidebar);

    }



    [Fact]

    public void MoveLibraryIndex_does_not_wrap_up_from_top_row()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.MoveLibraryIndex(0, NavigationDirection.Up, 4, isListLayout: false, positions).Should().Be(0);

        _service.MoveLibraryIndex(1, NavigationDirection.Up, 4, isListLayout: false, positions).Should().Be(1);

    }



    [Fact]

    public void MoveLibraryIndex_does_not_wrap_left_from_left_edge()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.MoveLibraryIndex(0, NavigationDirection.Left, 4, isListLayout: false, positions).Should().Be(0);

        _service.MoveLibraryIndex(2, NavigationDirection.Left, 4, isListLayout: false, positions).Should().Be(2);

    }



    [Fact]

    public void MoveLibraryIndex_does_not_wrap_down_from_bottom_row()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.MoveLibraryIndex(2, NavigationDirection.Down, 4, isListLayout: false, positions).Should().Be(2);

        _service.MoveLibraryIndex(3, NavigationDirection.Down, 4, isListLayout: false, positions).Should().Be(3);

    }



    [Fact]

    public void MoveLibraryIndex_does_not_wrap_right_from_right_edge()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.MoveLibraryIndex(1, NavigationDirection.Right, 4, isListLayout: false, positions).Should().Be(1);

        _service.MoveLibraryIndex(3, NavigationDirection.Right, 4, isListLayout: false, positions).Should().Be(3);

    }



    [Fact]

    public void MoveCatalogIndex_does_not_wrap_up_or_left_at_content_edges()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

        };



        _service.MoveCatalogIndex(0, NavigationDirection.Up, 3, positions).Should().Be(0);

        _service.MoveCatalogIndex(0, NavigationDirection.Left, 3, positions).Should().Be(0);

    }



    [Fact]

    public void MoveCatalogIndex_does_not_wrap_down_or_right_at_content_edges()

    {

        var positions = new List<(double X, double Y)>

        {

            (0, 0),

            (100, 0),

            (0, 100),

            (100, 100),

        };



        _service.MoveCatalogIndex(2, NavigationDirection.Down, 4, positions).Should().Be(2);

        _service.MoveCatalogIndex(3, NavigationDirection.Down, 4, positions).Should().Be(3);

        _service.MoveCatalogIndex(1, NavigationDirection.Right, 4, positions).Should().Be(1);

        _service.MoveCatalogIndex(3, NavigationDirection.Right, 4, positions).Should().Be(3);

    }



    [Fact]

    public void TryGetBlockedMoveZoneTransition_escapes_library_grid_at_left_edge()

    {

        var transition = _service.TryGetBlockedMoveZoneTransition(

            NavigationDirection.Left,

            GamepadNavigationZone.Library,

            GamepadNavigationZone.Library,

            isListLayout: false,

            currentIndex: 0,

            itemCount: 4);



        transition.Should().NotBeNull();

        transition!.Value.Zone.Should().Be(GamepadNavigationZone.Sidebar);

    }

}


