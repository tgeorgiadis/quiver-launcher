using QuiverLauncher.Services.Mods.Providers.Thunderstore;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace QuiverLauncher.Services.Mods;

public sealed class ModInstallService
{
    private readonly ModProviderRegistry _registry;
    private readonly InstalledModsStore _store = new();

    public ModInstallService(ModProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string GetModsDirectory(string installRoot, string modsPath)
    {
        var normalized = GameModsConfig.NormalizePath(modsPath);
        if (normalized.Length == 0)
            throw new InvalidOperationException("Mods path is not configured.");

        var parts = normalized.Split('/');
        return Path.Combine(new[] { installRoot }.Concat(parts).ToArray());
    }

    public async Task<InstalledModRecord> InstallAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IModProvider provider,
        ModDownloadFile? selectedFile = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(provider);

        if (selectedFile != null)
            package = Providers.GameBanana.GameBananaModProvider.WithSelectedFile(package, selectedFile);

        if (package.LatestVersion == null || string.IsNullOrWhiteSpace(package.LatestVersion.DownloadUrl))
            throw new InvalidOperationException($"Package '{package.FullName}' has no downloadable version.");

        // Replace only this file's previous install; sibling files for the same package stay.
        UninstallMatchingFile(installRoot, modsPath, package, selectedFile?.Id);

        await using var archiveStream = await provider
            .DownloadAsync(package.LatestVersion, progress, cancellationToken)
            .ConfigureAwait(false);

        var modsDir = GetModsDirectory(installRoot, modsPath);
        Directory.CreateDirectory(modsDir);

        var wrapFolderName = GameModsConfig.IsFolderPerMod(modsLayout)
            ? GameModsConfig.ResolveWrapFolderName(selectedFile?.FileName, package.Name, package.Id)
            : null;

        var installedFiles = ExtractPayloadFiles(
            archiveStream,
            modsDir,
            provider.GetArchiveMetadataFileNames(),
            modsLayout,
            wrapFolderName);

        var record = new InstalledModRecord
        {
            Provider = package.ProviderId,
            SourceKey = package.SourceKey,
            Id = package.Id,
            FullName = package.FullName,
            Owner = package.Owner,
            Name = package.Name,
            Version = package.LatestVersion.Version,
            DownloadFileId = selectedFile?.Id,
            DownloadFileName = selectedFile?.FileName,
            Files = installedFiles,
        };

        var document = _store.Load(installRoot);
        document.Mods.RemoveAll(m =>
            ModCatalogListBuilder.RecordMatchesPackageFile(m, package, selectedFile?.Id));
        document.Mods.Add(record);
        _store.Save(installRoot, document);

        return record;
    }

    /// <summary>
    /// Direct Thunderstore requirements that are not already in the install sidecar,
    /// using catalog names when available.
    /// </summary>
    public IReadOnlyList<string> ListMissingDirectDependencies(
        InstalledModsDocument document,
        ModPackage package,
        IReadOnlyList<ModPackage>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(package);

        var deps = package.LatestVersion?.Dependencies ?? [];
        if (deps.Count == 0)
            return [];

        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in deps)
        {
            if (!ThunderstoreModProvider.TryParseDependencyString(dep, out var depFullName, out _))
                continue;

            if (!seen.Add(depFullName))
                continue;

            if (_store.FindByFullName(document, package.ProviderId, package.SourceKey, depFullName) != null)
                continue;

            missing.Add(ResolveDependencyDisplayName(package, depFullName, catalog));
        }

