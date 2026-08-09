namespace QuiverLauncher.Services;



public enum GamepadNavigationZone

{

    Sidebar,

    TopBar,

    AnnouncementBanner,

    Library,

    CatalogSources,

    CatalogSourcesToolbar,

    CatalogSourcesFilters,

    CatalogSourceCardActions,

    CatalogReviewFilters,

    CatalogReviewList,

    CatalogReviewRowActions,

    AppUpdatesReviewToolbar,

    AppUpdatesReviewList,

    AppUpdatesReviewRowActions,

    ModsOverlayToolbar,

    ModsOverlayFilters,

    ModsOverlayList,

    ModsOverlayRowActions,

    ModsOverlaySourceFilters,

    ModsDetailsOverlay,

    DisplayFilterOverlay,

    EntryFormOverlay,

    TagEditOverlay,

    ChangelogOverlay,

    Settings,

}



public readonly record struct GamepadZoneTransition(GamepadNavigationZone Zone, int? SelectedIndex);



public sealed class GamepadNavigationService

{

    public GamepadNavigationZone ActiveZone { get; set; } = GamepadNavigationZone.Library;



    public int SidebarSelectedIndex { get; set; } = -1;



    public int TopBarSelectedIndex { get; set; } = -1;



    public int LibrarySelectedIndex { get; set; } = -1;



    public int CatalogSelectedIndex { get; set; } = -1;



    public int CatalogSourcesToolbarSelectedIndex { get; set; } = -1;



    public int CatalogSourcesFilterIndex { get; set; } = -1;



    public int CatalogSourceCardActionIndex { get; set; } = -1;



    public int CatalogReviewFilterIndex { get; set; } = -1;



    public int CatalogReviewSelectedIndex { get; set; } = -1;



    public int CatalogReviewRowActionIndex { get; set; } = -1;



    public int AppUpdatesReviewToolbarIndex { get; set; } = -1;



    public int AppUpdatesReviewSelectedIndex { get; set; } = -1;



    public int AppUpdatesReviewRowActionIndex { get; set; } = -1;



    public int ClampIndex(int index, int count)

    {

        if (count <= 0)

            return -1;



        if (index < 0)

            return 0;



        if (index >= count)

            return count - 1;



        return index;

    }



    public int MoveListIndex(int currentIndex, NavigationDirection direction, int count, bool wrap = true)

    {

        if (count <= 0)

            return -1;



        var index = currentIndex < 0 ? 0 : currentIndex;



        return direction switch

        {

            NavigationDirection.Up => index <= 0

                ? (wrap ? count - 1 : index)

                : index - 1,

            NavigationDirection.Down => index >= count - 1

                ? (wrap ? 0 : index)

                : index + 1,

            NavigationDirection.Left => index,

            NavigationDirection.Right => index,

            _ => index,

        };

    }



    public int MoveHorizontalIndex(int currentIndex, NavigationDirection direction, int count)

    {

        if (count <= 0)

            return -1;



        // Clamp both ends so a stale -1 / past-the-end index cannot snap Left to the last item.
        var index = currentIndex < 0 ? 0 : (currentIndex >= count ? count - 1 : currentIndex);



        return direction switch

        {

            NavigationDirection.Left => index <= 0 ? count - 1 : index - 1,

            NavigationDirection.Right => index >= count - 1 ? 0 : index + 1,

            _ => index,

        };

    }



    public bool IsLeftmostColumn(IReadOnlyList<(double X, double Y)> positions, int index, double rowTolerance = 24)

    {

        if (positions.Count == 0 || index < 0 || index >= positions.Count)

            return true;



        var currentY = positions[index].Y;

        var minX = positions

            .Where(position => Math.Abs(position.Y - currentY) <= rowTolerance)

            .Min(position => position.X);



        return positions[index].X <= minX + 1;

    }



    public bool IsTopRow(IReadOnlyList<(double X, double Y)> positions, int index, double rowTolerance = 24)

    {

        if (positions.Count == 0 || index < 0 || index >= positions.Count)

            return false;



        var minY = positions.Min(position => position.Y);

        return positions[index].Y <= minY + rowTolerance;

    }



