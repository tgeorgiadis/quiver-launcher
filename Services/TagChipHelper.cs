namespace QuiverLauncher.Services;

/// <summary>
/// Helpers for the tag chips shown on library cards.
/// </summary>
public static class TagChipHelper
{
    public const int DefaultLibraryCardTagMaxLines = 1;
    /// <summary>Stored value for “No limit” on library card tag lines. 0 means hidden.</summary>
    public const int UnlimitedLibraryCardTagMaxLines = 99;
    /// <summary>
    /// WrapPanel row height for library card tag chips (FontSize 9 + vertical padding + bottom margin).
    /// Keep in sync with library card tag chip styling in MainWindow.axaml.
    /// </summary>
    public const double LibraryCardTagLineHeight = 16;

    public static int NormalizeLibraryCardTagMaxLines(int maxLines)
    {
        if (maxLines < 0)
            return 0;
        return maxLines > UnlimitedLibraryCardTagMaxLines ? UnlimitedLibraryCardTagMaxLines : maxLines;
    }

    /// <summary>
    /// MaxHeight for clipped library card tag strips. 0 hides the strip; 99 is unlimited.
    /// </summary>
    public static double GetLibraryCardTagsMaxHeight(int maxLines)
    {
        var lines = NormalizeLibraryCardTagMaxLines(maxLines);
        if (lines <= 0)
            return 0;
        if (lines >= UnlimitedLibraryCardTagMaxLines)
            return double.PositiveInfinity;
        return lines * LibraryCardTagLineHeight;
    }

    public static List<string> SelectTagsForCardDisplay(IEnumerable<string>? appTags, int maxLines)
    {
        if (NormalizeLibraryCardTagMaxLines(maxLines) == 0)
            return [];

        return TagHelper.NormalizeTags(appTags);
    }
}
