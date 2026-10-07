namespace QuiverLauncher.Services;

/// <summary>Where a move on a details page lands: a control in a row, the page body, or out of the page.</summary>
public readonly record struct DetailsNav(
    int Row,
    int Column,
    bool Body = false,
    bool LeaveTopBar = false,
    bool LeaveSidebar = false,
    bool ScrollDown = false,
    bool ScrollUp = false);

/// <summary>
/// Controller and keyboard movement on a details page: rows of controls from top to bottom (back, actions, tabs…),
/// then the page body, which scrolls. Left and Right move along a row; Up and Down move between rows.
/// </summary>
public static class DetailsGamepadLayout
{
    /// <param name="rowLengths">How many controls each row has, top to bottom; empty rows are left out.</param>
    /// <param name="canScrollUp">The body is scrolled down, so Up scrolls before it leaves the body.</param>
    public static DetailsNav Move(NavigationDirection direction, IReadOnlyList<int> rowLengths, int row, int column,
        bool bodyFocused, bool canScrollUp)
    {
        var last = rowLengths.Count - 1;
        if (bodyFocused || last < 0)
        {
            return direction switch
            {
                NavigationDirection.Down => new(row, column, Body: true, ScrollDown: true),
                NavigationDirection.Up when canScrollUp => new(row, column, Body: true, ScrollUp: true),
                NavigationDirection.Up when last >= 0 => new(last, 0),
                NavigationDirection.Up => new(0, 0, LeaveTopBar: true),
                NavigationDirection.Left => new(0, 0, LeaveSidebar: true),
                _ => new(row, column, Body: true),
            };
        }

        row = Math.Clamp(row, 0, last);
        column = Math.Clamp(column, 0, rowLengths[row] - 1);
        return direction switch
        {
            NavigationDirection.Left when column > 0 => new(row, column - 1),
            NavigationDirection.Left => new(0, 0, LeaveSidebar: true),
            NavigationDirection.Right => new(row, Math.Min(column + 1, rowLengths[row] - 1)),
            // Between rows the column carries over, so a grid of cards moves straight up and down.
            NavigationDirection.Down when row < last => new(row + 1, Math.Min(column, rowLengths[row + 1] - 1)),
            NavigationDirection.Down => new(row, column, Body: true),
            NavigationDirection.Up when row > 0 => new(row - 1, Math.Min(column, rowLengths[row - 1] - 1)),
            NavigationDirection.Up => new(0, 0, LeaveTopBar: true),
            _ => new(row, column),
        };
    }
}
