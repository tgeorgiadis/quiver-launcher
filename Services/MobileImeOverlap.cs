namespace QuiverLauncher.Services;

/// <summary>
/// How much of the Avalonia client area is covered by the Android IME.
/// </summary>
internal static class MobileImeOverlap
{
    public static double BottomPadding(
        double clientHeight,
        double occludedY,
        double occludedHeight,
        double scale)
    {
        if (occludedHeight <= 0 || clientHeight <= 0)
            return 0;

        var y = occludedY;
        var height = occludedHeight;
        if (scale > 1 && height > clientHeight + 1)
        {
            y /= scale;
            height /= scale;
        }

        return Math.Max(0, clientHeight - y);
    }
}
