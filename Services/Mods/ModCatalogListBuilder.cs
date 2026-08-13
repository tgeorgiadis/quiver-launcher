namespace QuiverLauncher.Services.Mods;

/// <summary>How unmatched installed sidecar records are included in the list.</summary>
public enum ModOrphanInstallMode
{
    /// <summary>Append unmatched installs (enriched from known packages when available).</summary>
    Include,

    /// <summary>Do not append unmatched installs (remote search results only).</summary>
    Exclude,
}

/// <summary>
/// Builds browse/installed list rows from a remote catalog plus the local sidecar.
/// Dedupes when catalog package Ids changed (e.g. Thunderstore UUID → Owner-Name).
/// </summary>
public static class ModCatalogListBuilder
{
    public static bool RecordMatchesPackage(InstalledModRecord record, ModPackage package)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(package);

        if (!string.Equals(record.Provider, package.ProviderId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(record.Id, package.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(record.FullName) &&
            !string.IsNullOrWhiteSpace(package.FullName) &&
            string.Equals(record.FullName, package.FullName, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(record.Owner) &&
               !string.IsNullOrWhiteSpace(record.Name) &&
               string.Equals(record.Owner, package.Owner, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(record.Name, package.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <paramref name="record"/> is this package's install of <paramref name="downloadFileId"/>.
    /// A null/empty file id matches legacy records that have no <see cref="InstalledModRecord.DownloadFileId"/>.
    /// </summary>
    public static bool RecordMatchesPackageFile(
        InstalledModRecord record,
        ModPackage package,
        string? downloadFileId)
    {
        if (!RecordMatchesPackage(record, package))
            return false;

        if (string.IsNullOrWhiteSpace(downloadFileId))
            return string.IsNullOrWhiteSpace(record.DownloadFileId);

        return string.Equals(record.DownloadFileId, downloadFileId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when two catalog packages refer to the same mod (Id, FullName, or Owner+Name).</summary>
    public static bool PackagesMatch(ModPackage left, ModPackage right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!string.Equals(left.ProviderId, right.ProviderId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(left.Id) &&
            !string.IsNullOrWhiteSpace(right.Id) &&
            string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(left.FullName) &&
            !string.IsNullOrWhiteSpace(right.FullName) &&
            string.Equals(left.FullName, right.FullName, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(left.Owner) &&
               !string.IsNullOrWhiteSpace(left.Name) &&
               !string.IsNullOrWhiteSpace(right.Owner) &&
               !string.IsNullOrWhiteSpace(right.Name) &&
               string.Equals(left.Owner, right.Owner, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Index of <paramref name="package"/> in <paramref name="rows"/>, or <paramref name="fallbackIndex"/>
    /// clamped into range when no match (or 0 when the list is non-empty and fallback is invalid).
    /// </summary>
    public static int FindListIndexByPackage(
        IReadOnlyList<ModListItem> rows,
        ModPackage? package,
        int fallbackIndex)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return -1;

        if (package != null)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (PackagesMatch(rows[i].Package, package))
                    return i;
            }
        }

        if (fallbackIndex < 0)
            return 0;
        if (fallbackIndex >= rows.Count)
            return rows.Count - 1;
        return fallbackIndex;
    }

    public static InstalledModRecord? FindMatchingRecord(InstalledModsDocument doc, ModPackage package)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(package);
        return doc.Mods.FirstOrDefault(m => RecordMatchesPackage(m, package));
    }

    public static InstalledModRecord? FindMatchingRecord(
        InstalledModsDocument doc,
        ModPackage package,
        string? downloadFileId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(package);
        return doc.Mods.FirstOrDefault(m => RecordMatchesPackageFile(m, package, downloadFileId));
    }

    public static IReadOnlyList<InstalledModRecord> FindMatchingRecords(
        InstalledModsDocument doc,
        ModPackage package)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(package);
        return doc.Mods.Where(m => RecordMatchesPackage(m, package)).ToList();
    }

    public static string PackageIdKey(string providerId, string id) =>
        $"{providerId}|id|{id}";

    public static string PackageFullNameKey(string providerId, string fullName) =>
        $"{providerId}|full|{fullName}";

    public static string PackageOwnerNameKey(string providerId, string owner, string name) =>
        $"{providerId}|ownername|{owner}|{name}";

    /// <summary>
    /// Primary identity for catalog dedupe: Id, else FullName, else Owner+Name.
    /// </summary>
    public static string? PackageIdentityKey(ModPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(package.ProviderId))
            return null;

        if (!string.IsNullOrWhiteSpace(package.Id))
            return PackageIdKey(package.ProviderId, package.Id);

        if (!string.IsNullOrWhiteSpace(package.FullName))
            return PackageFullNameKey(package.ProviderId, package.FullName);

        if (!string.IsNullOrWhiteSpace(package.Owner) && !string.IsNullOrWhiteSpace(package.Name))
            return PackageOwnerNameKey(package.ProviderId, package.Owner, package.Name);

        return null;
    }

    /// <summary>All lookup keys that identify this package (Id, FullName, Owner+Name).</summary>
    public static IEnumerable<string> PackageIdentityKeys(ModPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(package.ProviderId))
            yield break;

        if (!string.IsNullOrWhiteSpace(package.Id))
            yield return PackageIdKey(package.ProviderId, package.Id);

        if (!string.IsNullOrWhiteSpace(package.FullName))
            yield return PackageFullNameKey(package.ProviderId, package.FullName);

        if (!string.IsNullOrWhiteSpace(package.Owner) && !string.IsNullOrWhiteSpace(package.Name))
            yield return PackageOwnerNameKey(package.ProviderId, package.Owner, package.Name);
    }

    /// <summary>
    /// Appends <paramref name="incoming"/> onto <paramref name="packages"/>, skipping packages
    /// that share any identity key (Id / FullName / Owner+Name) with an existing entry.
    /// </summary>
    public static void AppendUniquePackages(List<ModPackage> packages, IEnumerable<ModPackage> incoming)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(incoming);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in packages)
        {
            foreach (var key in PackageIdentityKeys(existing))
                seen.Add(key);
        }

        foreach (var package in incoming)
        {
            var keys = PackageIdentityKeys(package).ToList();
            if (keys.Count > 0 && keys.Any(seen.Contains))
                continue;

            packages.Add(package);
            foreach (var key in keys)
                seen.Add(key);
        }
    }

    /// <summary>Indexes packages by provider+id, fullName, and owner+name for orphan enrichment.</summary>
    public static void RememberPackages(
        IDictionary<string, ModPackage> known,
        IEnumerable<ModPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(packages);

        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.ProviderId))
                continue;

            if (!string.IsNullOrWhiteSpace(package.Id))
                known[PackageIdKey(package.ProviderId, package.Id)] = package;

            if (!string.IsNullOrWhiteSpace(package.FullName))
                known[PackageFullNameKey(package.ProviderId, package.FullName)] = package;

            if (!string.IsNullOrWhiteSpace(package.Owner) && !string.IsNullOrWhiteSpace(package.Name))
                known[PackageOwnerNameKey(package.ProviderId, package.Owner, package.Name)] = package;
        }
    }

