using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class DetailsGamepadLayoutTests
{
    // Back; Add and Open repository; the three tabs.
    private static readonly int[] Rows = [1, 2, 3];

    private static DetailsNav Move(NavigationDirection direction, int row, int column, bool body = false, bool scrolled = false) =>
        DetailsGamepadLayout.Move(direction, Rows, row, column, body, scrolled);

    [Fact]
    public void Left_and_right_move_along_a_row_and_left_of_the_first_leaves_for_the_sidebar()
    {
        Move(NavigationDirection.Right, 1, 0).Should().Be(new DetailsNav(1, 1));
        Move(NavigationDirection.Right, 1, 1).Should().Be(new DetailsNav(1, 1));
        Move(NavigationDirection.Left, 2, 1).Should().Be(new DetailsNav(2, 0));
        Move(NavigationDirection.Left, 2, 0).LeaveSidebar.Should().BeTrue();
    }

    [Fact]
    public void Up_and_down_walk_the_rows_then_the_body_and_back()
    {
        Move(NavigationDirection.Down, 0, 0).Should().Be(new DetailsNav(1, 0));
        // The column carries over between rows, clamped to the shorter one.
        Move(NavigationDirection.Down, 1, 1).Should().Be(new DetailsNav(2, 1));
        Move(NavigationDirection.Up, 2, 2).Should().Be(new DetailsNav(1, 1));
        Move(NavigationDirection.Down, 2, 1).Body.Should().BeTrue();
        Move(NavigationDirection.Down, 2, 1, body: true).ScrollDown.Should().BeTrue();
        // Up scrolls the body back to the top before it returns to the last row.
        Move(NavigationDirection.Up, 2, 1, body: true, scrolled: true).ScrollUp.Should().BeTrue();
        Move(NavigationDirection.Up, 2, 1, body: true).Should().Be(new DetailsNav(2, 0));
        Move(NavigationDirection.Up, 1, 1).Should().Be(new DetailsNav(0, 0));
        Move(NavigationDirection.Up, 0, 0).LeaveTopBar.Should().BeTrue();
    }

    [Fact]
    public void A_page_with_nothing_to_select_only_scrolls()
    {
        DetailsGamepadLayout.Move(NavigationDirection.Down, [], 0, 0, bodyFocused: false, canScrollUp: false).ScrollDown.Should().BeTrue();
        DetailsGamepadLayout.Move(NavigationDirection.Up, [], 0, 0, bodyFocused: false, canScrollUp: false).LeaveTopBar.Should().BeTrue();
    }
}
