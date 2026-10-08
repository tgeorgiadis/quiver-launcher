using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

/// <summary>What the catalog last set on a library app: its name, project, icon and tags. Saved in apps.json.</summary>
public sealed record CatalogSnapshot(string? Name, string? Project, string? IconUrl, IReadOnlyList<string> Tags);

/// <summary>
/// Keeps library apps that came from the quiverlauncher.com catalog looking like the catalog: name, project, icon and
/// tags. A field follows the catalog only while it still holds what the catalog last set, so anything the player changed
/// stays theirs. Their display name and custom cover image are separate fields and never change here. Tags the catalog
/// adds or drops are added or dropped; tags the player added or removed stay as they are.
/// </summary>
public static class CatalogLibrarySync
{
    /// <summary>Updates the app from its catalog entry; true when anything changed.</summary>
    public static bool Apply(GameInfo app, QuiverCatalogApp entry)
    {
        var last = app.CatalogSnapshot;
        // An icon borrowed from the game would show another app's art, so it isn't offered (as when adding an app).
        var next = new CatalogSnapshot(Text(entry.Name), Text(entry.ProjectName), entry.ArtworkFromGame ? null : Text(entry.Artwork),
            TagHelper.NormalizeTags(entry.Tags));
        var changed = false;
        // The entry's id links the app to it from now on (see GameInfo.CatalogEntryId).
        if (!string.IsNullOrWhiteSpace(entry.Id) && !string.Equals(app.CatalogEntryId, entry.Id, StringComparison.Ordinal))
        {
            app.CatalogEntryId = entry.Id;
            changed = true;
        }

        void Follow(string? current, string? before, string? value, Action<string> set)
        {
            // Never synced before: take the catalog's. Since then, only while the player hasn't changed it.
            if (value == null || (last != null && !Same(current, before)) || Same(current, value)) return;
            set(value);
            changed = true;
        }
        Follow(app.Name, last?.Name, next.Name, v => app.Name = v);
        Follow(app.Project, last?.Project, next.Project, v => app.Project = v);
        Follow(app.GameIconUrl, last?.IconUrl, next.IconUrl, v => app.GameIconUrl = v);

        var dropped = (last?.Tags ?? []).Where(t => !next.Tags.Contains(t, StringComparer.OrdinalIgnoreCase));
        var added = next.Tags.Where(t => !(last?.Tags ?? []).Contains(t, StringComparer.OrdinalIgnoreCase));
        var tags = app.Tags.Where(t => !dropped.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Concat(added.Where(t => !app.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))).ToList();
        if (!tags.SequenceEqual(app.Tags, StringComparer.OrdinalIgnoreCase))
        {
            app.Tags = tags;
            changed = true;
        }

        if (last == null || !Same(last.Name, next.Name) || !Same(last.Project, next.Project) || !Same(last.IconUrl, next.IconUrl) ||
            !last.Tags.SequenceEqual(next.Tags, StringComparer.OrdinalIgnoreCase))
        {
            app.CatalogSnapshot = next;
            changed = true;
        }
        return changed;
    }

    /// <summary>Copies what <see cref="Apply"/> may change, from the app shown to the copy about to be saved.</summary>
    public static void CopyTo(GameInfo from, GameInfo to)
    {
        to.Name = from.Name;
        to.Project = from.Project;
        to.GameIconUrl = from.GameIconUrl;
        to.Tags = [.. from.Tags];
        to.CatalogSnapshot = from.CatalogSnapshot;
        to.CatalogEntryId = from.CatalogEntryId;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Same(string? a, string? b) => string.Equals(Text(a), Text(b), StringComparison.Ordinal);
}
