using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace QuiverLauncher.Services.Mods.Providers.GameBanana;

/// <summary>Light HTML → markdown/plain conversion for GameBanana descriptions.</summary>
internal static partial class GameBananaHtml
{
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"</p\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosePRegex();

    [GeneratedRegex(@"<li\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LiRegex();

    [GeneratedRegex(@"<h([1-6])\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenHeadingRegex();

    [GeneratedRegex(@"</h[1-6]\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CloseHeadingRegex();

    [GeneratedRegex(@"<a\s+[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessNewlinesRegex();

    public static string ToMarkdown(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = html;
        text = BrRegex().Replace(text, "\n");
        text = ClosePRegex().Replace(text, "\n\n");
        text = LiRegex().Replace(text, "- ");
        text = OpenHeadingRegex().Replace(text, m =>
        {
            var level = int.Parse(m.Groups[1].Value);
            return "\n" + new string('#', Math.Clamp(level, 1, 6)) + " ";
        });
        text = CloseHeadingRegex().Replace(text, "\n\n");
        text = AnchorRegex().Replace(text, m => $"[{m.Groups[2].Value}]({m.Groups[1].Value})");
        text = TagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = ExcessNewlinesRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    public static string FormatUpdatesChangelog(IEnumerable<GameBananaUpdateRecord> updates)
    {
        var sb = new StringBuilder();
        foreach (var update in updates)
        {
            var title = !string.IsNullOrWhiteSpace(update.Version)
                ? update.Version!.Trim()
                : (!string.IsNullOrWhiteSpace(update.Name) ? update.Name!.Trim() : "Update");
            if (!string.IsNullOrWhiteSpace(update.Name) &&
                !string.IsNullOrWhiteSpace(update.Version) &&
                !string.Equals(update.Name, update.Version, StringComparison.OrdinalIgnoreCase))
            {
                title = $"{update.Version!.Trim()} — {update.Name!.Trim()}";
            }

            sb.AppendLine($"## {title}");
            sb.AppendLine();

            if (update.ChangeLog is { Count: > 0 })
            {
                foreach (var entry in update.ChangeLog)
                {
                    var line = entry.Text ?? entry.SText;
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    sb.AppendLine($"- {line.Trim()}");
                }

                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                sb.AppendLine(ToMarkdown(update.Text));
                sb.AppendLine();
            }
        }

        var result = sb.ToString().Trim();
        return result.Length == 0 ? string.Empty : result;
    }
}
