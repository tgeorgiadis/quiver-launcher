namespace QuiverLauncher.Services;

public enum TagChipState
{
    Neutral = 0,
    Include = 1,
    Exclude = 2,
}

/// <summary>
/// Domain-agnostic helpers for frequent/featured tag chips and tri-state filtering.
/// </summary>
public static class TagChipHelper
{
    public const int DefaultMaxChips = 12;
    public const int DefaultLibraryCardTagMaxLines = 2;
    public const int MaxLibraryCardTagMaxLines = 8;
    /// <summary>
    /// WrapPanel row height for library card tag chips (FontSize 9 + vertical padding + bottom margin).
    /// Keep in sync with library card tag chip styling in MainWindow.axaml.
    /// </summary>
    public const double LibraryCardTagLineHeight = 16;

    public static int NormalizeLibraryCardTagMaxLines(int maxLines)
    {
        if (maxLines < 0)
            return 0;
        return maxLines > MaxLibraryCardTagMaxLines ? MaxLibraryCardTagMaxLines : maxLines;
    }

    /// <summary>
    /// MaxHeight for clipped library card tag strips. Returns <see cref="double.PositiveInfinity"/> when unlimited (0).
    /// </summary>
    public static double GetLibraryCardTagsMaxHeight(int maxLines)
    {
        var lines = NormalizeLibraryCardTagMaxLines(maxLines);
        return lines <= 0 ? double.PositiveInfinity : lines * LibraryCardTagLineHeight;
    }

    public static List<string> RankTagsByFrequency(
        IEnumerable<IEnumerable<string>?> appTagSets,
        IEnumerable<string>? featuredTags = null,
        IEnumerable<string>? pinnedTags = null,
        IEnumerable<string>? hiddenTags = null,
        int maxChips = DefaultMaxChips)
    {
        var hidden = TagHelper.NormalizeTags(hiddenTags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tagSet in appTagSets)
        {
            foreach (var tag in TagHelper.NormalizeTags(tagSet))
            {
                if (hidden.Contains(tag))
                    continue;
                counts[tag] = counts.TryGetValue(tag, out var count) ? count + 1 : 1;
            }
        }

        if (counts.Count == 0)
            return [];

        var featured = TagHelper.NormalizeTags(featuredTags).Where(tag => !hidden.Contains(tag)).ToList();
        var pinned = TagHelper.NormalizeTags(pinnedTags).Where(tag => !hidden.Contains(tag)).ToList();
        var present = counts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();

        void AddIfPresent(IEnumerable<string> candidates)
        {
            foreach (var tag in candidates)
            {
                if (!present.Contains(tag))
                    continue;
                if (ordered.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    continue;
                ordered.Add(tag);
                if (ordered.Count >= maxChips)
                    return;
            }
        }

        void AddPresentByFrequency(IEnumerable<string> candidates)
        {
            var candidateSet = candidates.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in counts
                         .Where(kv => candidateSet.Contains(kv.Key))
                         .OrderByDescending(kv => kv.Value)
                         .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                         .Select(kv => kv.Key))
            {
                if (ordered.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    continue;
                ordered.Add(tag);
                if (ordered.Count >= maxChips)
                    return;
            }
        }

        AddIfPresent(pinned);
        if (ordered.Count >= maxChips)
            return ordered;

        AddPresentByFrequency(featured);
        if (ordered.Count >= maxChips)
            return ordered;

        foreach (var tag in counts
                     .OrderByDescending(kv => kv.Value)
                     .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                     .Select(kv => kv.Key))
        {
            if (ordered.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                continue;
            ordered.Add(tag);
            if (ordered.Count >= maxChips)
                break;
        }

        return ordered;
    }

    public static List<string> SelectTagsForCardDisplay(
        IEnumerable<string>? appTags,
        LibraryTagDisplayMode mode,
        IEnumerable<string>? featuredOrCommonTags)
    {
        var tags = TagHelper.NormalizeTags(appTags);
        if (tags.Count == 0 || mode == LibraryTagDisplayMode.Hidden)
            return [];

        if (mode == LibraryTagDisplayMode.All)
            return tags;

        var featured = TagHelper.NormalizeTags(featuredOrCommonTags);
        if (featured.Count == 0)
            return [];

        return tags
            .Where(tag => featured.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    public static TagChipState CycleState(TagChipState current) =>
        current switch
        {
            TagChipState.Neutral => TagChipState.Include,
            TagChipState.Include => TagChipState.Exclude,
            _ => TagChipState.Neutral,
        };

    public static bool MatchesTriStateChips(
        IEnumerable<string>? appTags,
        IReadOnlyDictionary<string, TagChipState>? chipStates)
    {
        if (chipStates == null || chipStates.Count == 0)
            return true;

        var tags = TagHelper.NormalizeTags(appTags);
        var include = chipStates
            .Where(kv => kv.Value == TagChipState.Include)
            .Select(kv => kv.Key)
            .ToList();
        var exclude = chipStates
            .Where(kv => kv.Value == TagChipState.Exclude)
            .Select(kv => kv.Key)
            .ToList();

        return TagHelper.MatchesDisplayFilter(
            tags,
            include,
            TagFilterMatchMode.Any,
            exclude,
            TagFilterMatchMode.Any);
    }
}
