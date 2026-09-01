namespace QuiverLauncher.Services;

public readonly record struct MarkdownImageMatch(int Start, int Length, string Alt, string Url);

public static class MarkdownImageLine
{
    private static readonly string[] BadgeHosts =
    [
        "img.shields.io",
        "shields.io",
        "badgen.net",
        "badge.fury.io",
        "flat.badgen.net",
        "img.badgesize.io",
    ];

    private static readonly string[] ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg",
    ];

    public static bool TryParse(string line, out string alt, out string url) =>
        TryParseAt(line, SkipLeadingWhitespace(line), out var consumed, out alt, out url) &&
        SkipLeadingWhitespace(line) + consumed >= line.TrimEnd().Length;

    public static bool IsImageOnlyLine(string line)
    {
        var i = SkipLeadingWhitespace(line);
        if (i >= line.Length)
            return false;

        var found = false;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                i++;
                continue;
            }

            if (!TryParseAt(line, i, out var consumed, out _, out _) || consumed <= 0)
                return false;

            found = true;
            i += consumed;
        }

        return found;
    }

    public static bool TryParseAt(string text, int index, out int consumed, out string alt, out string url) =>
        TryParseAt(text, index, out consumed, out alt, out url, out _);

    public static bool TryParseAt(
        string text,
        int index,
        out int consumed,
        out string alt,
        out string url,
        out string wrapUrl)
    {
        consumed = 0;
        alt = string.Empty;
        url = string.Empty;
        wrapUrl = string.Empty;
        if (index < 0 || index >= text.Length)
            return false;

        var start = index;
        var linked = false;
        if (text[index] == '[' && index + 1 < text.Length && text[index + 1] == '!')
        {
            linked = true;
            index++;
        }

        if (index >= text.Length || text[index] != '!' ||
            index + 1 >= text.Length || text[index + 1] != '[')
        {
            return false;
        }

        index += 2;
        var altEnd = text.IndexOf(']', index);
        if (altEnd < 0)
            return false;

        alt = text[index..altEnd];
        index = altEnd + 1;
        if (index >= text.Length || text[index] != '(' ||
            !TryReadBalancedParens(text, index, out url, out index))
        {
            return false;
        }

        if (linked)
        {
            if (index >= text.Length || text[index] != ']')
                return false;
            index++;
            if (index >= text.Length || text[index] != '(' ||
                !TryReadBalancedParens(text, index, out wrapUrl, out index))
            {
                return false;
            }
        }

        consumed = index - start;
        return true;
    }

    public static string? TryGetRenderableUrl(string url, string alt, string? rawRootUrl)
    {
        var candidate = url.Trim();
        if (string.IsNullOrEmpty(candidate) && LooksLikeImageFile(alt))
            candidate = StripTrailingJunk(alt);

        if (string.IsNullOrEmpty(candidate))
            return null;

        candidate = RewriteBadgeToPng(candidate);
        return ResolveUrl(candidate, rawRootUrl);
    }

    public static bool IsCompactBadge(string resolvedUrl, string originalUrl)
    {
        return IsBadgeHost(resolvedUrl) ||
               IsBadgeHost(originalUrl) ||
               HasExtension(resolvedUrl, ".svg") ||
               HasExtension(originalUrl, ".svg");
    }

    public static bool LooksLikeButtonBadge(string url, string alt)
    {
        if (url.Contains("badge", StringComparison.OrdinalIgnoreCase) ||
            alt.Contains("badge", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return alt.StartsWith("Add to ", StringComparison.OrdinalIgnoreCase) ||
               alt.Contains("Download from", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveUrl(string url, string? rawRootUrl)
    {
        var trimmed = url.Trim();
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("//"))
            return "https:" + trimmed;

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (string.IsNullOrWhiteSpace(rawRootUrl))
            return trimmed;

        var relative = trimmed.Replace('\\', '/');
        while (relative.StartsWith("./", StringComparison.Ordinal))
            relative = relative[2..];
        relative = relative.TrimStart('/');
        return rawRootUrl.TrimEnd('/') + "/" + relative;
    }

    public static string? ResolveLinkUrl(string url, string? rawRootUrl)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            return null;

        if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("discord:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return ResolveUrl(trimmed, rawRootUrl);
    }

    public static bool TryReadMarkdownLink(
        string text,
        int index,
        out int consumed,
        out string label,
        out string url)
    {
        consumed = 0;
        label = string.Empty;
        url = string.Empty;
        if (index < 0 || index >= text.Length || text[index] != '[')
            return false;
        if (index + 1 < text.Length && text[index + 1] == '!')
            return false;

        var closeAlt = text.IndexOf(']', index + 1);
        if (closeAlt <= index ||
            closeAlt + 1 >= text.Length ||
            text[closeAlt + 1] != '(' ||
            !TryReadBalancedParens(text, closeAlt + 1, out url, out var after) ||
            string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        label = UnwrapEmphasis(text[(index + 1)..closeAlt]);
        consumed = after - index;
        return true;
    }

    public static string UnwrapEmphasis(string label)
    {
        var trimmed = label.Trim();
        if (trimmed.Length >= 4 &&
            trimmed.StartsWith("**", StringComparison.Ordinal) &&
            trimmed.EndsWith("**", StringComparison.Ordinal))
        {
            return trimmed[2..^2];
        }

        if (trimmed.Length >= 2 && trimmed[0] == '*' && trimmed[^1] == '*')
            return trimmed[1..^1];

        return label;
    }

    public static string? LinkUrlAt(
        IReadOnlyList<(int Start, int End, string Url)> spans,
        int characterIndex)
    {
        foreach (var (start, end, url) in spans)
        {
            if (characterIndex >= start && characterIndex < end)
                return url;
        }

        return null;
    }

    internal static bool TryReadBalancedParens(string text, int openIndex, out string inner, out int after)
    {
        inner = string.Empty;
        after = openIndex;
        if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != '(')
            return false;

        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth != 0)
                    continue;

                inner = StripOptionalTitle(text[(openIndex + 1)..i].Trim());
                after = i + 1;
                return true;
            }
        }

        return false;
    }

    private static string StripOptionalTitle(string inner)
    {
        if (inner.Length >= 2 && inner[^1] == '"')
        {
            var start = inner.IndexOf(" \"", StringComparison.Ordinal);
            if (start >= 0)
                return inner[..start].Trim();
        }

        return inner;
    }

    private static string RewriteBadgeToPng(string url)
    {
        if (!IsBadgeHost(url))
            return url;

        if (HasExtension(url, ".png") || HasExtension(url, ".jpg") || HasExtension(url, ".jpeg") ||
            HasExtension(url, ".gif") || HasExtension(url, ".webp"))
        {
            return url;
        }

        if (HasExtension(url, ".svg"))
        {
            var suffixIndex = url.LastIndexOf(".svg", StringComparison.OrdinalIgnoreCase);
            return suffixIndex < 0 ? url : url[..suffixIndex] + ".png" + url[(suffixIndex + 4)..];
        }

        var query = url.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? url[..query] + ".png" + url[query..] : url + ".png";
    }

    private static bool LooksLikeImageFile(string value)
    {
        var trimmed = StripTrailingJunk(value);
        foreach (var extension in ImageExtensions)
        {
            if (trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string StripTrailingJunk(string value) =>
        value.Trim().TrimEnd(')', ']');

    private static bool HasExtension(string url, string extension)
    {
        var path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            path = absolute.AbsolutePath;
        else
        {
            var query = path.IndexOf('?', StringComparison.Ordinal);
            if (query >= 0)
                path = path[..query];
        }

        return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBadgeHost(string url)
    {
        foreach (var host in BadgeHosts)
        {
            if (url.Contains(host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int SkipLeadingWhitespace(string line)
    {
        var i = 0;
        while (i < line.Length && char.IsWhiteSpace(line[i]))
            i++;
        return i;
    }
}
