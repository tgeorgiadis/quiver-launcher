namespace QuiverLauncher.Services;

internal static class ActionOverflowLayout
{
    // Actions retain their order. The returned prefix stays inline; the rest go in More.
    public static int InlineCount(IReadOnlyList<double> widths, double available, double moreWidth, double spacing)
    {
        if (widths.Count == 0) return 0;
        if (widths.Sum() + spacing * (widths.Count - 1) <= available) return widths.Count;
        var used = moreWidth;
        var count = 0;
        foreach (var width in widths)
        {
            if (used + spacing + width > available) break;
            used += spacing + width;
            count++;
        }
        return count;
    }
}