        return missing;
    }

    static string ResolveDependencyDisplayName(
        ModPackage parent,
        string depFullName,
        IReadOnlyList<ModPackage>? catalog)
    {
        var catalogMatch = catalog?.FirstOrDefault(p =>
            string.Equals(p.ProviderId, parent.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.SourceKey, parent.SourceKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.FullName, depFullName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(catalogMatch?.Name))
            return catalogMatch.Name;

        if (ThunderstoreModProvider.TrySplitPackageFullName(depFullName, out _, out var name) &&
            !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return depFullName;
    }

    public async Task InstallWithDependenciesAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IReadOnlyList<ModPackage> catalog,
        IModProvider provider,
        ModDownloadFile? selectedFile = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null,
        bool installDependencies = true)
    {
        var document = _store.Load(installRoot);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await InstallRecursiveAsync(
            installRoot,
            modsPath,
            package,
            catalog,
            provider,
            document,
            visited,
            selectedFile,
            progress,
            cancellationToken,
            modsLayout,
            installDependencies).ConfigureAwait(false);
    }

    /// <summary>
    /// Installs each selected download file as its own sidecar record.
    /// Null or empty <paramref name="selectedFiles"/> keeps the single-file (Thunderstore) path.
    /// </summary>
    public async Task InstallSelectedFilesAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IReadOnlyList<ModPackage> catalog,
        IModProvider provider,
        IReadOnlyList<ModDownloadFile>? selectedFiles,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null,
        bool installDependencies = true)
    {
        if (selectedFiles == null || selectedFiles.Count == 0)
        {
            await InstallWithDependenciesAsync(
                    installRoot,
                    modsPath,
                    package,
                    catalog,
                    provider,
                    selectedFile: null,
                    progress,
                    cancellationToken,
                    modsLayout,
                    installDependencies)
                .ConfigureAwait(false);
            return;
        }

        foreach (var file in selectedFiles)
        {
            await InstallWithDependenciesAsync(
                    installRoot,
                    modsPath,
                    package,
                    catalog,
                    provider,
                    file,
                    progress,
                    cancellationToken,
                    modsLayout,
                    installDependencies)
                .ConfigureAwait(false);
        }
    }

    private async Task InstallRecursiveAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IReadOnlyList<ModPackage> catalog,
        IModProvider provider,
        InstalledModsDocument document,
        HashSet<string> visited,
        ModDownloadFile? selectedFile,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        string? modsLayout,
        bool installDependencies)
    {
        if (!visited.Add(package.FullName))
            return;

        package = await EnsureDownloadableAsync(package, provider, cancellationToken).ConfigureAwait(false);

        var deps = installDependencies
            ? package.LatestVersion?.Dependencies ?? []
            : [];
        foreach (var dep in deps)
        {
            if (!ThunderstoreModProvider.TryParseDependencyString(dep, out var depFullName, out _))
                continue;

            if (_store.FindByFullName(document, package.ProviderId, package.SourceKey, depFullName) != null)
                continue;

            var depPackage = catalog.FirstOrDefault(p =>
                string.Equals(p.ProviderId, package.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.SourceKey, package.SourceKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.FullName, depFullName, StringComparison.OrdinalIgnoreCase));

            if (depPackage == null)
            {
                depPackage = CreateThunderstoreDependencyStub(package, depFullName)
                    ?? throw new InvalidOperationException(
                        $"Dependency '{depFullName}' was not found in the mod catalog and could not be resolved.");
            }

            depPackage = await EnsureDownloadableAsync(depPackage, provider, cancellationToken)
                .ConfigureAwait(false);

            await InstallRecursiveAsync(
                installRoot,
                modsPath,
                depPackage,
                catalog,
                provider,
                document,
                visited,
                selectedFile: null,
                progress,
                cancellationToken,
                modsLayout,
                installDependencies: true).ConfigureAwait(false);

            document = _store.Load(installRoot);
        }

        var targetVersion = !string.IsNullOrWhiteSpace(selectedFile?.Version)
            ? selectedFile.Version
            : package.LatestVersion?.Version;
        if (ModCatalogListBuilder.FindMatchingRecord(document, package, selectedFile?.Id) is { } existing &&
            string.Equals(existing.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Only apply the selected file to the root package being installed, not dependencies.
        var fileForThis = selectedFile;
        await InstallAsync(
                installRoot,
                modsPath,
                package,
                provider,
                fileForThis,
                progress,
                cancellationToken,
                modsLayout)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Listing stubs have empty download URLs; enrich Thunderstore packages via experimental API.
    /// </summary>
    private static async Task<ModPackage> EnsureDownloadableAsync(
        ModPackage package,
        IModProvider provider,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(package.LatestVersion?.DownloadUrl))
            return package;

        if (provider is not ThunderstoreModProvider thunderstore)
            return package;

        var enriched = await thunderstore
            .EnrichForInstallAsync(package, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(enriched.LatestVersion?.DownloadUrl))
        {
            throw new InvalidOperationException(
                $"Package '{package.FullName}' could not be resolved for download.");
        }

        return enriched;
    }

    internal static ModPackage? CreateThunderstoreDependencyStub(ModPackage parent, string depFullName)
    {
        if (!string.Equals(parent.ProviderId, ModProviderIds.Thunderstore, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!ThunderstoreModProvider.TrySplitPackageFullName(depFullName, out var owner, out var name))
            return null;

        return new ModPackage
        {
            ProviderId = parent.ProviderId,
            SourceKey = parent.SourceKey,
            SourceDisplayLabel = parent.SourceDisplayLabel,
            Id = depFullName,
            Owner = owner,
            Name = name,
            FullName = depFullName,
            PackagePageUrl = ThunderstoreModProvider.BuildPackagePageUrl(parent.SourceKey, owner, name),
            LatestVersion = new ModPackageVersion
            {
                Version = string.Empty,
                DownloadUrl = string.Empty,
            },
        };
    }

    public bool Uninstall(string installRoot, string modsPath, string providerId, string packageId)
    {
        var document = _store.Load(installRoot);
        var records = document.Mods.Where(m =>
            string.Equals(m.Provider, providerId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(m.Id, packageId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(m.FullName, packageId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (records.Count == 0)
            return false;

        return UninstallRecords(installRoot, modsPath, document, records);
    }

    public bool UninstallMatching(string installRoot, string modsPath, ModPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var document = _store.Load(installRoot);
        var records = ModCatalogListBuilder.FindMatchingRecords(document, package);
        if (records.Count == 0)
            return false;

        return UninstallRecords(installRoot, modsPath, document, records);
    }

    public bool UninstallMatchingFile(
        string installRoot,
        string modsPath,
        ModPackage package,
        string? downloadFileId)
    {
        ArgumentNullException.ThrowIfNull(package);
        var document = _store.Load(installRoot);
        var record = ModCatalogListBuilder.FindMatchingRecord(document, package, downloadFileId);
        if (record == null)
            return false;

        return UninstallRecords(installRoot, modsPath, document, [record]);
    }

    private bool UninstallRecords(
        string installRoot,
        string modsPath,
        InstalledModsDocument document,
        IReadOnlyList<InstalledModRecord> records)
    {
        if (records.Count == 0)
            return false;

        var modsDir = GetModsDirectory(installRoot, modsPath);
        var modsRootFull = Path.GetFullPath(modsDir);
        foreach (var record in records)
        {
            foreach (var relative in record.Files)
            {
                var fullPath = Path.GetFullPath(Path.Combine(modsDir, relative));
                if (!fullPath.StartsWith(modsRootFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }

        foreach (var record in records)
            document.Mods.RemoveAll(m => ReferenceEquals(m, record));
        _store.Save(installRoot, document);
        return true;
    }

    public async Task UpdateAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IModProvider provider,
        ModDownloadFile? selectedFile = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null)
    {
        await InstallAsync(
                installRoot,
                modsPath,
                package,
                provider,
                selectedFile,
                progress,
                cancellationToken,
                modsLayout)
            .ConfigureAwait(false);
    }

    public InstalledModsDocument LoadInstalled(string installRoot) => _store.Load(installRoot);

    /// <summary>
    /// Extracts non-metadata entries from a zip, 7z, or rar archive into <paramref name="modsDir"/>.
    /// Returns relative paths that were written (forward-slash normalized).
    /// When <paramref name="modsLayout"/> is folderPerMod and the archive has root-level payload
    /// files, all payload paths are prefixed with <paramref name="wrapFolderName"/>.
    /// </summary>
    public static List<string> ExtractPayloadFiles(
        Stream archiveStream,
        string modsDir,
        IReadOnlySet<string> metadataFileNames,
        string? modsLayout = null,
        string? wrapFolderName = null)
    {
        Directory.CreateDirectory(modsDir);
        var installed = new List<string>();
        var modsRootFull = Path.GetFullPath(modsDir);

        using var archive = ArchiveFactory.OpenArchive(archiveStream);
        var payloadRelatives = new List<string>();

        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
                continue;

            var key = entry.Key ?? string.Empty;
            var relative = key.Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0)
                continue;

            // Skip host metadata files only at the package root.
            if (!relative.Contains('/') && metadataFileNames.Contains(relative))
                continue;

            payloadRelatives.Add(relative);
        }

        var shouldWrap = GameModsConfig.IsFolderPerMod(modsLayout) &&
                         payloadRelatives.Any(relative => !relative.Contains('/'));
        var prefix = shouldWrap
            ? GameModsConfig.SanitizeFolderName(wrapFolderName)
            : string.Empty;
        if (shouldWrap && prefix.Length == 0)
            prefix = "mod";

        var payloadSet = new HashSet<string>(payloadRelatives, StringComparer.OrdinalIgnoreCase);
        var extractOptions = new ExtractionOptions
        {
            Overwrite = true,
            ExtractFullPath = false,
        };

        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory)
                    continue;

                if (!TryWriteModPayloadEntry(
                        reader.Entry.Key,
                        payloadSet,
                        prefix,
                        modsDir,
                        modsRootFull,
                        destination => reader.WriteEntryToFile(destination, extractOptions),
                        installed))
                {
                    continue;
                }
            }
        }
        else
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory)
                    continue;

                if (!TryWriteModPayloadEntry(
                        entry.Key,
                        payloadSet,
                        prefix,
                        modsDir,
                        modsRootFull,
                        destination => entry.WriteToFile(destination, extractOptions),
                        installed))
                {
                    continue;
                }
            }
        }

        return installed;
    }

    static bool TryWriteModPayloadEntry(
        string? entryKey,
        HashSet<string> payloadSet,
        string prefix,
        string modsDir,
        string modsRootFull,
        Action<string> write,
        List<string> installed)
    {
        var relative = (entryKey ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || !payloadSet.Contains(relative))
            return false;

        var destRelative = prefix.Length > 0 ? $"{prefix}/{relative}" : relative;

        var destination = Path.GetFullPath(
            Path.Combine(modsDir, destRelative.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(modsRootFull, StringComparison.OrdinalIgnoreCase))
            return false;

        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        write(destination);
        installed.Add(destRelative);
        return true;
    }
}
