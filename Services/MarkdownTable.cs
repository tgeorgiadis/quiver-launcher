namespace QuiverLauncher.Services;

public enum MarkdownTableAlign
{
    Left,
    Center,
    Right,
}

public sealed record MarkdownTableModel(
    IReadOnlyList<string> Headers,
    IReadOnlyList<MarkdownTableAlign> Alignments,
    IReadOnlyList<IReadOnlyList<string>> Rows);

public static class MarkdownTable
{
    public static bool TryCollect(
        IReadOnlyList<string> lines,
        int start,
        out int lineCount,
        out MarkdownTableModel table)
    {
        lineCount = 0;
        table = new MarkdownTableModel([], [], []);
        if (start < 0 || start + 1 >= lines.Count)
            return false;

        if (!TryParseRow(lines[start].TrimEnd('\r'), out var headers) || headers.Count == 0)
            return false;

        if (!TryParseSeparator(lines[start + 1].TrimEnd('\r'), headers.Count, out var alignments))
            return false;

        var rows = new List<IReadOnlyList<string>>();
        var i = start + 2;
        while (i < lines.Count)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || !LooksLikeRow(line))
                break;

            if (!TryParseRow(line, out var cells))
                break;

            rows.Add(PadCells(cells, headers.Count));
            i++;
        }

        lineCount = i - start;
        table = new MarkdownTableModel(headers, alignments, rows);
        return true;
    }

    public static bool TryParseRow(string line, out List<string> cells)
    {
        cells = [];
        if (!LooksLikeRow(line))
            return false;

        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal))
            trimmed = trimmed[..^1];

        cells = trimmed.Split('|').Select(static cell => cell.Trim()).ToList();
        return cells.Count > 0;
    }

    public static bool TryParseSeparator(string line, int columnCount, out List<MarkdownTableAlign> alignments)
    {
        alignments = [];
        if (!TryParseRow(line, out var cells) || cells.Count == 0)
            return false;

        foreach (var cell in cells)
        {
            if (!TryParseAlignment(cell, out var align))
                return false;
            alignments.Add(align);
        }

        while (alignments.Count < columnCount)
            alignments.Add(MarkdownTableAlign.Left);
        if (alignments.Count > columnCount)
            alignments.RemoveRange(columnCount, alignments.Count - columnCount);
        return true;
    }

    private static bool LooksLikeRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Contains('|') &&
               !trimmed.StartsWith("```", StringComparison.Ordinal) &&
               !trimmed.StartsWith('#') &&
               !trimmed.StartsWith('>');
    }

    private static bool TryParseAlignment(string cell, out MarkdownTableAlign align)
    {
        align = MarkdownTableAlign.Left;
        var value = cell.Trim();
        if (value.Length == 0)
            return false;

        var left = value[0] == ':';
        var right = value.Length > 1 && value[^1] == ':';
        var dashes = value[(left ? 1 : 0)..(right ? ^1 : ^0)];
        // GFM allows one or more hyphens (GitHub READMEs often use `| - |`).
        if (dashes.Length < 1 || dashes.Any(static c => c != '-'))
            return false;

        align = left && right ? MarkdownTableAlign.Center : right ? MarkdownTableAlign.Right : MarkdownTableAlign.Left;
        return true;
    }

    private static List<string> PadCells(List<string> cells, int columnCount)
    {
        while (cells.Count < columnCount)
            cells.Add(string.Empty);
        if (cells.Count > columnCount)
            cells.RemoveRange(columnCount, cells.Count - columnCount);
        return cells;
    }
}
