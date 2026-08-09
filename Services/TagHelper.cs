using System;
using System.Collections.Generic;
using System.Linq;
using QuiverLauncher;

namespace QuiverLauncher.Services
{
    public static class TagHelper
    {
        public static List<string> NormalizeTags(IEnumerable<string>? tags)
        {
            if (tags == null)
                return [];

            return tags
                .Select(t => t?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> ParseCommaSeparatedTags(string? input) =>
            NormalizeTags(input?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public static string FormatTagsForDisplay(IEnumerable<string>? tags)
        {
            var normalized = NormalizeTags(tags);
            return normalized.Count == 0 ? string.Empty : string.Join(", ", normalized);
        }

        public static List<string> MergeTags(IEnumerable<string>? primary, IEnumerable<string>? secondary)
        {
            var merged = new List<string>();
            merged.AddRange(NormalizeTags(primary));
            merged.AddRange(NormalizeTags(secondary));
            return NormalizeTags(merged);
        }

        public static bool MatchesAnyFilterTags(IEnumerable<string>? appTags, IEnumerable<string>? filterTags)
        {
            var normalizedAppTags = NormalizeTags(appTags);
            var normalizedFilterTags = NormalizeTags(filterTags);

            if (normalizedFilterTags.Count == 0)
                return true;

            if (normalizedAppTags.Count == 0)
                return false;

            return normalizedFilterTags.Any(filterTag =>
                normalizedAppTags.Contains(filterTag, StringComparer.OrdinalIgnoreCase));
        }

        public static bool MatchesAllFilterTags(IEnumerable<string>? appTags, IEnumerable<string>? filterTags)
        {
            var normalizedAppTags = NormalizeTags(appTags);
            var normalizedFilterTags = NormalizeTags(filterTags);

            if (normalizedFilterTags.Count == 0)
                return true;

            if (normalizedAppTags.Count == 0)
                return false;

            return normalizedFilterTags.All(filterTag =>
                normalizedAppTags.Contains(filterTag, StringComparer.OrdinalIgnoreCase));
        }

        public static bool MatchesFilterTags(
            IEnumerable<string>? appTags,
            IEnumerable<string>? filterTags,
            TagFilterMatchMode matchMode) =>
            matchMode == TagFilterMatchMode.All
                ? MatchesAllFilterTags(appTags, filterTags)
                : MatchesAnyFilterTags(appTags, filterTags);

        public static bool MatchesDisplayFilter(
            IEnumerable<string>? appTags,
            IEnumerable<string>? includeTags,
            TagFilterMatchMode includeMode,
            IEnumerable<string>? excludeTags,
            TagFilterMatchMode excludeMode)
        {
            if (!MatchesFilterTags(appTags, includeTags, includeMode))
                return false;

            if (NormalizeTags(excludeTags).Count == 0)
                return true;

            return !MatchesFilterTags(appTags, excludeTags, excludeMode);
        }
    }
}