    public bool IsBottomRow(IReadOnlyList<(double X, double Y)> positions, int index, double rowTolerance = 24)

    {

        if (positions.Count == 0 || index < 0 || index >= positions.Count)

            return false;



        var maxY = positions.Max(position => position.Y);

        return positions[index].Y >= maxY - rowTolerance;

    }



    public bool HasSpatialNeighbor(

        IReadOnlyList<(double X, double Y)> positions,

        int index,

        NavigationDirection direction,

        double rowTolerance = 24)

    {

        if (positions.Count == 0 || index < 0 || index >= positions.Count)

            return false;



        var current = positions[index];

        for (var i = 0; i < positions.Count; i++)

        {

            if (i == index)

                continue;



            if (CalculateNavigationScore(current, positions[i], direction).HasValue)

                return true;

        }



        return false;

    }



    public bool IsAtContentEdge(

        NavigationDirection direction,

        bool isListLayout,

        IReadOnlyList<(double X, double Y)>? positions,

        int currentIndex,

        int itemCount,

        double rowTolerance = 24)

    {

        if (direction == NavigationDirection.Left)

        {

            if (isListLayout || positions == null || positions.Count == 0)

                return true;



            if (currentIndex < 0)

                return true;



            return IsLeftmostColumn(positions, currentIndex, rowTolerance) ||

                   !HasSpatialNeighbor(positions, currentIndex, NavigationDirection.Left, rowTolerance);

        }



        if (direction == NavigationDirection.Up)

        {

            if (isListLayout)

                return itemCount == 0 || currentIndex <= 0;



            if (positions == null || positions.Count == 0)

                return currentIndex <= 0;



            if (currentIndex < 0)

                return true;



            return IsTopRow(positions, currentIndex, rowTolerance) ||

                   !HasSpatialNeighbor(positions, currentIndex, NavigationDirection.Up, rowTolerance);

        }



        return false;

    }



    public int WrapToRow(

        IReadOnlyList<(double X, double Y)> positions,

        int index,

        double targetRowY,

        double rowTolerance = 24)

    {

        if (positions.Count == 0 || index < 0 || index >= positions.Count)

            return index;



        var currentX = positions[index].X;

        var bestIndex = index;

        var bestScore = double.MaxValue;



        for (var i = 0; i < positions.Count; i++)

        {

            if (Math.Abs(positions[i].Y - targetRowY) > rowTolerance)

                continue;



            var score = Math.Abs(positions[i].X - currentX);

            if (score < bestScore)

            {

                bestScore = score;

                bestIndex = i;

            }

        }



        return bestIndex;

    }



    public int? SelectSpatialNextIndex(

        int currentIndex,

        NavigationDirection direction,

        IReadOnlyList<(double X, double Y)> positions,

        double rowTolerance = 24,

        bool allowVerticalWrap = true,

        bool allowHorizontalWrap = true)

    {

        if (positions.Count == 0)

            return null;



        if (currentIndex < 0 || currentIndex >= positions.Count)

            return 0;



        if (direction == NavigationDirection.Up && IsTopRow(positions, currentIndex, rowTolerance))

        {

            if (!allowVerticalWrap)

                return currentIndex;



            var maxY = positions.Max(position => position.Y);

            return WrapToRow(positions, currentIndex, maxY, rowTolerance);

        }



        if (direction == NavigationDirection.Down && IsBottomRow(positions, currentIndex, rowTolerance))

        {

            if (!allowVerticalWrap)

                return currentIndex;



            var minY = positions.Min(position => position.Y);

            return WrapToRow(positions, currentIndex, minY, rowTolerance);

        }



        var current = positions[currentIndex];

        int? bestIndex = null;

        var bestScore = double.MaxValue;



        for (var i = 0; i < positions.Count; i++)

        {

            if (i == currentIndex)

                continue;



            var score = CalculateNavigationScore(current, positions[i], direction);

            if (!score.HasValue || score.Value >= bestScore)

                continue;



            bestScore = score.Value;

            bestIndex = i;

        }



        if (bestIndex.HasValue)

            return bestIndex;



        if (direction == NavigationDirection.Left && !allowHorizontalWrap)

            return currentIndex;



        if (direction == NavigationDirection.Right && !allowHorizontalWrap)

            return currentIndex;



        if (direction == NavigationDirection.Up && !allowVerticalWrap)

            return currentIndex;



        if (direction == NavigationDirection.Down && !allowVerticalWrap)

            return currentIndex;



        return SelectWrapIndex(currentIndex, direction, positions);

    }



