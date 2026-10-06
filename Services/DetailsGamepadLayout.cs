namespace QuiverLauncher.Services;

public enum DetailsRegion
{
    Header,
    Actions,
    Body,
}

public readonly record struct DetailsNav(
    DetailsRegion Region,
    int Index,
    bool LeaveTopBar = false,
    bool LeaveSidebar = false,
    bool ScrollDown = false,
    bool ScrollUp = false);

public static class DetailsGamepadLayout
{
    public static DetailsNav Move(
        NavigationDirection direction,
        int currentIndex,
        int headerCount,
        int totalCount,
        bool bodyFocused,
        bool canScrollUp)
    {
        if (totalCount <= 0)
        {
            return direction switch
            {
                NavigationDirection.Up when canScrollUp => Stay(DetailsRegion.Body, 0, scrollUp: true),
                NavigationDirection.Up => LeaveTopBar(),
                NavigationDirection.Down => Stay(DetailsRegion.Body, 0, scrollDown: true),
                NavigationDirection.Left => LeaveSidebar(),
                _ => Stay(DetailsRegion.Body, 0),
            };
        }

        var index = Math.Clamp(currentIndex, 0, totalCount - 1);
        var actionStart = headerCount;

        if (bodyFocused)
        {
            if (direction == NavigationDirection.Down)
                return Stay(DetailsRegion.Body, index, scrollDown: true);
            if (direction == NavigationDirection.Up)
            {
                if (canScrollUp)
                    return Stay(DetailsRegion.Body, index, scrollUp: true);

                var returnIndex = totalCount > headerCount
                    ? totalCount - 1
                    : index;
                var region = returnIndex >= actionStart && totalCount > headerCount
                    ? DetailsRegion.Actions
                    : DetailsRegion.Header;
                return Stay(region, returnIndex);
            }

            if (direction == NavigationDirection.Left)
                return LeaveSidebar();

            return Stay(DetailsRegion.Body, index);
        }

        var inHeader = headerCount > 0 && index < headerCount;

        if (direction == NavigationDirection.Left)
        {
            var rowStart = inHeader ? 0 : actionStart;
            if (index <= rowStart)
                return LeaveSidebar();
            return Stay(inHeader ? DetailsRegion.Header : DetailsRegion.Actions, index - 1);
        }

        if (direction == NavigationDirection.Right)
        {
            var rowEnd = inHeader ? headerCount - 1 : totalCount - 1;
            if (index >= rowEnd)
                return Stay(inHeader ? DetailsRegion.Header : DetailsRegion.Actions, index);
            return Stay(inHeader ? DetailsRegion.Header : DetailsRegion.Actions, index + 1);
        }

        if (direction == NavigationDirection.Down)
        {
            if (inHeader && totalCount > headerCount)
                return Stay(DetailsRegion.Actions, actionStart);
            return Stay(DetailsRegion.Body, index);
        }

        if (direction == NavigationDirection.Up)
        {
            if (!inHeader && headerCount > 0)
                return Stay(DetailsRegion.Header, headerCount - 1);
            return LeaveTopBar();
        }

        return Stay(inHeader ? DetailsRegion.Header : DetailsRegion.Actions, index);
    }

    private static DetailsNav Stay(
        DetailsRegion region,
        int index,
        bool scrollDown = false,
        bool scrollUp = false) =>
        new(region, index, ScrollDown: scrollDown, ScrollUp: scrollUp);

    private static DetailsNav LeaveTopBar() =>
        new(DetailsRegion.Header, 0, LeaveTopBar: true);

    private static DetailsNav LeaveSidebar() =>
        new(DetailsRegion.Header, 0, LeaveSidebar: true);
}
