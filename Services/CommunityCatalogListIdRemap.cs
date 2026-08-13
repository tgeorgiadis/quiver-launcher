namespace QuiverLauncher.Services;

/// <summary>
/// Maps legacy community list source IDs (platform+type lists) onto platform-oriented list IDs.
/// </summary>
public static class CommunityCatalogListIdRemap
{
    /// <summary>Nintendo 64 consolidated platform list.</summary>
    public const string Nintendo64ListId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    /// <summary>SNES consolidated platform list.</summary>
    public const string SnesListId = "b2c3d4e5-f6a7-8901-bcde-f12345678901";

    /// <summary>GBA consolidated platform list.</summary>
    public const string GbaListId = "c3d4e5f6-a7b8-9012-cdef-123456789012";

    /// <summary>GameCube consolidated platform list.</summary>
    public const string GcnListId = "d4e5f6a7-b8c9-0123-def0-234567890123";

    /// <summary>PlayStation consolidated platform list.</summary>
    public const string PsxListId = "e5f6a7b8-c9d0-1234-ef01-345678901234";

    /// <summary>Xbox 360 consolidated platform list.</summary>
    public const string X360ListId = "f6a7b8c9-d0e1-2345-f012-456789012345";

    /// <summary>Multiplatform / recreations bucket.</summary>
    public const string MultiplatformListId = "a7b8c9d0-e1f2-3456-0123-567890123456";

    private static readonly Dictionary<string, string> OldIdToNewId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // N64-Recomps, N64-Decomps-HarbourMasters, N64-Decomps → Nintendo 64
            ["b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c"] = Nintendo64ListId,
            ["c5f9d3b2-4a6e-5f0c-9d8b-2e3f4a5b6c7d"] = Nintendo64ListId,
            ["e7b1f5d4-6c8a-4e2b-9f0d-3a4b5c6d7e8f"] = Nintendo64ListId,
            // SNES-Decomps → SNES
            ["f8c2a6e5-7d9b-4f3c-a0e1-4b5c6d7e8f9a"] = SnesListId,
            // GBA-Decomps → GBA
            ["a9d3b7f6-8e0c-4a4d-b1f2-5c6d7e8f9a0b"] = GbaListId,
            // GCN-Decomps → GameCube
            ["b0e4c8a7-9f1d-4b5e-c2a3-6d7e8f9a0b1c"] = GcnListId,
            // PSX-Decomps → PlayStation
            ["c1f5d9b8-a0e2-4c6f-d3b4-7e8f9a0b1c2d"] = PsxListId,
            // X360-Recomps → Xbox 360
            ["d2a6e0c9-b1f3-4d7a-e4c5-8f9a0b1c2d3e"] = X360ListId,
            // General-Game-Recreations → Multiplatform
            ["e3b7f1da-c2a4-4e8b-f5d6-9a0b1c2d3e4f"] = MultiplatformListId,
        };

    public static bool TryGetNewListId(string? oldListId, out string newListId)
    {
        newListId = "";
        if (string.IsNullOrWhiteSpace(oldListId))
            return false;

        if (!OldIdToNewId.TryGetValue(oldListId.Trim(), out var mapped) ||
            string.IsNullOrWhiteSpace(mapped))
        {
            return false;
        }

        newListId = mapped;
        return true;
    }

    /// <summary>
    /// Rewrites legacy community source IDs onto platform list IDs present in <paramref name="index"/>.
    /// Merges duplicate legacy sources that map to the same new ID.
    /// Returns true if settings were modified.
    /// </summary>
    public static bool RemapLegacySourceIds(AppSettings settings, CommunityCatalogIndex? index)
    {
        settings.EnsureInitialized();
        if (index == null || index.Lists.Count == 0)
            return false;

        var indexIds = index.Lists
            .Where(l => !string.IsNullOrWhiteSpace(l.Id))
            .Select(l => l.Id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        var sources = settings.AppCatalogSources;

        // Snapshot because we may remove entries while iterating.
        foreach (var source in sources.ToList())
        {
            if (!source.IsCommunityManaged)
                continue;

            if (!TryGetNewListId(source.Id, out var newId))
                continue;

            // Only remap when the destination list exists in the current index.
            if (!indexIds.Contains(newId))
                continue;

            var existingTarget = sources.FirstOrDefault(s =>
                !ReferenceEquals(s, source) &&
                string.Equals(s.Id, newId, StringComparison.OrdinalIgnoreCase));

            if (existingTarget != null)
            {
                MergeSourceState(existingTarget, source);
                sources.Remove(source);
                changed = true;
                continue;
            }

            source.Id = newId;
            changed = true;
        }

        return changed;
    }

    private static void MergeSourceState(AppCatalogSource target, AppCatalogSource legacy)
    {
        if (legacy.Enabled)
            target.Enabled = true;

        target.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        legacy.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (repo, version) in legacy.IgnoredChangesAtVersion)
        {
            if (!target.IgnoredChangesAtVersion.ContainsKey(repo))
                target.IgnoredChangesAtVersion[repo] = version;
        }

        target.HiddenFromReviewRepositories ??= [];
        legacy.HiddenFromReviewRepositories ??= [];
        foreach (var repo in legacy.HiddenFromReviewRepositories)
        {
            if (!target.HiddenFromReviewRepositories.Any(r =>
                    r.Equals(repo, StringComparison.OrdinalIgnoreCase)))
            {
                target.HiddenFromReviewRepositories.Add(repo);
            }
        }
    }
}
