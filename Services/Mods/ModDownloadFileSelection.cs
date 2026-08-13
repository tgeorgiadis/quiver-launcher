namespace QuiverLauncher.Services.Mods;

/// <summary>Helpers for independent per-file GameBanana installs.</summary>
public static class ModDownloadFileSelection
{
    public static HashSet<string> GetPreselectedFileIds(
        IReadOnlyList<InstalledModRecord> installedRecords,
        string? preferredFileId)
    {
        ArgumentNullException.ThrowIfNull(installedRecords);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in installedRecords)
        {
            if (!string.IsNullOrWhiteSpace(record.DownloadFileId))
                ids.Add(record.DownloadFileId);
        }

        if (!string.IsNullOrWhiteSpace(preferredFileId))
            ids.Add(preferredFileId);

        return ids;
    }

    public static bool IsFileInstalled(ModDownloadFile file, IReadOnlyList<InstalledModRecord> installedRecords)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(installedRecords);

        foreach (var record in installedRecords)
        {
            if (!string.IsNullOrWhiteSpace(record.DownloadFileId) &&
                string.Equals(record.DownloadFileId, file.Id, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(record.DownloadFileName) &&
                string.Equals(record.DownloadFileName, file.FileName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Catalog files that are not yet installed for this package.</summary>
    public static IReadOnlyList<ModDownloadFile> GetUninstalledFiles(
        IReadOnlyList<ModDownloadFile> files,
        IReadOnlyList<InstalledModRecord> installedRecords)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(installedRecords);
        return files.Where(file => !IsFileInstalled(file, installedRecords)).ToList();
    }

    public static bool HasFileIdentity(InstalledModRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !string.IsNullOrWhiteSpace(record.DownloadFileId) ||
               !string.IsNullOrWhiteSpace(record.DownloadFileName);
    }

    public static ModDownloadFile? FindCatalogFile(ModPackage package, InstalledModRecord record)
    {
        ArgumentNullException.ThrowIfNull(package);
        return FindCatalogFile(package.DownloadFiles, record);
    }

    public static ModDownloadFile? FindCatalogFile(
        IReadOnlyList<ModDownloadFile> files,
        InstalledModRecord record)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(record);

        if (files.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(record.DownloadFileId))
        {
            var byId = files.FirstOrDefault(f =>
                string.Equals(f.Id, record.DownloadFileId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(record.DownloadFileName))
        {
            var byName = files.FirstOrDefault(f =>
                string.Equals(f.FileName, record.DownloadFileName, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return byName;
        }

        return files.Count == 1 ? files[0] : null;
    }

    /// <summary>
    /// Catalog files that should be re-downloaded when updating already-installed files.
    /// Only includes files that are actually behind their catalog version.
    /// </summary>
    public static IReadOnlyList<ModDownloadFile> ResolveFilesToUpdate(
        ModPackage package,
        IReadOnlyList<InstalledModRecord> records)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(records);

        var files = new List<ModDownloadFile>();
        foreach (var record in records)
        {
            if (!IsRecordUpdateAvailable(record, package))
                continue;

            var file = FindCatalogFile(package, record);
            if (file != null)
                files.Add(file);
        }

        return files;
    }

    /// <summary>Picker rows for uninstalling a subset of installed files.</summary>
    public static IReadOnlyList<ModDownloadFile> ResolveInstalledFilesForPicker(
        ModPackage package,
        IReadOnlyList<InstalledModRecord> records)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(records);

        var files = new List<ModDownloadFile>();
        foreach (var record in records)
        {
            var catalogFile = FindCatalogFile(package, record);
            if (catalogFile != null)
            {
                files.Add(catalogFile);
                continue;
            }

            files.Add(new ModDownloadFile
            {
                Id = record.DownloadFileId ?? string.Empty,
                FileName = string.IsNullOrWhiteSpace(record.DownloadFileName)
                    ? record.Name
                    : record.DownloadFileName,
                DownloadUrl = string.Empty,
                Version = record.Version,
                Description = "Installed",
            });
        }

        return files;
    }

    /// <summary>
    /// Latest version to compare against for an installed record.
    /// Per-file installs use that file's catalog version; never the mod-page version
    /// when the record has a DownloadFileId/Name (avoids false updates when one file
    /// stays at v1 while the page is v2).
    /// </summary>
    public static string? ResolveLatestVersion(InstalledModRecord record, ModPackage package) =>
        ResolveLatestVersion(record, package, package.DownloadFiles);

    public static string? ResolveLatestVersion(
        InstalledModRecord record,
        ModPackage package,
        IReadOnlyList<ModDownloadFile> downloadFiles)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(downloadFiles);

        var file = FindCatalogFile(downloadFiles, record);
        if (file != null && !string.IsNullOrWhiteSpace(file.Version))
            return file.Version;

        // Per-file identity without a matched catalog row (empty list or unmatched):
        // do not fall back to mod-page _sVersion — that causes false Update buttons.
        if (HasFileIdentity(record))
            return record.Version;

        return package.LatestVersion?.Version;
    }

    public static bool IsRecordUpdateAvailable(InstalledModRecord record, ModPackage package) =>
        IsRecordUpdateAvailable(record, package, package.DownloadFiles);

    public static bool IsRecordUpdateAvailable(
        InstalledModRecord record,
        ModPackage package,
        IReadOnlyList<ModDownloadFile> downloadFiles)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(downloadFiles);
        return ModVersionComparer.IsUpdateAvailable(
            record.Version,
            ResolveLatestVersion(record, package, downloadFiles));
    }
}
