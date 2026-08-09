using System.Text.RegularExpressions;

namespace QuiverLauncher.Services.Mods.Providers.GameBanana;

public static partial class GameBananaSourceParser
{
    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex BareIdRegex();

    /// <summary>
    /// Accepts <c>https://gamebanana.com/mods/games/{id}</c>,
    /// <c>https://gamebanana.com/games/{id}</c>, or a bare numeric game id.
    /// </summary>
    public static bool TryParse(string? sourceUrl, out string gameId)
    {
        gameId = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return false;

        var trimmed = sourceUrl.Trim();
        if (BareIdRegex().IsMatch(trimmed))
        {
            gameId = trimmed;
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("www.gamebanana.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        // mods/games/{id}
        if (segments.Length >= 3 &&
            segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("games", StringComparison.OrdinalIgnoreCase) &&
            BareIdRegex().IsMatch(segments[2]))
        {
            gameId = segments[2];
            return true;
        }

        // games/{id}
        if (segments.Length >= 2 &&
            segments[0].Equals("games", StringComparison.OrdinalIgnoreCase) &&
            BareIdRegex().IsMatch(segments[1]))
        {
            gameId = segments[1];
            return true;
        }

        return false;
    }

    public static string BuildModsPageUrl(string gameId) =>
        $"https://gamebanana.com/mods/games/{gameId}";

    public static bool LooksLikeGameBananaUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (BareIdRegex().IsMatch(trimmed))
            return false; // bare id alone is ambiguous; require URL or explicit provider

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               (uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("www.gamebanana.com", StringComparison.OrdinalIgnoreCase));
    }
}
