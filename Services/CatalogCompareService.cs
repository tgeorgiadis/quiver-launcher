using System.ComponentModel;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Services
{
    public enum CatalogSyncStatus
    {
        InLocalOnly,
        InExternalOnly,
        Unchanged,
        Changed,
    }

    public class CatalogSyncRowItem : INotifyPropertyChanged
    {
        private bool _isGamepadFocused;

        public event PropertyChangedEventHandler? PropertyChanged;

        public CatalogSyncStatus Status { get; init; }
        public string Repository { get; init; } = "";
        public string IdentityKey { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public GameInfo? Local { get; init; }
        public GameInfo? External { get; init; }
        public IReadOnlyList<string> ChangedFields { get; init; } = [];
        public IReadOnlyList<CatalogSyncFieldDiffItem> FieldDiffs { get; init; } = [];

        public bool HasInlineDiff => FieldDiffs.Count > 0;

        public string IconUrl =>
            External?.DefaultIconUrl
            ?? Local?.DefaultIconUrl
            ?? "/Assets/DefaultGame.png";

        public string StatusShortLabel => Status switch
        {
            CatalogSyncStatus.InLocalOnly => "Local only",
            CatalogSyncStatus.InExternalOnly => "New",
            CatalogSyncStatus.Unchanged => "Up to date",
            CatalogSyncStatus.Changed => "Changed",
            _ => "",
        };

        public string StatusLabel => Status switch
        {
            CatalogSyncStatus.InLocalOnly => "Local only",
            CatalogSyncStatus.InExternalOnly => "Not in library",
            CatalogSyncStatus.Unchanged => "Up to date",
            CatalogSyncStatus.Changed => "Changed",
            _ => "",
        };

        public bool CanAdd => Status == CatalogSyncStatus.InExternalOnly;
        public bool CanReplace => Status == CatalogSyncStatus.Changed;
        public bool CanMerge => Status == CatalogSyncStatus.Changed;
        public bool CanIgnore => Status is CatalogSyncStatus.Changed or CatalogSyncStatus.InExternalOnly;
        public bool CanRemoveFromLibrary => Local != null;

        public bool ShowHideButton { get; set; }
        public bool ShowUnhideButton { get; set; }

        public bool IsGamepadFocused
        {
            get => _isGamepadFocused;
            set
            {
                if (_isGamepadFocused == value)
                    return;

                _isGamepadFocused = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGamepadFocused)));
            }
        }
    }

    public static class CatalogCompareService
    {
        private static readonly string[] CompareFields =
        [
            "name",
            "project",
            "installPath",
            "appIconUrl",
            "preferredVersion",
            "tags",
            "filesToAdd",
            "mods",
            "repositorySource",
        ];

        public static IReadOnlyList<CatalogSyncRowItem> BuildCompareRows(
            List<GameInfo> localApps,
            List<GameInfo> externalApps)
        {
            var localByKey = localApps
                .Where(a => !string.IsNullOrWhiteSpace(a.Repository))
                .ToDictionary(a => a.IdentityKey, a => a, StringComparer.OrdinalIgnoreCase);

            var rows = new List<CatalogSyncRowItem>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var external in externalApps)
            {
                if (string.IsNullOrWhiteSpace(external.Repository))
                    continue;

                var key = external.IdentityKey;
                if (!seenKeys.Add(key))
                    continue;

                localByKey.TryGetValue(key, out var local);
                rows.Add(CreateCompareRow(external.Repository!, local, external));
            }

            return rows;
        }

        public static (int UsingCount, int TotalCount) ComputeLibraryUsageStats(
            List<GameInfo> localApps,
            List<GameInfo> externalApps)
        {
            var rows = BuildCompareRows(localApps, externalApps);
            return (rows.Count(r => r.Local != null), rows.Count);
        }

        private static CatalogSyncRowItem CreateCompareRow(string repo, GameInfo? local, GameInfo? external)
        {
            CatalogSyncStatus status;
            IReadOnlyList<string> changedFields = [];

            if (local != null && external == null)
                status = CatalogSyncStatus.InLocalOnly;
            else if (local == null && external != null)
                status = CatalogSyncStatus.InExternalOnly;
            else if (local != null && external != null &&
                     AppCatalogService.AreCatalogFieldsEquivalent(local, external))
                status = CatalogSyncStatus.Unchanged;
            else
            {
                status = CatalogSyncStatus.Changed;
                changedFields = GetChangedFields(local!, external!);
            }

            return new CatalogSyncRowItem
            {
                Status = status,
                Repository = repo,
                IdentityKey = (external ?? local)?.IdentityKey
                    ?? RepositorySourceHelper.GetIdentityKey(null, repo),
                // Catalog review keeps project in the title so ports of the same game stay distinct in a dense list.
                DisplayName = AppDisplayName.Resolve(
                    external?.Name ?? local?.Name,
                    external?.Project ?? local?.Project,
                    customDisplayName: null,
                    LibraryNameStyle.NameAndProjectInTitle) is { Length: > 0 } composed
                    ? composed
                    : repo,
                Local = local,
                External = external,
                ChangedFields = changedFields,
                FieldDiffs = CatalogSyncFieldDiffBuilder.BuildFieldDiffs(status, local, external, changedFields),
            };
        }

        public static IReadOnlyList<string> GetChangedFields(GameInfo local, GameInfo external)
        {
            var changed = new List<string>();

            if (!string.Equals(local.EffectiveRepositorySource, external.EffectiveRepositorySource, StringComparison.OrdinalIgnoreCase))
                changed.Add("repositorySource");
            if (!string.Equals(local.Name, external.Name, StringComparison.OrdinalIgnoreCase))
                changed.Add("name");
            if (!string.Equals(local.Project ?? "", external.Project ?? "", StringComparison.OrdinalIgnoreCase))
                changed.Add("project");
            // folderName is not an actionable sync field (preserved on accept for install stability).
            if (!string.Equals(local.InstallPath ?? "", external.InstallPath ?? "", StringComparison.OrdinalIgnoreCase))
                changed.Add("installPath");
            if (!string.Equals(local.GameIconUrl ?? "", external.GameIconUrl ?? "", StringComparison.OrdinalIgnoreCase))
                changed.Add("appIconUrl");
            if (!string.Equals(local.PreferredVersion ?? "", external.PreferredVersion ?? "", StringComparison.OrdinalIgnoreCase))
                changed.Add("preferredVersion");
            if (!string.Equals(
                    TagHelper.FormatTagsForDisplay(local.Tags),
                    TagHelper.FormatTagsForDisplay(external.Tags),
                    StringComparison.OrdinalIgnoreCase))
                changed.Add("tags");
            if (!AppFilesToAddService.AreEquivalent(local.FilesToAdd, external.FilesToAdd))
                changed.Add("filesToAdd");
            if (!GameModsConfig.AreEquivalent(
                    local.ModsPath, local.ModsSources, local.ModsLayout,
                    external.ModsPath, external.ModsSources, external.ModsLayout))
                changed.Add("mods");

            return changed;
        }

        public static GameInfo CloneForLocal(GameInfo external, bool autoUpdate = false) =>
            new()
            {
                Name = external.Name,
                Project = string.IsNullOrWhiteSpace(external.Project) ? null : external.Project.Trim(),
                Repository = external.Repository,
                RepositorySource = RepositorySourceHelper.IsGitHub(external.RepositorySource)
                    ? null
                    : RepositorySourceHelper.Normalize(external.RepositorySource),
                FolderName = external.FolderName,
                InstallPath = external.InstallPath,
                GameIconUrl = external.GameIconUrl,
                PreferredVersion = external.PreferredVersion,
                SkippedUpdateVersion = external.SkippedUpdateVersion,
                AutoUpdate = autoUpdate,
                Tags = TagHelper.NormalizeTags(external.Tags),
                FilesToAdd = AppFilesToAddService.Normalize(external.FilesToAdd),
                ModsPath = GameModsConfig.NormalizePath(external.ModsPath) is { Length: > 0 } path ? path : null,
                ModsSources = GameModsConfig.NormalizeSources(external.ModsSources),
                ModsLayout = GameModsConfig.NormalizeLayout(external.ModsLayout) is { } layout &&
                             layout != GameModsConfig.LayoutFlat
                    ? layout
                    : null,
                IsExperimental = false,
                IsCustom = true,
                GameManager = external.GameManager,
                CatalogSourceId = null,
            };

        /// <summary>
        /// Replaces catalog fields from external. Preserves local <see cref="GameInfo.FolderName"/>
        /// so accepting catalog updates does not retarget installed folders (conservative policy).
        /// Also preserves <see cref="GameInfo.CustomDisplayName"/>.
        /// </summary>
        public static GameInfo ReplaceFromExternal(GameInfo local, GameInfo external) =>
            new()
            {
                Name = external.Name,
                Project = string.IsNullOrWhiteSpace(external.Project) ? null : external.Project.Trim(),
                CustomDisplayName = local.CustomDisplayName,
                Repository = external.Repository,
                RepositorySource = RepositorySourceHelper.IsGitHub(external.RepositorySource)
                    ? null
                    : RepositorySourceHelper.Normalize(external.RepositorySource),
                FolderName = !string.IsNullOrWhiteSpace(local.FolderName) ? local.FolderName : external.FolderName,
                InstallPath = external.InstallPath,
                GameIconUrl = external.GameIconUrl,
                PreferredVersion = external.PreferredVersion,
                SkippedUpdateVersion = external.SkippedUpdateVersion,
                AutoUpdate = local.AutoUpdate,
                Tags = TagHelper.NormalizeTags(external.Tags),
                FilesToAdd = AppFilesToAddService.Normalize(external.FilesToAdd),
                ModsPath = GameModsConfig.NormalizePath(external.ModsPath) is { Length: > 0 } path ? path : null,
                ModsSources = GameModsConfig.NormalizeSources(external.ModsSources),
                ModsLayout = GameModsConfig.NormalizeLayout(external.ModsLayout) is { } layout &&
                             layout != GameModsConfig.LayoutFlat
                    ? layout
                    : null,
                IsExperimental = local.IsExperimental,
                IsCustom = local.IsCustom,
                GameManager = local.GameManager,
                CatalogSourceId = null,
            };

        public static GameInfo MergeExternalIntoLocal(GameInfo local, GameInfo external)
        {
            var mergedTags = TagHelper.NormalizeTags(
                (local.Tags ?? []).Concat(external.Tags ?? []));

            // Prefer external mods config when present; otherwise keep local.
            var externalHasMods = GameModsConfig.HasUsableConfig(external.ModsPath, external.ModsSources);
            var modsPath = externalHasMods
                ? GameModsConfig.NormalizePath(external.ModsPath)
                : GameModsConfig.NormalizePath(local.ModsPath);
            var modsSources = externalHasMods
                ? GameModsConfig.NormalizeSources(external.ModsSources)
                : GameModsConfig.NormalizeSources(local.ModsSources);
            var modsLayout = GameModsConfig.NormalizeLayout(
                externalHasMods ? external.ModsLayout : local.ModsLayout);

            return new GameInfo
            {
                Name = external.Name,
                Project = string.IsNullOrWhiteSpace(external.Project) ? null : external.Project.Trim(),
                CustomDisplayName = local.CustomDisplayName,
                Repository = external.Repository,
                RepositorySource = RepositorySourceHelper.IsGitHub(external.RepositorySource)
                    ? null
                    : RepositorySourceHelper.Normalize(external.RepositorySource),
                // Keep installed folder mapping stable across catalog renames.
                FolderName = !string.IsNullOrWhiteSpace(local.FolderName) ? local.FolderName : external.FolderName,
                InstallPath = external.InstallPath,
                GameIconUrl = external.GameIconUrl,
                PreferredVersion = external.PreferredVersion,
                SkippedUpdateVersion = local.SkippedUpdateVersion,
                AutoUpdate = local.AutoUpdate,
                Tags = mergedTags,
                FilesToAdd = AppFilesToAddService.Normalize(external.FilesToAdd),
                ModsPath = modsPath.Length > 0 ? modsPath : null,
                ModsSources = modsSources,
                ModsLayout = modsLayout != GameModsConfig.LayoutFlat ? modsLayout : null,
                IsExperimental = local.IsExperimental,
                IsCustom = local.IsCustom,
                GameManager = local.GameManager,
                CatalogSourceId = null,
            };
        }

        public static List<GameInfo> ApplyAddAllExternalOnly(
            List<GameInfo> localApps,
            IReadOnlyList<CatalogSyncRowItem> rows,
            bool autoUpdateNewlyAdded = false)
        {
            var result = new List<GameInfo>(localApps);
            var localKeys = new HashSet<string>(
                result
                    .Where(a => !string.IsNullOrWhiteSpace(a.Repository))
                    .Select(a => a.IdentityKey),
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows.Where(r => r.Status == CatalogSyncStatus.InExternalOnly && r.External != null))
            {
                if (localKeys.Add(row.IdentityKey))
                    result.Add(CloneForLocal(row.External!, autoUpdateNewlyAdded));
            }

            return result;
        }

        public static List<GameInfo> ApplyReplaceAllChanged(
            List<GameInfo> localApps,
            IReadOnlyList<CatalogSyncRowItem> rows)
        {
            var replaceByKey = rows
                .Where(r => r.Status == CatalogSyncStatus.Changed && r.Local != null && r.External != null)
                .ToDictionary(r => r.IdentityKey, r => r, StringComparer.OrdinalIgnoreCase);

            return localApps
                .Select(app =>
                {
                    if (string.IsNullOrWhiteSpace(app.Repository) ||
                        !replaceByKey.TryGetValue(app.IdentityKey, out var row))
                        return app;

                    return ReplaceFromExternal(app, row.External!);
                })
                .ToList();
        }

        public static List<GameInfo> ApplyRowAdd(
            List<GameInfo> localApps,
            CatalogSyncRowItem row,
            bool autoUpdateNewlyAdded = false)
        {
            if (row.External == null || row.Status != CatalogSyncStatus.InExternalOnly)
                return localApps;

            var exists = localApps.Any(a =>
                string.Equals(a.IdentityKey, row.IdentityKey, StringComparison.OrdinalIgnoreCase));

            if (exists)
                return localApps;

            var result = new List<GameInfo>(localApps) { CloneForLocal(row.External, autoUpdateNewlyAdded) };
            return result;
        }

        public static List<GameInfo> ApplyRowReplace(List<GameInfo> localApps, CatalogSyncRowItem row)
        {
            if (row.Local == null || row.External == null || row.Status != CatalogSyncStatus.Changed)
                return localApps;

            return localApps
                .Select(app =>
                    string.Equals(app.IdentityKey, row.IdentityKey, StringComparison.OrdinalIgnoreCase)
                        ? ReplaceFromExternal(app, row.External)
                        : app)
                .ToList();
        }

        public static List<GameInfo> ApplyRowMerge(List<GameInfo> localApps, CatalogSyncRowItem row)
        {
            if (row.Local == null || row.External == null || row.Status != CatalogSyncStatus.Changed)
                return localApps;

            return localApps
                .Select(app =>
                    string.Equals(app.IdentityKey, row.IdentityKey, StringComparison.OrdinalIgnoreCase)
                        ? MergeExternalIntoLocal(app, row.External)
                        : app)
                .ToList();
        }

        public static List<GameInfo> ApplyRowRemove(List<GameInfo> localApps, CatalogSyncRowItem row)
        {
            if (row.Local == null)
                return localApps;

            return localApps
                .Where(app =>
                    !string.Equals(app.IdentityKey, row.IdentityKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static IReadOnlyList<string> AllCompareFields => CompareFields;

        public static bool IsIgnoredForCurrentVersion(AppCatalogSource source, string repository)
        {
            if (string.IsNullOrWhiteSpace(source.CachedListVersion))
                return false;

            source.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return source.IgnoredChangesAtVersion.TryGetValue(repository, out var ignoredVersion) &&
                   string.Equals(ignoredVersion, source.CachedListVersion, StringComparison.Ordinal);
        }

        public static void IgnoreChangesForCurrentVersion(AppCatalogSource source, string repository)
        {
            if (string.IsNullOrWhiteSpace(source.CachedListVersion))
                return;

            source.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            source.IgnoredChangesAtVersion[repository] = source.CachedListVersion;
        }

        public static void ClearIgnoredChange(AppCatalogSource source, string repository)
        {
            source.IgnoredChangesAtVersion?.Remove(repository);
        }

        public static void PruneIgnoredChanges(AppCatalogSource source)
        {
            if (source.IgnoredChangesAtVersion == null || source.IgnoredChangesAtVersion.Count == 0)
                return;

            var current = source.CachedListVersion;
            foreach (var repo in source.IgnoredChangesAtVersion.Keys.ToList())
            {
                if (!string.Equals(source.IgnoredChangesAtVersion[repo], current, StringComparison.Ordinal))
                    source.IgnoredChangesAtVersion.Remove(repo);
            }
        }

        public static bool IsHiddenFromReview(AppCatalogSource source, string repository) =>
            source.HiddenFromReviewRepositories?.Any(r =>
                r.Equals(repository, StringComparison.OrdinalIgnoreCase)) ?? false;

        public static void HideFromReview(AppCatalogSource source, string repository)
        {
            source.HiddenFromReviewRepositories ??= new List<string>();
            if (source.HiddenFromReviewRepositories.Any(r =>
                    r.Equals(repository, StringComparison.OrdinalIgnoreCase)))
                return;

            source.HiddenFromReviewRepositories.Add(repository);
        }

        public static void UnhideFromReview(AppCatalogSource source, string repository)
        {
            if (source.HiddenFromReviewRepositories == null || source.HiddenFromReviewRepositories.Count == 0)
                return;

            source.HiddenFromReviewRepositories.RemoveAll(r =>
                r.Equals(repository, StringComparison.OrdinalIgnoreCase));
        }

        public static void PruneHiddenRepositories(AppCatalogSource source, IEnumerable<string> validRepositories)
        {
            if (source.HiddenFromReviewRepositories == null || source.HiddenFromReviewRepositories.Count == 0)
                return;

            var valid = new HashSet<string>(validRepositories, StringComparer.OrdinalIgnoreCase);
            source.HiddenFromReviewRepositories.RemoveAll(r => !valid.Contains(r));
        }

        public static bool IsActionableRow(CatalogSyncRowItem row, AppCatalogSource source)
        {
            if (IsHiddenFromReview(source, row.Repository))
                return false;

            if (row.Status == CatalogSyncStatus.Unchanged)
                return false;

            if (IsIgnoredForCurrentVersion(source, row.Repository))
                return false;

            return row.Status is CatalogSyncStatus.InExternalOnly or CatalogSyncStatus.Changed;
        }

        public static bool HasActionableChanges(
            AppCatalogSource source,
            IReadOnlyList<CatalogSyncRowItem> rows) =>
            rows.Any(r => IsActionableRow(r, source));

        public static IEnumerable<CatalogSyncRowItem> FilterVisibleRows(
            IReadOnlyList<CatalogSyncRowItem> rows,
            AppCatalogSource source,
            bool showUpToDateApps)
        {
            foreach (var row in rows)
            {
                if (IsHiddenFromReview(source, row.Repository))
                    continue;

                if (IsIgnoredForCurrentVersion(source, row.Repository))
                    continue;

                if (!showUpToDateApps && row.Status == CatalogSyncStatus.Unchanged)
                    continue;

                yield return row;
            }
        }

        public static IEnumerable<CatalogSyncRowItem> FilterByReviewFilter(
            IReadOnlyList<CatalogSyncRowItem> rows,
            AppCatalogSource source,
            CatalogReviewFilter filter)
        {
            foreach (var row in rows)
            {
                var isHidden = IsHiddenFromReview(source, row.Repository);

                if (filter == CatalogReviewFilter.Hidden)
                {
                    if (isHidden)
                        yield return row;
                    continue;
                }

                if (isHidden)
                    continue;

                if (filter != CatalogReviewFilter.All &&
                    filter != CatalogReviewFilter.NotInLibrary &&
                    IsIgnoredForCurrentVersion(source, row.Repository))
                    continue;

                var include = filter switch
                {
                    CatalogReviewFilter.All => true,
                    CatalogReviewFilter.NeedsReview => IsActionableRow(row, source),
                    CatalogReviewFilter.New => row.Status == CatalogSyncStatus.InExternalOnly &&
                                               !IsIgnoredForCurrentVersion(source, row.Repository),
                    CatalogReviewFilter.NotInLibrary => row.Status == CatalogSyncStatus.InExternalOnly,
                    CatalogReviewFilter.Changed => row.Status == CatalogSyncStatus.Changed &&
                                                   !IsIgnoredForCurrentVersion(source, row.Repository),
                    CatalogReviewFilter.UpToDate => row.Status == CatalogSyncStatus.Unchanged,
                    _ => true,
                };

                if (include)
                    yield return row;
            }
        }

        public static IEnumerable<CatalogSyncRowItem> SortRows(
            IEnumerable<CatalogSyncRowItem> rows,
            string sortMode,
            bool ignoreArticlesWhenSorting = true)
        {
            var list = rows as IReadOnlyList<CatalogSyncRowItem> ?? rows.ToList();
            string NameKey(CatalogSyncRowItem r) => ignoreArticlesWhenSorting
                ? NameSortHelper.GetAlphabeticalSortKey(r.DisplayName)
                : (r.DisplayName ?? string.Empty);

            return sortMode switch
            {
                "NameDesc" => list
                    .OrderByDescending(NameKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Repository, StringComparer.OrdinalIgnoreCase),
                "Repository" => list
                    .OrderBy(r => r.Repository, StringComparer.OrdinalIgnoreCase),
                "Status" => list
                    .OrderBy(r => GetStatusSortRank(r.Status))
                    .ThenBy(NameKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Repository, StringComparer.OrdinalIgnoreCase),
                _ => list
                    .OrderBy(NameKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Repository, StringComparer.OrdinalIgnoreCase),
            };
        }

        private static int GetStatusSortRank(CatalogSyncStatus status) => status switch
        {
            CatalogSyncStatus.Changed => 0,
            CatalogSyncStatus.InExternalOnly => 1,
            CatalogSyncStatus.Unchanged => 2,
            _ => 3,
        };

        public static string FormatVersionForDisplay(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "unknown";

            if (version.Length <= 16)
                return version;

            return $"{version[..8]}…{version[^8..]}";
        }

        public static bool IsUnreviewedVersion(string? acknowledgedVersion) =>
            string.IsNullOrWhiteSpace(acknowledgedVersion) || acknowledgedVersion == "0";

        public static bool IsReviewedVersion(string? acknowledgedVersion) =>
            !IsUnreviewedVersion(acknowledgedVersion);

        public static string FormatCatalogVersionSummary(string? cachedVersion, string? acknowledgedVersion)
        {
            if (string.IsNullOrWhiteSpace(cachedVersion) && IsUnreviewedVersion(acknowledgedVersion))
                return "";

            if (string.IsNullOrWhiteSpace(cachedVersion))
                return IsUnreviewedVersion(acknowledgedVersion)
                    ? ""
                    : $"Reviewed v{FormatVersionForDisplay(acknowledgedVersion)}";

            var cached = FormatVersionForDisplay(cachedVersion);
            if (IsUnreviewedVersion(acknowledgedVersion))
                return $"List version: {cached}\nLast reviewed: not yet";

            var reviewed = FormatVersionForDisplay(acknowledgedVersion);
            return $"List version: {cached}\nLast reviewed: {reviewed}";
        }

        public static CatalogVersionParts FormatCatalogVersionParts(string? cachedVersion, string? acknowledgedVersion)
        {
            var lastReviewedUnreviewed = IsUnreviewedVersion(acknowledgedVersion);
            var versionRowVisible = !string.IsNullOrWhiteSpace(cachedVersion) || !lastReviewedUnreviewed;

            if (string.IsNullOrWhiteSpace(cachedVersion))
            {
                return new CatalogVersionParts(
                    ListVersionText: "",
                    LastReviewedText: lastReviewedUnreviewed
                        ? "not yet"
                        : FormatVersionForDisplay(acknowledgedVersion),
                    LastReviewedUnreviewed: lastReviewedUnreviewed,
                    VersionRowVisible: versionRowVisible);
            }

            return new CatalogVersionParts(
                ListVersionText: FormatVersionForDisplay(cachedVersion),
                LastReviewedText: lastReviewedUnreviewed
                    ? "not yet"
                    : FormatVersionForDisplay(acknowledgedVersion),
                LastReviewedUnreviewed: lastReviewedUnreviewed,
                VersionRowVisible: true);
        }
    }

    public readonly record struct CatalogVersionParts(
        string ListVersionText,
        string LastReviewedText,
        bool LastReviewedUnreviewed,
        bool VersionRowVisible);
}
