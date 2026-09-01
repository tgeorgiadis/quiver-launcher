namespace QuiverLauncher.Services;

/// <summary>
/// Caps the Create/Edit App Entry field scroller so tall screens show more
/// fields, while short screens still leave room for title and buttons.
/// </summary>
internal static class EntryFormOverlayLayout
{
    public const double ChromeAllowance = 240;
    public const double MinScrollHeight = 400;
    public const double MaxScrollHeight = 760;

    public static double? ResolveScrollMaxHeight(double availableHeight, bool fillAvailable)
    {
        if (fillAvailable)
            return null;

        if (availableHeight <= 0)
            return MaxScrollHeight;

        return Math.Clamp(availableHeight - ChromeAllowance, MinScrollHeight, MaxScrollHeight);
    }
}
