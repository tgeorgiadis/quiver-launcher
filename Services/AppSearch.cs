using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

/// <summary>
/// Case-insensitive app search for the library and an app list. Every whitespace token must match
/// at least one of name, project, display name, repository, folder, or tags.
/// </summary>
public static class AppSearch
{
    public static bool Matches(GameInfo? app, string? query)
    {
        var tokens = SplitTokens(query);
        if (tokens.Count == 0)
            return true;
        if (app == null)
            return false;

        var values = new List<string>();
        AddApp(values, app);
        return tokens.All(token => values.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> SplitTokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static void AddApp(List<string> values, GameInfo app)
    {
        AddIfPresent(values, app.Name);
        AddIfPresent(values, app.Project);
        AddIfPresent(values, app.CustomDisplayName);
        AddIfPresent(values, app.Repository);
        AddIfPresent(values, app.FolderName);
        AddIfPresent(values, app.DisplayName);
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
