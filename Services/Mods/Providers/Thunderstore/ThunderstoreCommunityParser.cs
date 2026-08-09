using System.Text.RegularExpressions;

namespace QuiverLauncher.Services.Mods.Providers.Thunderstore;

public static partial class ThunderstoreCommunityParser
{
    [GeneratedRegex(
        @"^(?:https?://)?(?:www\.)?thunderstore\.io/c/(?<slug>[a-zA-Z0-9\-._]+)/?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommunityUrlRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9\-._]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    public static bool TryParse(string? sourceUrl, out string communitySlug)
    {
        communitySlug = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return false;

        var trimmed = sourceUrl.Trim().TrimEnd('/');
        var match = CommunityUrlRegex().Match(trimmed);
        if (match.Success)
        {
            communitySlug = match.Groups["slug"].Value.ToLowerInvariant();
            return communitySlug.Length > 0;
        }

        if (SlugRegex().IsMatch(trimmed) &&
            !trimmed.Contains('/') &&
            !trimmed.Contains('\\') &&
            !trimmed.Contains(':'))
        {
            communitySlug = trimmed.ToLowerInvariant();
            return true;
        }

        return false;
    }

    public static string BuildCommunityPageUrl(string communitySlug) =>
        $"https://thunderstore.io/c/{communitySlug}/";
}
