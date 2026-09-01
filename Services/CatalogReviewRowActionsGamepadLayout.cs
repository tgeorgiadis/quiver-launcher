namespace QuiverLauncher.Services;

/// <summary>
/// Gamepad motion for catalog-review card actions when they are stacked vertically
/// (mobile). Horizontal desktop strips keep Left/Right navigation in the view.
/// </summary>
public static class CatalogReviewRowActionsGamepadLayout
{
    public enum MoveKind
    {
        Ignore,
        Stay,
        WithinStrip,
        BackToRow,
        AdjacentRow,
    }

    public readonly record struct Move(MoveKind Kind, int NextIndex, NavigationDirection AdjacentDirection);

    /// <summary>
    /// Left returns to the card. Up/Down walk the stack and leave to the adjacent
    /// review row at either end. Right is a no-op (already in the action column).
    /// </summary>
    public static Move HandleStacked(NavigationDirection direction, int currentIndex, int count)
    {
        if (count <= 0)
            return new Move(MoveKind.Ignore, -1, direction);

        var index = currentIndex < 0 ? 0 : (currentIndex >= count ? count - 1 : currentIndex);

        if (direction == NavigationDirection.Left)
            return new Move(MoveKind.BackToRow, index, direction);

        if (direction == NavigationDirection.Right)
            return new Move(MoveKind.Stay, index, direction);

        if (direction == NavigationDirection.Up)
        {
            if (index <= 0)
                return new Move(MoveKind.AdjacentRow, index, NavigationDirection.Up);
            return new Move(MoveKind.WithinStrip, index - 1, direction);
        }

        if (direction == NavigationDirection.Down)
        {
            if (index >= count - 1)
                return new Move(MoveKind.AdjacentRow, index, NavigationDirection.Down);
            return new Move(MoveKind.WithinStrip, index + 1, direction);
        }

        return new Move(MoveKind.Ignore, index, direction);
    }
}