    public GamepadZoneTransition? TryGetZoneTransition(

        NavigationDirection direction,

        GamepadNavigationZone zone,

        GamepadNavigationZone mainContentZone,

        bool isListLayout,

        IReadOnlyList<(double X, double Y)>? positions,

        int currentIndex,

        int itemCount = 0)

    {

        if (direction == NavigationDirection.Right && zone == GamepadNavigationZone.Sidebar)

            return new GamepadZoneTransition(mainContentZone, 0);



        if (direction == NavigationDirection.Down && zone == GamepadNavigationZone.TopBar)

        {

            if (mainContentZone is GamepadNavigationZone.CatalogReviewList or GamepadNavigationZone.CatalogReviewFilters)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogReviewList, 0);



            if (mainContentZone is GamepadNavigationZone.AppUpdatesReviewList
                or GamepadNavigationZone.AppUpdatesReviewToolbar)

                return new GamepadZoneTransition(GamepadNavigationZone.AppUpdatesReviewToolbar, 0);



            if (mainContentZone is GamepadNavigationZone.ModsOverlayList
                or GamepadNavigationZone.ModsOverlayToolbar
                or GamepadNavigationZone.ModsOverlayFilters
                or GamepadNavigationZone.ModsOverlaySourceFilters
                or GamepadNavigationZone.ModsOverlayRowActions)

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayToolbar, 0);