    public static ModPackage? FindKnownPackage(
        IReadOnlyDictionary<string, ModPackage>? known,
        InstalledModRecord record)
    {
        if (known == null || known.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(record.Id) &&
            known.TryGetValue(PackageIdKey(record.Provider, record.Id), out var byId))
            return byId;

        if (!string.IsNullOrWhiteSpace(record.FullName) &&
            known.TryGetValue(PackageFullNameKey(record.Provider, record.FullName), out var byFull))
            return byFull;

        if (!string.IsNullOrWhiteSpace(record.Owner) &&
            !string.IsNullOrWhiteSpace(record.Name) &&
            known.TryGetValue(PackageOwnerNameKey(record.Provider, record.Owner, record.Name), out var byOwnerName))
            return byOwnerName;

        return null;
    }

    /// <summary>
    /// Builds list items for the catalog. Orphan installs (not in catalog) get stub cards
    /// when <paramref name="orphanMode"/> is <see cref="ModOrphanInstallMode.Include"/>,
    /// enriched from <paramref name="knownPackages"/> when available.
    /// When a sidecar Id differs from the matched catalog package Id, updates the record in-place
    /// and sets <paramref name="sidecarIdsMigrated"/> so the caller can persist.
    /// </summary>
    public static IReadOnlyList<ModListItem> BuildItems(
        IEnumerable<ModPackage> catalog,
        InstalledModsDocument installedDoc,
        out bool sidecarIdsMigrated,
        ModOrphanInstallMode orphanMode = ModOrphanInstallMode.Include,
        IReadOnlyDictionary<string, ModPackage>? knownPackages = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(installedDoc);

        sidecarIdsMigrated = false;
        var items = new List<ModListItem>();
        var matchedRecords = new HashSet<InstalledModRecord>();
        var seenCatalogKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in catalog.Where(p => !p.IsDeprecated))
        {
            var keys = PackageIdentityKeys(package).ToList();
            if (keys.Count > 0 && keys.Any(seenCatalogKeys.Contains))
                continue;

            foreach (var key in keys)
                seenCatalogKeys.Add(key);

            var records = FindMatchingRecords(installedDoc, package);
            foreach (var record in records)
            {
                matchedRecords.Add(record);
                if (!string.IsNullOrWhiteSpace(package.Id) &&
                    !string.Equals(record.Id, package.Id, StringComparison.OrdinalIgnoreCase))
                {
                    record.Id = package.Id;
                    sidecarIdsMigrated = true;
                }

                if (!string.IsNullOrWhiteSpace(package.FullName) &&
                    !string.Equals(record.FullName, package.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    record.FullName = package.FullName;
                    sidecarIdsMigrated = true;
                }
            }

            var item = new ModListItem { Package = package };
            item.ApplyInstalled(records);
            items.Add(item);
        }

        if (orphanMode == ModOrphanInstallMode.Exclude)
            return items;

        foreach (var record in installedDoc.Mods)
        {
            if (matchedRecords.Contains(record))
                continue;

            var package = CreateOrphanPackage(record, FindKnownPackage(knownPackages, record));
            var item = new ModListItem { Package = package };
            item.ApplyInstalled(record);
            items.Add(item);
        }

        return items;
    }

