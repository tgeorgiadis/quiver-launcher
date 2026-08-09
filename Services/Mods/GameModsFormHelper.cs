using System.Text;
using QuiverLauncher.Services.Mods.Providers.GameBanana;

namespace QuiverLauncher.Services.Mods;

/// <summary>Parse/format helpers for the entry-form mods fields.</summary>
public static class GameModsFormHelper
{
    public static string FormatSourcesForEditor(IEnumerable<GameModSource>? sources)
    {
        var normalized = GameModsConfig.NormalizeSources(sources);
        if (normalized.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var source in normalized)
        {
            if (string.Equals(source.Provider, ModProviderIds.Thunderstore, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Provider, ModProviderIds.GameBanana, StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(source.SourceUrl);
            else
                sb.AppendLine($"{source.Provider}|{source.SourceUrl}");
        }

        return sb.ToString().TrimEnd();
    }

    public static List<GameModSource> ParseSourcesFromEditor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var sources = new List<GameModSource>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string provider;
            string url;
            var pipe = line.IndexOf('|');
            if (pipe > 0)
            {
                provider = GameModsConfig.NormalizeProvider(line[..pipe]);
                url = line[(pipe + 1)..].Trim();
            }
            else if (GameBananaSourceParser.LooksLikeGameBananaUrl(line) ||
                     GameBananaSourceParser.TryParse(line, out _))
            {
                provider = ModProviderIds.GameBanana;
                url = line;
            }
            else
            {
                provider = ModProviderIds.Thunderstore;
                url = line;
            }

            // Prefer GameBanana when the URL host is gamebanana.com even with an explicit prefix typo.
            if (GameBananaSourceParser.LooksLikeGameBananaUrl(url))
                provider = ModProviderIds.GameBanana;

            if (url.Length == 0)
                continue;

            sources.Add(new GameModSource { Provider = provider, SourceUrl = url });
        }

        return GameModsConfig.NormalizeSources(sources);
    }
}
