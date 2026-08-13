namespace QuiverLauncher.Services;

/// <summary>
/// Composes library/catalog display names from title, project, custom override, and style settings.
/// </summary>
public static class AppDisplayName
{
    public static string Resolve(
        string? name,
        string? project,
        string? customDisplayName,
        LibraryNameStyle style = LibraryNameStyle.NameAndProject)
    {
        if (!string.IsNullOrWhiteSpace(customDisplayName))
            return customDisplayName.Trim();

        var title = (name ?? string.Empty).Trim();
        var attribution = (project ?? string.Empty).Trim();

        return style switch
        {
            LibraryNameStyle.NameOnly => title.Length > 0 ? title : attribution,
            LibraryNameStyle.ProjectOnly => attribution.Length > 0 ? attribution : title,
            LibraryNameStyle.NameAndProjectInTitle => ComposeNameAndProjectInTitle(title, attribution),
            // NameAndProject: keep the title clean; project is shown via ProjectSubtitle.
            _ => title.Length > 0 ? title : attribution,
        };
    }

    /// <summary>
    /// Secondary library line (project name only) when style is <see cref="LibraryNameStyle.NameAndProject"/>.
    /// Empty when project should not be shown as a subtitle.
    /// </summary>
    public static string ResolveProjectSubtitle(
        string? name,
        string? project,
        string? customDisplayName,
        LibraryNameStyle style = LibraryNameStyle.NameAndProject)
    {
        if (style != LibraryNameStyle.NameAndProject)
            return string.Empty;

        // Custom title replaces the composed label; do not also show a project subtitle.
        if (!string.IsNullOrWhiteSpace(customDisplayName))
            return string.Empty;

        var attribution = (project ?? string.Empty).Trim();
        if (attribution.Length == 0)
            return string.Empty;

        var title = (name ?? string.Empty).Trim();
        if (title.Length > 0 &&
            string.Equals(title, attribution, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return attribution;
    }

    public static string ComposeNameAndProjectInTitle(string? name, string? project)
    {
        var title = (name ?? string.Empty).Trim();
        var attribution = (project ?? string.Empty).Trim();

        if (title.Length == 0)
            return attribution;
        if (attribution.Length == 0)
            return title;
        if (string.Equals(title, attribution, StringComparison.OrdinalIgnoreCase))
            return title;

        return $"{title} ({attribution})";
    }
}
