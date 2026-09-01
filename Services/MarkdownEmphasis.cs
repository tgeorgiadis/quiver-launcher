namespace QuiverLauncher.Services;

public static class MarkdownEmphasis
{
    public static bool TryRead(
        string text,
        int index,
        out int consumed,
        out string inner,
        out bool bold,
        out bool italic)
    {
        consumed = 0;
        inner = string.Empty;
        bold = false;
        italic = false;
        if (index < 0 || index >= text.Length)
            return false;

        var marker = text[index];
        if (marker is not ('*' or '_'))
            return false;

        var run = CountRun(text, index, marker);
        for (var length = Math.Min(run, 3); length >= 1; length--)
        {
            if (TryReadRun(text, index, marker, length, out consumed, out inner, out bold, out italic))
                return true;
        }

        return false;
    }

    private static bool TryReadRun(
        string text,
        int index,
        char marker,
        int length,
        out int consumed,
        out string inner,
        out bool bold,
        out bool italic)
    {
        consumed = 0;
        inner = string.Empty;
        bold = false;
        italic = false;
        if (length < 1 || index + length >= text.Length)
            return false;
        if (!IsLeftFlanking(text, index, length))
            return false;
        if (marker == '_' && index > 0 && IsWordChar(text[index - 1]))
            return false;

        var search = index + length;
        while (search < text.Length)
        {
            var found = text.IndexOf(marker, search);
            if (found < 0)
                return false;
            if (CountRun(text, found, marker) < length ||
                !IsRightFlanking(text, found) ||
                found == index + length ||
                (marker == '_' && found + length < text.Length && IsWordChar(text[found + length])))
            {
                search = found + 1;
                continue;
            }

            inner = text[(index + length)..found];
            consumed = found + length - index;
            bold = length >= 2;
            italic = length % 2 == 1;
            return true;
        }

        return false;
    }

    private static int CountRun(string text, int index, char marker)
    {
        var count = 0;
        while (index + count < text.Length && text[index + count] == marker)
            count++;
        return count;
    }

    private static bool IsLeftFlanking(string text, int delimStart, int delimLen)
    {
        var after = delimStart + delimLen;
        return after < text.Length && !char.IsWhiteSpace(text[after]);
    }

    private static bool IsRightFlanking(string text, int delimStart) =>
        delimStart > 0 && !char.IsWhiteSpace(text[delimStart - 1]);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
}
