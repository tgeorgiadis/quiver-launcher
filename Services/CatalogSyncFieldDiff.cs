using Avalonia.Media;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Services;

public enum CatalogSyncFieldDiffKind
{
    ValueChange,
    Tags,
    ExternalPreview,
    Icon,
    Snapshot,
}

public enum CatalogSyncTagDiffKind
{
    Shared,
    LocalOnly,
    ExternalOnly,
}

public class CatalogSyncTagDiffItem
{
    public string Tag { get; init; } = "";
    public CatalogSyncTagDiffKind Kind { get; init; }

    public string Tooltip => Kind switch
    {
        CatalogSyncTagDiffKind.LocalOnly => "Only in your library (kept on Merge, removed on Replace)",
        CatalogSyncTagDiffKind.ExternalOnly => "Only in external list (added on Merge and Replace)",
        _ => "In both",
    };

    public IBrush BackgroundBrush => Kind switch
    {
        CatalogSyncTagDiffKind.LocalOnly => new SolidColorBrush(Color.Parse("#5C4A1F")),
        CatalogSyncTagDiffKind.ExternalOnly => new SolidColorBrush(Color.Parse("#1E4D32")),
        _ => new SolidColorBrush(Color.Parse("#3A3A3A")),
    };

    public IBrush ForegroundBrush => Kind switch
    {
        CatalogSyncTagDiffKind.LocalOnly => new SolidColorBrush(Color.Parse("#FBBF77")),
        CatalogSyncTagDiffKind.ExternalOnly => new SolidColorBrush(Color.Parse("#6EE7A0")),
        _ => new SolidColorBrush(Color.Parse("#B8B8B8")),
    };
}

public class CatalogSyncFieldDiffItem
{
    public string FieldLabel { get; init; } = "";
    public CatalogSyncFieldDiffKind Kind { get; init; }
    public string? LocalValue { get; init; }
    public string? ExternalValue { get; init; }
    public IReadOnlyList<CatalogSyncTagDiffItem> TagDiffs { get; init; } = [];

    public bool IsTagDiff => Kind == CatalogSyncFieldDiffKind.Tags;
    public bool IsIconDiff => Kind == CatalogSyncFieldDiffKind.Icon;
    public bool IsSnapshot => Kind == CatalogSyncFieldDiffKind.Snapshot;
    public bool IsTextValueDiff => Kind is CatalogSyncFieldDiffKind.ValueChange
        or CatalogSyncFieldDiffKind.ExternalPreview
        or CatalogSyncFieldDiffKind.Snapshot;
    public bool HasLocalValue => !string.IsNullOrEmpty(LocalValue);
    public bool HasExternalValue => !string.IsNullOrEmpty(ExternalValue);
    public bool ShowIncomingValue => HasExternalValue && !IsSnapshot;
    public bool ShowArrow => HasLocalValue && HasExternalValue;
    public bool ShowEmptyLocal => Kind == CatalogSyncFieldDiffKind.ValueChange && !HasLocalValue;
    public bool ShowEmptyExternal => Kind == CatalogSyncFieldDiffKind.ValueChange && !HasExternalValue;
}

