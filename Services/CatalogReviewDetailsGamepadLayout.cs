namespace QuiverLauncher.Services;

public enum CatalogReviewDetailsRegion
{
    Header,
    Actions,
    Body,
}

public readonly record struct CatalogReviewDetailsNav(
    CatalogReviewDetailsRegion Region,
    int Index,
    bool LeaveTopBar = false,
    bool LeaveSidebar = false,
    bool ScrollDown = false,
    bool ScrollUp = false);

public static class CatalogReviewDetailsGamepadLayout
{
    public static CatalogReviewDetailsNav Move(
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
                NavigationDirection.Up when canScrollUp => Stay(CatalogReviewDetailsRegion.Body, 0, scrollUp: true),
                NavigationDirection.Up => LeaveTopBar(),
                NavigationDirection.Down => Stay(CatalogReviewDetailsRegion.Body, 0, scrollDown: true),
                NavigationDirection.Left => LeaveSidebar(),
                _ => Stay(CatalogReviewDetailsRegion.Body, 0),
            };
        }

        var index = Math.Clamp(currentIndex, 0, totalCount - 1);
        var actionStart = headerCount;

        if (bodyFocused)
        {
            if (direction == NavigationDirection.Down)
                return Stay(CatalogReviewDetailsRegion.Body, index, scrollDown: true);
            if (direction == NavigationDirection.Up)
            {
                if (canScrollUp)
                    return Stay(CatalogReviewDetailsRegion.Body, index, scrollUp: true);

                var returnIndex = totalCount > headerCount
                    ? totalCount - 1
                    : index;
                var region = returnIndex >= actionStart && totalCount > headerCount
                    ? CatalogReviewDetailsRegion.Actions
                    : CatalogReviewDetailsRegion.Header;
                return Stay(region, returnIndex);
            }

            if (direction == NavigationDirection.Left)
                return LeaveSidebar();

            return Stay(CatalogReviewDetailsRegion.Body, index);
        }

        var inHeader = headerCount > 0 && index < headerCount;

        if (direction == NavigationDirection.Left)
        {
            var rowStart = inHeader ? 0 : actionStart;
            if (index <= rowStart)
                return LeaveSidebar();
            return Stay(inHeader ? CatalogReviewDetailsRegion.Header : CatalogReviewDetailsRegion.Actions, index - 1);
        }

        if (direction == NavigationDirection.Right)
        {
            var rowEnd = inHeader ? headerCount - 1 : totalCount - 1;
            if (index >= rowEnd)
                return Stay(inHeader ? CatalogReviewDetailsRegion.Header : CatalogReviewDetailsRegion.Actions, index);
            return Stay(inHeader ? CatalogReviewDetailsRegion.Header : CatalogReviewDetailsRegion.Actions, index + 1);
        }

        if (direction == NavigationDirection.Down)
        {
            if (inHeader && totalCount > headerCount)
                return Stay(CatalogReviewDetailsRegion.Actions, actionStart);
            return Stay(CatalogReviewDetailsRegion.Body, index);
        }

        if (direction == NavigationDirection.Up)
        {
            if (!inHeader && headerCount > 0)
                return Stay(CatalogReviewDetailsRegion.Header, headerCount - 1);
            return LeaveTopBar();
        }

        return Stay(inHeader ? CatalogReviewDetailsRegion.Header : CatalogReviewDetailsRegion.Actions, index);
    }

    private static CatalogReviewDetailsNav Stay(
        CatalogReviewDetailsRegion region,
        int index,
        bool scrollDown = false,
        bool scrollUp = false) =>
        new(region, index, ScrollDown: scrollDown, ScrollUp: scrollUp);

    private static CatalogReviewDetailsNav LeaveTopBar() =>
        new(CatalogReviewDetailsRegion.Header, 0, LeaveTopBar: true);

    private static CatalogReviewDetailsNav LeaveSidebar() =>
        new(CatalogReviewDetailsRegion.Header, 0, LeaveSidebar: true);
}