            if (mainContentZone == GamepadNavigationZone.CatalogSources)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogSourcesToolbar, 0);



            return new GamepadZoneTransition(GamepadNavigationZone.Library, 0);

        }



        if (zone == GamepadNavigationZone.AnnouncementBanner)

        {

            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (direction == NavigationDirection.Down)

            {

                if (mainContentZone is GamepadNavigationZone.CatalogReviewList or GamepadNavigationZone.CatalogReviewFilters)

                    return new GamepadZoneTransition(GamepadNavigationZone.CatalogReviewList, 0);



                if (mainContentZone is GamepadNavigationZone.AppUpdatesReviewList
                    or GamepadNavigationZone.AppUpdatesReviewToolbar)

                    return new GamepadZoneTransition(GamepadNavigationZone.AppUpdatesReviewToolbar, 0);



                if (mainContentZone is GamepadNavigationZone.ModsOverlayList
                    or GamepadNavigationZone.ModsOverlayToolbar
                    or GamepadNavigationZone.ModsOverlayFilters
                    or GamepadNavigationZone.ModsOverlaySourceFilters
                    or GamepadNavigationZone.ModsOverlayRowActions)

                    return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayToolbar, 0);



                if (mainContentZone == GamepadNavigationZone.CatalogSources)

                    return new GamepadZoneTransition(GamepadNavigationZone.CatalogSourcesToolbar, 0);



                return new GamepadZoneTransition(GamepadNavigationZone.Library, 0);

            }



            if (direction == NavigationDirection.Left)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.CatalogSourcesToolbar)

        {

            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (direction == NavigationDirection.Down)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogSourcesFilters, null);



            if (direction == NavigationDirection.Left && currentIndex <= 0)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.CatalogSourcesFilters)

        {

            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogSourcesToolbar, null);



            if (direction == NavigationDirection.Down && itemCount > 0)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogSources, 0);



            if (direction == NavigationDirection.Left && currentIndex <= 0)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.Library)

        {

            if (direction == NavigationDirection.Left &&

                IsAtContentEdge(direction, isListLayout, positions, currentIndex, itemCount))

            {

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

            }



            if (direction == NavigationDirection.Up &&

                IsAtContentEdge(direction, isListLayout, positions, currentIndex, itemCount))

            {

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);

            }

        }



        if (zone == GamepadNavigationZone.CatalogSources)

        {

            if (direction == NavigationDirection.Left &&

                IsAtContentEdge(direction, isListLayout: false, positions, currentIndex, itemCount))

            {

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

            }



            if (direction == NavigationDirection.Up &&

                IsAtContentEdge(direction, isListLayout: false, positions, currentIndex, itemCount))

            {

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogSourcesFilters, null);

            }

        }



        if (zone == GamepadNavigationZone.CatalogReviewList)

        {

            if (direction == NavigationDirection.Left)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);



            if (direction == NavigationDirection.Up && (itemCount == 0 || currentIndex <= 0))

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogReviewFilters, null);

        }



        if (zone == GamepadNavigationZone.CatalogReviewFilters)

        {

            if (direction == NavigationDirection.Down)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogReviewList, 0);



            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (direction == NavigationDirection.Left && currentIndex <= 0)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.AppUpdatesReviewToolbar)

        {

            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (direction == NavigationDirection.Down)

                return new GamepadZoneTransition(GamepadNavigationZone.AppUpdatesReviewList, 0);



            if (direction == NavigationDirection.Left && currentIndex <= 0)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.AppUpdatesReviewList)

        {

            if (direction == NavigationDirection.Left)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);



            if (direction == NavigationDirection.Up && (itemCount == 0 || currentIndex <= 0))

                return new GamepadZoneTransition(GamepadNavigationZone.AppUpdatesReviewToolbar, null);

        }



        if (zone == GamepadNavigationZone.ModsOverlayToolbar)

        {

            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (direction == NavigationDirection.Down)

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayFilters, 0);



            if (direction == NavigationDirection.Left && currentIndex <= 0)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.ModsOverlayFilters)

        {

            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayToolbar, null);



            if (direction == NavigationDirection.Down)

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlaySourceFilters, 0);



            if (direction == NavigationDirection.Left && currentIndex <= 0)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.ModsOverlaySourceFilters)

        {

            if (direction == NavigationDirection.Up)

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayFilters, null);



            if (direction == NavigationDirection.Down)

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayList, 0);



            if (direction == NavigationDirection.Left && currentIndex <= 0)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (zone == GamepadNavigationZone.ModsOverlayList)

        {

            if (direction == NavigationDirection.Left &&

                IsAtContentEdge(direction, isListLayout, positions, currentIndex, itemCount))

            {

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

            }



            if (direction == NavigationDirection.Up &&

                (itemCount == 0 || IsAtContentEdge(direction, isListLayout, positions, currentIndex, itemCount)))

            {

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlaySourceFilters, null);

            }

        }



        return null;

    }



    public GamepadZoneTransition? TryGetBlockedMoveZoneTransition(

        NavigationDirection direction,

        GamepadNavigationZone zone,

        GamepadNavigationZone mainContentZone,

        bool isListLayout,

        int currentIndex,

        int itemCount)

    {

        if (direction == NavigationDirection.Left)

        {

            if (zone == GamepadNavigationZone.Library && isListLayout)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);



            if (zone is GamepadNavigationZone.Library or GamepadNavigationZone.CatalogSources or GamepadNavigationZone.CatalogReviewList
                or GamepadNavigationZone.AppUpdatesReviewList
                or GamepadNavigationZone.ModsOverlayList)

                return new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null);

        }



        if (direction == NavigationDirection.Up)

        {

            if (zone == GamepadNavigationZone.Library)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (zone == GamepadNavigationZone.CatalogSources)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogSourcesFilters, null);



            if (zone == GamepadNavigationZone.CatalogSourcesToolbar)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (zone == GamepadNavigationZone.CatalogSourcesFilters)

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogSourcesToolbar, null);



            if (zone == GamepadNavigationZone.CatalogReviewFilters)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (zone == GamepadNavigationZone.AppUpdatesReviewToolbar)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (zone == GamepadNavigationZone.AnnouncementBanner)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (zone == GamepadNavigationZone.AppUpdatesReviewList && (itemCount == 0 || currentIndex <= 0))

                return new GamepadZoneTransition(GamepadNavigationZone.AppUpdatesReviewToolbar, null);



            if (zone == GamepadNavigationZone.ModsOverlayToolbar)

                return new GamepadZoneTransition(GamepadNavigationZone.TopBar, null);



            if (zone == GamepadNavigationZone.ModsOverlayList)

                return new GamepadZoneTransition(GamepadNavigationZone.ModsOverlaySourceFilters, null);



            if (zone == GamepadNavigationZone.CatalogReviewList && (itemCount == 0 || currentIndex <= 0))

                return new GamepadZoneTransition(GamepadNavigationZone.CatalogReviewFilters, null);

        }



        return null;

    }



    public int MoveLibraryIndex(

        int currentIndex,

        NavigationDirection direction,

        int count,

        bool isListLayout,

        IReadOnlyList<(double X, double Y)>? positions = null)

    {

        if (count <= 0)

            return -1;



        if (isListLayout || positions == null || positions.Count != count)

            return MoveListIndex(currentIndex, direction, count);



        if (direction is NavigationDirection.Up or NavigationDirection.Down or NavigationDirection.Left or NavigationDirection.Right)

        {

            return SelectSpatialNextIndex(

                currentIndex,

                direction,

                positions,

                allowVerticalWrap: false,

                allowHorizontalWrap: false) ?? currentIndex;

        }



        return currentIndex;

    }



    public int MoveCatalogIndex(

        int currentIndex,

        NavigationDirection direction,

        int count,

        IReadOnlyList<(double X, double Y)>? positions = null)

    {

        if (count <= 0)

            return -1;



        if (positions == null || positions.Count != count)

            return MoveListIndex(currentIndex, direction, count);



        return SelectSpatialNextIndex(

            currentIndex,

            direction,

            positions,

            allowVerticalWrap: false,

            allowHorizontalWrap: false) ?? currentIndex;

    }



    private static int SelectWrapIndex(

        int currentIndex,

        NavigationDirection direction,

        IReadOnlyList<(double X, double Y)> positions)

    {

        if (positions.Count <= 1)

            return currentIndex;



        var current = positions[currentIndex];

        var bestIndex = currentIndex;

        var bestScore = double.MaxValue;



        for (var i = 0; i < positions.Count; i++)

        {

            if (i == currentIndex)

                continue;



            var candidate = positions[i];

            var score = direction switch

            {

                NavigationDirection.Up => Math.Abs(candidate.X - current.X) + (10_000 - candidate.Y),

                NavigationDirection.Down => Math.Abs(candidate.X - current.X) + candidate.Y,

                NavigationDirection.Left => Math.Abs(candidate.Y - current.Y) + (10_000 - candidate.X),

                NavigationDirection.Right => Math.Abs(candidate.Y - current.Y) + candidate.X,

                _ => double.MaxValue,

            };



            if (score < bestScore)

            {

                bestScore = score;

                bestIndex = i;

            }

        }



        return bestIndex;

    }



    private static double? CalculateNavigationScore(

        (double X, double Y) current,

        (double X, double Y) candidate,

        NavigationDirection direction)

    {

        var dx = candidate.X - current.X;

        var dy = candidate.Y - current.Y;



        double primaryDistance;

        double secondaryDistance;



        switch (direction)

        {

            case NavigationDirection.Up:

                if (dy >= -1)

                    return null;

                primaryDistance = Math.Abs(dy);

                secondaryDistance = Math.Abs(dx);

                break;

            case NavigationDirection.Down:

                if (dy <= 1)

                    return null;

                primaryDistance = Math.Abs(dy);

                secondaryDistance = Math.Abs(dx);

                break;

            case NavigationDirection.Left:

                if (dx >= -1)

                    return null;

                primaryDistance = Math.Abs(dx);

                secondaryDistance = Math.Abs(dy);

                break;

            case NavigationDirection.Right:

                if (dx <= 1)

                    return null;

                primaryDistance = Math.Abs(dx);

                secondaryDistance = Math.Abs(dy);

                break;

            default:

                return null;

        }



        var offAxisPenalty = secondaryDistance > 10 ? secondaryDistance * 2.5 : 0;

        return primaryDistance + (secondaryDistance * 0.3) + offAxisPenalty;

    }

}