public static class CatalogSyncFieldDiffBuilder
{
    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "Name",
        ["project"] = "Project",
        ["folderName"] = "Folder",
        ["installPath"] = "Install path",
        ["appIconUrl"] = "Icon",
        ["preferredVersion"] = "Preferred version",
        ["tags"] = "Tags",
        ["filesToAdd"] = "Files to add",
        ["releaseAssetFilter"] = "Release asset filter",
        ["mods"] = "Mods",
        ["repositorySource"] = "Repository source",
        ["repository"] = "Repository",
    };

    public static IReadOnlyList<CatalogSyncFieldDiffItem> BuildFieldDiffs(
        CatalogSyncStatus status,
        GameInfo? local,
        GameInfo? external,
        IReadOnlyList<string> changedFields)
    {
        if (status == CatalogSyncStatus.InExternalOnly && external != null)
            return BuildExternalPreview(external);

        if (status != CatalogSyncStatus.Changed || local == null || external == null)
            return [];

        var diffs = new List<CatalogSyncFieldDiffItem>();
        foreach (var field in changedFields)
        {
            if (string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase))
                diffs.Add(BuildTagDiff(local, external));
            else if (string.Equals(field, "appIconUrl", StringComparison.OrdinalIgnoreCase))
                diffs.Add(BuildIconDiff(local, external));
            else
                diffs.Add(BuildValueDiff(field, local, external));
        }

        return OrderFieldDiffs(diffs);
    }

    public static IReadOnlyList<CatalogSyncFieldDiffItem> BuildSnapshot(GameInfo? app)
    {
        if (app == null)
            return [];

        return BuildPreview(app, CatalogSyncFieldDiffKind.Snapshot, CatalogSyncTagDiffKind.Shared);
    }

    private static IReadOnlyList<CatalogSyncFieldDiffItem> BuildExternalPreview(GameInfo external) =>
        BuildPreview(external, CatalogSyncFieldDiffKind.ExternalPreview, CatalogSyncTagDiffKind.ExternalOnly);

    private static IReadOnlyList<CatalogSyncFieldDiffItem> BuildPreview(
        GameInfo app,
        CatalogSyncFieldDiffKind valueKind,
        CatalogSyncTagDiffKind tagKind)
    {
        var diffs = new List<CatalogSyncFieldDiffItem>();

        if (TryProjectPreview(app, valueKind) is { } projectPreview)
            diffs.Add(projectPreview);

        if (!string.IsNullOrWhiteSpace(app.FolderName))
        {
            diffs.Add(new CatalogSyncFieldDiffItem
            {
                FieldLabel = "Folder",
                Kind = valueKind,
                ExternalValue = app.FolderName,
            });
        }

        var tags = TagHelper.NormalizeTags(app.Tags);
        if (tags.Count > 0)
        {
            diffs.Add(new CatalogSyncFieldDiffItem
            {
                FieldLabel = "Tags",
                Kind = CatalogSyncFieldDiffKind.Tags,
                TagDiffs = tags
                    .Select(tag => new CatalogSyncTagDiffItem
                    {
                        Tag = tag,
                        Kind = tagKind,
                    })
                    .ToList(),
            });
        }

        if (!string.IsNullOrWhiteSpace(app.PreferredVersion))
        {
            diffs.Add(new CatalogSyncFieldDiffItem
            {
                FieldLabel = "Preferred version",
                Kind = valueKind,
                ExternalValue = app.PreferredVersion,
            });
        }

        var modsDisplay = GameModsConfig.FormatForDisplay(app.ModsPath, app.ModsSources, app.ModsLayout);
        if (!string.IsNullOrWhiteSpace(modsDisplay))
        {
            diffs.Add(new CatalogSyncFieldDiffItem
            {
                FieldLabel = "Mods",
                Kind = valueKind,
                ExternalValue = modsDisplay,
            });
        }

        return diffs;
    }

    private static CatalogSyncFieldDiffItem? TryProjectPreview(GameInfo app, CatalogSyncFieldDiffKind kind)
    {
        var project = (app.Project ?? string.Empty).Trim();
        if (project.Length == 0)
            return null;

        var name = (app.Name ?? string.Empty).Trim();
        if (name.Length > 0 && string.Equals(name, project, StringComparison.OrdinalIgnoreCase))
            return null;

        return new CatalogSyncFieldDiffItem
        {
            FieldLabel = "Project",
            Kind = kind,
            ExternalValue = project,
        };
    }

    private static IReadOnlyList<CatalogSyncFieldDiffItem> OrderFieldDiffs(List<CatalogSyncFieldDiffItem> diffs)
    {
        return diffs
            .Select((diff, index) => (diff, index))
            .OrderBy(item => FieldRank(item.diff.FieldLabel))
            .ThenBy(item => item.index)
            .Select(item => item.diff)
            .ToList();
    }

    private static int FieldRank(string label) =>
        label switch
        {
            "Name" => 0,
            "Project" => 1,
            _ => 2,
        };

    private static CatalogSyncFieldDiffItem BuildIconDiff(GameInfo local, GameInfo external) =>
        new()
        {
            FieldLabel = "Icon",
            Kind = CatalogSyncFieldDiffKind.Icon,
            LocalValue = string.IsNullOrWhiteSpace(local.GameIconUrl) ? null : local.GameIconUrl,
            ExternalValue = string.IsNullOrWhiteSpace(external.GameIconUrl) ? null : external.GameIconUrl,
        };

    private static CatalogSyncFieldDiffItem BuildValueDiff(string field, GameInfo local, GameInfo external)
    {
        var label = FieldLabels.TryGetValue(field, out var displayLabel) ? displayLabel : field;
        return new CatalogSyncFieldDiffItem
        {
            FieldLabel = label,
            Kind = CatalogSyncFieldDiffKind.ValueChange,
            LocalValue = FormatFieldValue(field, local) is { Length: > 0 } localValue ? localValue : null,
            ExternalValue = FormatFieldValue(field, external) is { Length: > 0 } externalValue ? externalValue : null,
        };
    }

    private static CatalogSyncFieldDiffItem BuildTagDiff(GameInfo local, GameInfo external)
    {
        var localTags = TagHelper.NormalizeTags(local.Tags);
        var externalTags = TagHelper.NormalizeTags(external.Tags);
        var allTags = localTags
            .Concat(externalTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);

        var tagDiffs = new List<CatalogSyncTagDiffItem>();
        foreach (var tag in allTags)
        {
            var inLocal = localTags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
            var inExternal = externalTags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
            var kind = inLocal && inExternal
                ? CatalogSyncTagDiffKind.Shared
                : inLocal
                    ? CatalogSyncTagDiffKind.LocalOnly
                    : CatalogSyncTagDiffKind.ExternalOnly;

            tagDiffs.Add(new CatalogSyncTagDiffItem { Tag = tag, Kind = kind });
        }

        return new CatalogSyncFieldDiffItem
        {
            FieldLabel = "Tags",
            Kind = CatalogSyncFieldDiffKind.Tags,
            TagDiffs = tagDiffs,
        };
    }

    private static string FormatFieldValue(string field, GameInfo app) =>
        field switch
        {
            "name" => app.Name ?? "",
            "project" => app.Project ?? "",
            "folderName" => app.FolderName ?? "",
            "installPath" => app.InstallPath ?? "",
            "appIconUrl" => app.GameIconUrl ?? "",
            "preferredVersion" => app.PreferredVersion ?? "",
            "tags" => TagHelper.FormatTagsForDisplay(app.Tags),
            "filesToAdd" => AppFilesToAddService.FormatForDisplay(app.FilesToAdd),
            "releaseAssetFilter" => RepositorySourceHelper.NormalizeReleaseAssetFilter(app.ReleaseAssetFilter) ?? "",
            "mods" => GameModsConfig.FormatForDisplay(app.ModsPath, app.ModsSources, app.ModsLayout),
            "repositorySource" => RepositorySourceHelper.DisplayName(app.RepositorySource),
            "repository" => string.IsNullOrWhiteSpace(app.Repository) ? "Manually managed" : app.Repository,
            _ => "",
        };
}
