using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class DetailsGamepadLayoutTests
{
    private const int HeaderCount = 2;
    private const int TotalCount = 7; // Open Repo, Close, then 5 actions

    [Fact]
    public void Left_and_right_move_within_the_action_row()
    {
        var right = DetailsGamepadLayout.Move(
            NavigationDirection.Right, currentIndex: 2, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false);
        right.Index.Should().Be(3);
        right.Region.Should().Be(DetailsRegion.Actions);

        var left = DetailsGamepadLayout.Move(
            NavigationDirection.Left, currentIndex: 3, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false);
        left.Index.Should().Be(2);
        left.Region.Should().Be(DetailsRegion.Actions);
    }

    [Fact]
    public void Up_and_down_do_not_walk_the_action_row()
    {
        var down = DetailsGamepadLayout.Move(
            NavigationDirection.Down, currentIndex: 2, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false);
        down.Region.Should().Be(DetailsRegion.Body);
        down.Index.Should().Be(2);

        var up = DetailsGamepadLayout.Move(
            NavigationDirection.Up, currentIndex: 3, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false);
        up.Region.Should().Be(DetailsRegion.Header);
        up.Index.Should().Be(1);
    }

    [Fact]
    public void Down_from_header_enters_first_action()
    {
        var nav = DetailsGamepadLayout.Move(
            NavigationDirection.Down, currentIndex: 0, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false);
        nav.Region.Should().Be(DetailsRegion.Actions);
        nav.Index.Should().Be(2);
    }

    [Fact]
    public void Up_from_header_leaves_to_top_bar()
    {
        DetailsGamepadLayout.Move(
            NavigationDirection.Up, currentIndex: 0, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false).LeaveTopBar.Should().BeTrue();
    }

    [Fact]
    public void Left_from_first_of_a_row_leaves_to_sidebar()
    {
        DetailsGamepadLayout.Move(
            NavigationDirection.Left, currentIndex: 0, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false).LeaveSidebar.Should().BeTrue();
        DetailsGamepadLayout.Move(
            NavigationDirection.Left, currentIndex: 2, HeaderCount, TotalCount,
            bodyFocused: false, canScrollUp: false).LeaveSidebar.Should().BeTrue();
    }

    [Fact]
    public void Body_down_scrolls_and_up_scrolls_until_top()
    {
        var down = DetailsGamepadLayout.Move(
            NavigationDirection.Down, currentIndex: 6, HeaderCount, TotalCount,
            bodyFocused: true, canScrollUp: true);
        down.ScrollDown.Should().BeTrue();
        down.Region.Should().Be(DetailsRegion.Body);

        var up = DetailsGamepadLayout.Move(
            NavigationDirection.Up, currentIndex: 6, HeaderCount, TotalCount,
            bodyFocused: true, canScrollUp: true);
        up.ScrollUp.Should().BeTrue();
        up.Region.Should().Be(DetailsRegion.Body);
    }

    [Fact]
    public void Body_up_at_top_returns_to_actions()
    {
        var nav = DetailsGamepadLayout.Move(
            NavigationDirection.Up, currentIndex: 6, HeaderCount, TotalCount,
            bodyFocused: true, canScrollUp: false);
        nav.Region.Should().Be(DetailsRegion.Actions);
        nav.Index.Should().Be(6);
        nav.ScrollUp.Should().BeFalse();
    }
}
