namespace QuiverLauncher.Services;

/// <summary>
/// Column count for catalog-review grid cards on a phone.
/// Portrait fills two stretched columns when the viewport is wide enough;
/// otherwise one full-width column. Landscape uses as many as fit.
/// </summary>
public static class CatalogReviewGridLayout
{
    public const double PortraitMinCardWidth = 128;
    public const double LandscapeMinCardWidth = 168;
    public const double CardGutter = 8;
    public const int PortraitMaxColumns = 2;

    public static int GetColumns(double viewportWidth, bool landscape)
    {
        if (viewportWidth <= 1 || double.IsNaN(viewportWidth) || double.IsInfinity(viewportWidth))
            return 1;

        var minCard = landscape ? LandscapeMinCardWidth : PortraitMinCardWidth;
        var columns = Math.Max(1, (int)Math.Floor(viewportWidth / (minCard + CardGutter)));
        if (!landscape)
            columns = Math.Min(columns, PortraitMaxColumns);
        return columns;
    }

    /// <summary>
    /// Width of one card so <see cref="GetColumns"/> items plus 4px side margins fill the viewport.
    /// </summary>
    public static double GetCardWidth(double viewportWidth, bool landscape)
    {
        var columns = GetColumns(viewportWidth, landscape);
        if (viewportWidth <= 1 || double.IsNaN(viewportWidth) || double.IsInfinity(viewportWidth))
            return 1;

        return Math.Max(1, Math.Floor(viewportWidth / columns) - CardGutter);
    }
}