    internal static ModPackage CreateOrphanPackage(InstalledModRecord record, ModPackage? known)
    {
        if (known == null)
        {
            return new ModPackage
            {
                ProviderId = record.Provider,
                SourceKey = record.SourceKey,
                Id = record.Id,
                Owner = record.Owner,
                Name = record.Name,
                FullName = record.FullName,
                LatestVersion = new ModPackageVersion
                {
                    Version = record.Version,
                    DownloadUrl = string.Empty,
                },
            };
        }

        var version = known.LatestVersion;
        return new ModPackage
        {
            ProviderId = known.ProviderId,
            SourceKey = string.IsNullOrWhiteSpace(known.SourceKey) ? record.SourceKey : known.SourceKey,
            SourceDisplayLabel = known.SourceDisplayLabel,
            Id = string.IsNullOrWhiteSpace(known.Id) ? record.Id : known.Id,
            Owner = string.IsNullOrWhiteSpace(known.Owner) ? record.Owner : known.Owner,
            Name = string.IsNullOrWhiteSpace(known.Name) ? record.Name : known.Name,
            FullName = string.IsNullOrWhiteSpace(known.FullName) ? record.FullName : known.FullName,
            Description = known.Description,
            IconUrl = known.IconUrl,
            PackagePageUrl = known.PackagePageUrl,
            IsDeprecated = known.IsDeprecated,
            HasContentRating = known.HasContentRating,
            DownloadCount = known.DownloadCount,
            RatingScore = known.RatingScore,
            UpdatedAtUnix = known.UpdatedAtUnix,
            CreatedAtUnix = known.CreatedAtUnix,
            LatestVersion = version == null
                ? new ModPackageVersion { Version = record.Version, DownloadUrl = string.Empty }
                : new ModPackageVersion
                {
                    Version = string.IsNullOrWhiteSpace(version.Version) ? record.Version : version.Version,
                    DownloadUrl = version.DownloadUrl ?? string.Empty,
                    FileSize = version.FileSize,
                    Dependencies = version.Dependencies,
                },
            DownloadFiles = known.DownloadFiles,
        };
    }
}
