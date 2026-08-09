using System;

namespace QuiverLauncher.Services;

public static class NameSortHelper
{
    public static string GetAlphabeticalSortKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var trimmed = name.Trim();

        if (TryStripLeadingArticle(trimmed, "the ", out var remainder) ||
            TryStripLeadingArticle(trimmed, "an ", out remainder) ||
            TryStripLeadingArticle(trimmed, "a ", out remainder))
        {
            return remainder.Length > 0 ? remainder : trimmed;
        }

        return trimmed;
    }

    private static bool TryStripLeadingArticle(string name, string articleWithSpace, out string remainder)
    {
        if (name.StartsWith(articleWithSpace, StringComparison.OrdinalIgnoreCase))
        {
            remainder = name[articleWithSpace.Length..].TrimStart();
            return true;
        }

        remainder = string.Empty;
        return false;
    }
}
