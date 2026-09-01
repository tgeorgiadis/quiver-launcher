using System.Text.RegularExpressions;

namespace QuiverLauncher.Services;

public readonly record struct MarkdownListLine(int Level, bool Ordered, string Number, string Text);

public static partial class MarkdownBlocks
{
    [GeneratedRegex(@"^(\d+)\.\s+(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AlertTypeRegex();

    // GitHub-style hidden comments: [comment]: <> (text) or [//]: # (text)
    [GeneratedRegex(@"^\[(?:comment|//)\]:\s*(?:<>|#)(?:\s+\(.*\))?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HiddenCommentRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    public static bool TryParseListLine(string line, out MarkdownListLine item)
    {
        item = default;
        var indent = 0;
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            indent += line[i] == '\t' ? 4 : 1;
            i++;
        }

        var rest = line[i..];
        if (rest.StartsWith("- ", StringComparison.Ordinal) ||
            rest.StartsWith("* ", StringComparison.Ordinal) ||
            rest.StartsWith("+ ", StringComparison.Ordinal))
        {
            item = new MarkdownListLine(ListLevel(indent), Ordered: false, Number: "", rest[2..]);
            return true;
        }

        var ordered = OrderedListRegex().Match(rest);
        if (!ordered.Success)
            return false;

        item = new MarkdownListLine(ListLevel(indent), Ordered: true, ordered.Groups[1].Value, ordered.Groups[2].Value);
        return true;
    }

    public static bool IsBlockquoteLine(string line, out string content)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;

        if (i >= line.Length || line[i] != '>')
        {
            content = string.Empty;
            return false;
        }

        i++;
        if (i < line.Length && line[i] == ' ')
            i++;
        content = line[i..];
        return true;
    }

    public static bool IsHiddenComment(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return false;
        if (HiddenCommentRegex().IsMatch(trimmed))
            return true;
        if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
            return StripHtmlComments(trimmed).Trim().Length == 0 ||
                   !trimmed.Contains("-->", StringComparison.Ordinal);
        return false;
    }

    public static string StripHtmlComments(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("<!--", StringComparison.Ordinal) < 0)
            return text;
        return HtmlCommentRegex().Replace(text, string.Empty);
    }

    public static bool TrySkipHtmlComment(IReadOnlyList<string> lines, int start, out int lineCount)
    {
        lineCount = 0;
        if (start < 0 || start >= lines.Count)
            return false;

        var first = lines[start].TrimEnd('\r');
        var trimmed = first.TrimStart();
        if (!trimmed.StartsWith("<!--", StringComparison.Ordinal))
            return false;
        if (first.Contains("-->", StringComparison.Ordinal) &&
            StripHtmlComments(first).Trim().Length > 0)
        {
            return false;
        }

        for (var i = start; i < lines.Count; i++)
        {
            lineCount = i - start + 1;
            if (lines[i].TrimEnd('\r').Contains("-->", StringComparison.Ordinal))
                return true;
        }

        return lineCount > 0;
    }

    public static bool TryParseAlertType(string quoteContent, out string alertType)
    {
        var match = AlertTypeRegex().Match(quoteContent.Trim());
        if (!match.Success)
        {
            alertType = string.Empty;
            return false;
        }

        alertType = match.Groups[1].Value.ToUpperInvariant();
        return true;
    }

    public static string ListMarker(MarkdownListLine item) =>
        item.Ordered ? item.Number + "." : item.Level > 0 ? "◦" : "•";

    public static bool CanContinueParagraph(string nextLine)
    {
        if (string.IsNullOrWhiteSpace(nextLine))
            return false;

        var trimmed = nextLine.TrimStart();
        if (trimmed.StartsWith('#') ||
            trimmed.StartsWith("```", StringComparison.Ordinal) ||
            trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsHiddenComment(nextLine))
            return false;
        if (IsBlockquoteLine(nextLine, out _))
            return false;
        if (TryParseListLine(nextLine, out _))
            return false;
        if (MarkdownHtml.LooksLikeHtml(nextLine))
            return false;
        if (MarkdownImageLine.IsImageOnlyLine(nextLine))
            return false;

        return true;
    }

    private static int ListLevel(int indentSpaces) =>
        indentSpaces < 2 ? 0 : indentSpaces / 2;
}
