using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReviewRowActionsGamepadLayoutTests
{
    [Fact]
    public void Left_from_any_action_returns_to_the_row()
    {
        var move = CatalogReviewRowActionsGamepadLayout.HandleStacked(
            NavigationDirection.Left, currentIndex: 2, count: 3);

        move.Kind.Should().Be(CatalogReviewRowActionsGamepadLayout.MoveKind.BackToRow);
    }

    [Fact]
    public void Right_stays_on_the_current_action()
    {
        var move = CatalogReviewRowActionsGamepadLayout.HandleStacked(
            NavigationDirection.Right, currentIndex: 1, count: 3);

        move.Kind.Should().Be(CatalogReviewRowActionsGamepadLayout.MoveKind.Stay);
        move.NextIndex.Should().Be(1);
    }

    [Fact]
    public void Up_and_Down_move_within_the_stack()
    {
        CatalogReviewRowActionsGamepadLayout.HandleStacked(
                NavigationDirection.Down, currentIndex: 0, count: 3)
            .Should().Be(new CatalogReviewRowActionsGamepadLayout.Move(
                CatalogReviewRowActionsGamepadLayout.MoveKind.WithinStrip,
                1,
                NavigationDirection.Down));

        CatalogReviewRowActionsGamepadLayout.HandleStacked(
                NavigationDirection.Up, currentIndex: 2, count: 3)
            .Should().Be(new CatalogReviewRowActionsGamepadLayout.Move(
                CatalogReviewRowActionsGamepadLayout.MoveKind.WithinStrip,
                1,
                NavigationDirection.Up));
    }

    [Fact]
    public void Up_from_first_and_Down_from_last_leave_to_the_adjacent_row()
    {
        CatalogReviewRowActionsGamepadLayout.HandleStacked(
                NavigationDirection.Up, currentIndex: 0, count: 3)
            .Kind.Should().Be(CatalogReviewRowActionsGamepadLayout.MoveKind.AdjacentRow);

        CatalogReviewRowActionsGamepadLayout.HandleStacked(
                NavigationDirection.Down, currentIndex: 2, count: 3)
            .Kind.Should().Be(CatalogReviewRowActionsGamepadLayout.MoveKind.AdjacentRow);
    }
}
