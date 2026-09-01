using System.Text;

namespace QuiverLauncher.Services;

public readonly record struct MarkdownHtmlTag(
    string Name,
    bool IsClosing,
    bool IsSelfClosing,
    IReadOnlyDictionary<string, string> Attributes,
    int Length);

public static class MarkdownHtml
{
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "img", "br", "hr", "input", "meta", "link", "source",
    };

    private static readonly HashSet<string> TrackedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "a", "center", "span",
    };

    public static bool LooksLikeHtml(string line)
    {
        var i = SkipWs(line, 0);
        return i < line.Length && line[i] == '<' && i + 1 < line.Length &&
               (char.IsLetter(line[i + 1]) || line[i + 1] == '/');
    }

    public static bool TryCollectBlock(
        IReadOnlyList<string> lines,
        int start,
        out int lineCount,
        out string joined,
        out bool center)
    {
        lineCount = 0;
        joined = string.Empty;
        center = false;
        if (start < 0 || start >= lines.Count)
            return false;

        var first = lines[start].TrimEnd('\r');
        if (!LooksLikeHtml(first))
            return false;

        var stack = new List<string>();
        var sb = new StringBuilder();
        var seenTag = false;

        for (var i = start; i < lines.Count && i - start < 80; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (i > start && stack.Count == 0 && seenTag &&
                (!LooksLikeHtml(line.TrimStart()) || IsMarkdownStructure(line)))
            {
                break;
            }

            if (i > start)
                sb.Append('\n');
            sb.Append(line);

            var idx = 0;
            while (idx < line.Length)
            {
                if (!TryParseTag(line, idx, out var tag))
                {
                    idx++;
                    continue;
                }

                seenTag = true;
                idx += tag.Length;
                if (!TrackedTags.Contains(tag.Name) || VoidTags.Contains(tag.Name) || tag.IsSelfClosing)
                    continue;

                if (tag.IsClosing)
                {
                    var pop = stack.FindLastIndex(name =>
                        name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase));
                    if (pop >= 0)
                        stack.RemoveAt(pop);
                    continue;
                }

                if (IsCentered(tag))
                    center = true;
                stack.Add(tag.Name);
            }

            lineCount = i - start + 1;
            if (stack.Count == 0 && seenTag)
            {
                var next = i + 1 < lines.Count ? lines[i + 1].TrimEnd('\r') : string.Empty;
                if (LooksLikeHtml(next.TrimStart()) && !IsMarkdownStructure(next))
                    continue;
                break;
            }
        }

        joined = sb.ToString();
        return seenTag && lineCount > 0;
    }

    public static bool TryParseTag(string text, int index, out MarkdownHtmlTag tag)
    {
        tag = default;
        if (index < 0 || index >= text.Length || text[index] != '<')
            return false;

        var start = index;
        index++;
        var closing = false;
        if (index < text.Length && text[index] == '/')
        {
            closing = true;
            index++;
        }

        if (index >= text.Length || !char.IsLetter(text[index]))
            return false;

        var nameStart = index;
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '-'))
            index++;
        var name = text[nameStart..index];
        if (name.Length == 0)
            return false;

        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var selfClosing = false;
        while (index < text.Length)
        {
            index = SkipWs(text, index);
            if (index >= text.Length)
                return false;

            if (text[index] == '>')
            {
                index++;
                break;
            }

            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '>')
            {
                selfClosing = true;
                index += 2;
                break;
            }

            var attrStart = index;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '-' or '_' or ':'))
                index++;
            if (index == attrStart)
                return false;

            var attrName = text[attrStart..index];
            index = SkipWs(text, index);
            var attrValue = string.Empty;
            if (index < text.Length && text[index] == '=')
            {
                index = SkipWs(text, index + 1);
                if (index < text.Length && text[index] is '"' or '\'')
                {
                    var quote = text[index++];
                    var valueStart = index;
                    while (index < text.Length && text[index] != quote)
                        index++;
                    attrValue = text[valueStart..index];
                    if (index < text.Length)
                        index++;
                }
                else
                {
                    var valueStart = index;
                    while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not ('>' or '/'))
                        index++;
                    attrValue = text[valueStart..index];
                }
            }

            attrs[attrName] = DecodeEntities(attrValue);
        }

        if (VoidTags.Contains(name))
            selfClosing = true;

        tag = new MarkdownHtmlTag(name, closing, selfClosing, attrs, index - start);
        return true;
    }

    public static string DecodeEntities(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('&') < 0)
            return value;

        return value
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("&apos;", "'", StringComparison.OrdinalIgnoreCase)
            .Replace("&#39;", "'", StringComparison.OrdinalIgnoreCase);
    }

    public static string? Attr(MarkdownHtmlTag tag, string name) =>
        tag.Attributes.TryGetValue(name, out var value) ? value : null;

    public static bool TryParsePixels(string? value, out double pixels)
    {
        pixels = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            text = text[..^2].Trim();

        return double.TryParse(text, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out pixels) &&
               pixels > 0;
    }

    public static bool IsDisplaySizeHint(double? width, double? height)
    {
        if (height is > 0 and <= 96)
            return true;
        return width is > 0 and <= 320 && height is null or <= 200;
    }

    public static bool IsLayoutTag(string name) =>
        name.Equals("p", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("div", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("span", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("center", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("br", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("hr", StringComparison.OrdinalIgnoreCase);

    private static bool IsCentered(MarkdownHtmlTag tag)
    {
        if (tag.Name.Equals("center", StringComparison.OrdinalIgnoreCase))
            return true;

        var align = Attr(tag, "align");
        return align != null && align.Equals("center", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarkdownStructure(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith('#') ||
               trimmed.StartsWith('>') ||
               trimmed.StartsWith("```", StringComparison.Ordinal) ||
               trimmed.StartsWith("---", StringComparison.Ordinal);
    }

    private static int SkipWs(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        return index;
    }
}
