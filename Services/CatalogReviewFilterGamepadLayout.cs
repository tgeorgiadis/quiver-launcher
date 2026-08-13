namespace QuiverLauncher.Services;

/// <summary>
/// Index layout for catalog-review gamepad focus:
/// [status chips][tag chips][bulk actions].
/// </summary>
public static class CatalogReviewFilterGamepadLayout
{
    public enum Row
    {
        Status,
        Tags,
        Bulk,
    }

    public readonly record struct Ranges(int StatusCount, int TagCount, int BulkCount)
    {
        public int StatusStart => 0;
        public int TagStart => StatusCount;
        public int BulkStart => StatusCount + TagCount;
        public int Total => StatusCount + TagCount + BulkCount;

        public bool HasStatus => StatusCount > 0;
        public bool HasTags => TagCount > 0;
        public bool HasBulk => BulkCount > 0;

        /// <summary>Index to focus when moving Up from the review list into the filter strip.</summary>
        public int PreferredIndexFromList =>
            HasTags ? TagStart
            : HasStatus ? StatusStart
            : HasBulk ? BulkStart
            : -1;

        public Row ResolveRow(int index)
        {
            if (index < 0 || Total <= 0)
                return HasStatus ? Row.Status : HasTags ? Row.Tags : Row.Bulk;

            if (HasBulk && index >= BulkStart)
                return Row.Bulk;
            if (HasTags && index >= TagStart)
                return Row.Tags;
            return Row.Status;
        }

        public int LocalIndex(int absoluteIndex) =>
            ResolveRow(absoluteIndex) switch
            {
                Row.Bulk => Math.Max(0, absoluteIndex - BulkStart),
                Row.Tags => Math.Max(0, absoluteIndex - TagStart),
                _ => Math.Max(0, absoluteIndex - StatusStart),
            };

        public int AbsoluteIndex(Row row, int localIndex) =>
            row switch
            {
                Row.Bulk => BulkStart + localIndex,
                Row.Tags => TagStart + localIndex,
                _ => StatusStart + localIndex,
            };

        public int RowCount(Row row) =>
            row switch
            {
                Row.Bulk => BulkCount,
                Row.Tags => TagCount,
                _ => StatusCount,
            };
    }

    public static Ranges FromCounts(int statusCount, int tagCount, int bulkCount) =>
        new(
            Math.Max(0, statusCount),
            Math.Max(0, tagCount),
            Math.Max(0, bulkCount));

    /// <summary>
    /// Horizontal move that clamps at both ends (no wrap). Used for the tag chip row.
    /// </summary>
    public static int MoveHorizontalClamped(int localIndex, NavigationDirection direction, int count)
    {
        if (count <= 0)
            return -1;

        var index = localIndex < 0 ? 0 : (localIndex >= count ? count - 1 : localIndex);
        return direction switch
        {
            NavigationDirection.Left => index <= 0 ? 0 : index - 1,
            NavigationDirection.Right => index >= count - 1 ? count - 1 : index + 1,
            _ => index,
        };
    }

    /// <summary>
    /// True when Left on the first item of a row should leave to the sidebar.
    /// </summary>
    public static bool ShouldLeaveToSidebarOnLeft(int localIndex, NavigationDirection direction) =>
        direction == NavigationDirection.Left && localIndex <= 0;
}
