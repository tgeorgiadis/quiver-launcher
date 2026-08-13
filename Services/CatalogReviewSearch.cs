using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

/// <summary>
/// Case-insensitive catalog-review search. Every whitespace token must match
/// at least one of name, project, display name, repository, folder, or tags.
/// </summary>
public static class CatalogReviewSearch
{
    public static bool Matches(CatalogSyncRowItem row, string? query)
    {
        var tokens = SplitTokens(query);
        if (tokens.Count == 0)
            return true;

        var haystacks = CollectHaystacks(row);
        return tokens.All(token =>
            haystacks.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    public static List<string> SplitTokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static List<string> CollectHaystacks(CatalogSyncRowItem row)
    {
        var values = new List<string>();
        AddIfPresent(values, row.DisplayName);
        AddIfPresent(values, row.Repository);
        AddApp(values, row.External);
        AddApp(values, row.Local);
        return values;
    }

    private static void AddApp(List<string> values, GameInfo? app)
    {
        if (app == null)
            return;

        AddIfPresent(values, app.Name);
        AddIfPresent(values, app.Project);
        AddIfPresent(values, app.CustomDisplayName);
        AddIfPresent(values, app.Repository);
        AddIfPresent(values, app.FolderName);
        AddIfPresent(
            values,
            AppDisplayName.Resolve(
                app.Name,
                app.Project,
                app.CustomDisplayName,
                LibraryNameStyle.NameAndProjectInTitle));

        foreach (var tag in app.Tags)
            AddIfPresent(values, tag);
    }

    private static void AddIfPresent(List<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(value.Trim());
    }
}
