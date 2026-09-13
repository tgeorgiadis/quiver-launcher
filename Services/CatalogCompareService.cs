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

    public enum CatalogIdentityChangeKind
    {
        None,
        Promote,
        Demote,
        Retarget,
    }

    public enum CatalogCompatibilityState { Unverified, Checking, Available, Unavailable }

    public class CatalogSyncRowItem : INotifyPropertyChanged
    {
        public CatalogCompatibilityState CompatibilityState { get; private set; }
        public string CompatibilityText { get; private set; } = "";
        public bool HasCompatibilityText => !string.IsNullOrWhiteSpace(CompatibilityText);
        public void SetCompatibility(CatalogCompatibilityState state, string text)
        {
            if (CompatibilityState == state && CompatibilityText == text) return;
            CompatibilityState = state;
            CompatibilityText = text;
            PropertyChanged?.Invoke(this, new(nameof(CompatibilityState)));
            PropertyChanged?.Invoke(this, new(nameof(CompatibilityText)));
            PropertyChanged?.Invoke(this, new(nameof(HasCompatibilityText)));
        }

        private bool _isGamepadFocused;
        private bool _isHovered;
        private bool _isAddPending;
        private bool _wasAdded;
        private CatalogReviewGridCardActions.Layout? _desktopLayout;
        private readonly ResettableObservableCollection<CatalogReviewGridCardActions.ChromeItem> _desktopChrome = new();
        public bool IsAddPending => _isAddPending;
        public bool ShowAddButton => CanAdd;
        public void SetAddPending(bool pending, bool saved = false)
        {
            _isAddPending = pending;
            _wasAdded = saved;
            // Let the view move focus before removing the Add control.
            PropertyChanged?.Invoke(this, new(nameof(IsAddPending)));
            _desktopLayout = null;
            PropertyChanged?.Invoke(this, new(null));
        }

        public void UpdateComparison(CatalogSyncRowItem row)
        {
            Status = row.Status; Local = row.Local; AddBlockedReason = row.AddBlockedReason;
            ChangedFields = row.ChangedFields; FieldDiffs = row.FieldDiffs;
            DetailsFields = row.DetailsFields; IdentityChangeKind = row.IdentityChangeKind;
            _desktopLayout = null;
            PropertyChanged?.Invoke(this, new(null));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public CatalogSyncStatus Status { get; set; }
        public string Repository { get; init; } = "";
        public string IdentityKey { get; init; } = "";
        public string ReviewKey =>
            !string.IsNullOrWhiteSpace(Repository) ? Repository : IdentityKey;
        public string Subtitle =>
            !string.IsNullOrWhiteSpace(Repository) ? Repository : "Manually managed";
        public bool HasRepository => !string.IsNullOrWhiteSpace(Repository);
        public string? EffectiveRepositorySource =>
            External?.EffectiveRepositorySource ?? Local?.EffectiveRepositorySource;
        public string DisplayName { get; init; } = "";

        /// <summary>Library-style title (name only) for grid cards.</summary>
        public string TitleName
        {
            get
            {
                var title = AppDisplayName.Resolve(
                    External?.Name ?? Local?.Name,
                    External?.Project ?? Local?.Project,
                    customDisplayName: null,
                    LibraryNameStyle.NameAndProject);
                return title.Length > 0 ? title : DisplayName;
            }
        }

        /// <summary>Library-style project line under the title on grid cards.</summary>
        public string ProjectSubtitle =>
            AppDisplayName.ResolveProjectSubtitle(
                External?.Name ?? Local?.Name,
                External?.Project ?? Local?.Project,
                customDisplayName: null,
                LibraryNameStyle.NameAndProject);

        public bool HasProjectSubtitle => !string.IsNullOrWhiteSpace(ProjectSubtitle);

        /// <summary>Grid badges for actionable states only — not "Up to date".</summary>
        public bool HasStatusBadge =>
            Status is CatalogSyncStatus.InExternalOnly
                or CatalogSyncStatus.Changed
                or CatalogSyncStatus.InLocalOnly;

        public bool IsChangedBadge => Status == CatalogSyncStatus.Changed;

        public bool IsHovered
        {
            get => _isHovered;
            set
            {
                if (_isHovered == value)
                    return;

                _isHovered = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHovered)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldScrollTitle)));
            }
        }

        public bool ShouldScrollTitle => _isHovered || _isGamepadFocused;
        public GameInfo? Local { get; set; }
        public GameInfo? External { get; init; }
        public IReadOnlyList<string> ChangedFields { get; set; } = [];
        public IReadOnlyList<CatalogSyncFieldDiffItem> FieldDiffs { get; set; } = [];
        public IReadOnlyList<CatalogSyncFieldDiffItem> DetailsFields { get; set; } = [];
        public CatalogIdentityChangeKind IdentityChangeKind { get; set; }
        public string AddBlockedReason { get; set; } = "";

        public bool HasInlineDiff => FieldDiffs.Count > 0;
        public bool HasDetailsFields => DetailsFields.Count > 0;
        public bool HasAddBlockedReason => !string.IsNullOrWhiteSpace(AddBlockedReason);
        public bool HasReviewCardFooter => HasAddBlockedReason || HasInlineDiff;

        public string IconUrl =>
            External?.DefaultIconUrl
            ?? Local?.DefaultIconUrl
            ?? "/Assets/DefaultGame.png";

        public string StatusShortLabel => Status switch
        {
            CatalogSyncStatus.InLocalOnly => "Local only",
            CatalogSyncStatus.InExternalOnly => "New",
            CatalogSyncStatus.Unchanged => "Up to date",
            CatalogSyncStatus.Changed => IdentityChangeKind switch
            {
                CatalogIdentityChangeKind.Promote => "Promote",
                CatalogIdentityChangeKind.Demote => "Demote",
                CatalogIdentityChangeKind.Retarget => "Retarget",
                _ => "Changed",
            },
            _ => "",
        };

        public string StatusLabel => Status switch
        {
            CatalogSyncStatus.InLocalOnly => "Local only",
            CatalogSyncStatus.InExternalOnly => "Not in library",
            CatalogSyncStatus.Unchanged => "Up to date",
            CatalogSyncStatus.Changed => IdentityChangeKind switch
            {
                CatalogIdentityChangeKind.Promote => "Promote to repository",
                CatalogIdentityChangeKind.Demote => "Demote to manual",
                CatalogIdentityChangeKind.Retarget => "Retarget repository",
                _ => "Changed",
            },
            _ => "",
        };

        public bool CanAdd => !_isAddPending && !_wasAdded && Status == CatalogSyncStatus.InExternalOnly && !HasAddBlockedReason;
        public bool CanReplace => Status == CatalogSyncStatus.Changed;
        public bool CanMerge => Status == CatalogSyncStatus.Changed;
        public bool CanIgnore => Status is CatalogSyncStatus.Changed or CatalogSyncStatus.InExternalOnly;
        public bool CanRemoveFromLibrary => Local != null;
        public bool ShowGridCardPrimaryAdd => ShowAddButton;
        public bool ShowGridCardPrimaryMerge => CanMerge && !CanAdd;
        public bool ShowMenuAdd => CanAdd && !ShowGridCardPrimaryAdd;
        public bool ShowMenuMerge => CanMerge && !ShowGridCardPrimaryMerge;

        private bool _showHideButton, _showCardHideButton, _showUnhideButton, _showRemoveFromLibrary;
        public bool ShowHideButton { get => _showHideButton; set => SetActionVisibility(ref _showHideButton, value); }
        public bool ShowCardHideButton { get => _showCardHideButton; set => SetActionVisibility(ref _showCardHideButton, value); }
        public bool ShowUnhideButton { get => _showUnhideButton; set => SetActionVisibility(ref _showUnhideButton, value); }
        public bool ShowRemoveFromLibrary { get => _showRemoveFromLibrary; set => SetActionVisibility(ref _showRemoveFromLibrary, value); }
        private void SetActionVisibility(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            _desktopLayout = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public CatalogReviewGridCardActions.Layout GridCardDesktopLayout =>
            _desktopLayout ??= CatalogReviewGridCardActions.ForDesktop(
                ShowAddButton,
                CanMerge,
                ShowCardHideButton,
                ShowUnhideButton,
                ShowRemoveFromLibrary,
                IdentityKey);

        public IReadOnlyList<CatalogReviewGridCardActions.ChromeItem> GridCardDesktopChrome =>
            GetDesktopChrome();

        public IReadOnlyList<CatalogReviewGridCardActions.ChromeItem> GridCardMobileActions =>
            GridCardDesktopLayout.Inline.Concat(GridCardDesktopLayout.Menu)
                .Select(action => new CatalogReviewGridCardActions.ChromeItem(
                    CatalogReviewGridCardActions.ToChrome(action), IdentityKey)).ToArray();

        private IReadOnlyList<CatalogReviewGridCardActions.ChromeItem> GetDesktopChrome()
        {
            var chrome = GridCardDesktopLayout.Chrome;
            var next = chrome.Select(item => _desktopChrome.FirstOrDefault(old =>
                old.Kind == item.Kind && old.IdentityKey == item.IdentityKey &&
                old.MenuAdd == item.MenuAdd && old.MenuMerge == item.MenuMerge && old.MenuDetails == item.MenuDetails &&
                old.MenuHide == item.MenuHide && old.MenuUnhide == item.MenuUnhide && old.MenuRemove == item.MenuRemove) ?? item).ToList();
            _desktopChrome.UpdateWith(next);
            return _desktopChrome;
        }

        public int GridCardDesktopColumnCount => GridCardDesktopLayout.ColumnCount;

        public bool ShowGridInlineAdd =>
            GridCardDesktopLayout.IsInline(CatalogReviewGridCardActions.Action.Add);
        public bool ShowGridInlineMerge =>
            GridCardDesktopLayout.IsInline(CatalogReviewGridCardActions.Action.Merge);
        public bool ShowGridInlineDetails =>
            GridCardDesktopLayout.IsInline(CatalogReviewGridCardActions.Action.Details);
        public bool ShowGridInlineHide =>
            GridCardDesktopLayout.IsInline(CatalogReviewGridCardActions.Action.Hide);
        public bool ShowGridInlineUnhide =>
            GridCardDesktopLayout.IsInline(CatalogReviewGridCardActions.Action.Unhide);
        public bool ShowGridInlineRemove =>
            GridCardDesktopLayout.IsInline(CatalogReviewGridCardActions.Action.Remove);
        public bool ShowGridMore => GridCardDesktopLayout.ShowMore;
        public bool ShowGridMenuAdd =>
            GridCardDesktopLayout.IsMenu(CatalogReviewGridCardActions.Action.Add);
        public bool ShowGridMenuMerge =>
            GridCardDesktopLayout.IsMenu(CatalogReviewGridCardActions.Action.Merge);
        public bool ShowGridMenuDetails =>
            GridCardDesktopLayout.IsMenu(CatalogReviewGridCardActions.Action.Details);
        public bool ShowGridMenuHide =>
            GridCardDesktopLayout.IsMenu(CatalogReviewGridCardActions.Action.Hide);
        public bool ShowGridMenuUnhide =>
            GridCardDesktopLayout.IsMenu(CatalogReviewGridCardActions.Action.Unhide);
        public bool ShowGridMenuRemove =>
            GridCardDesktopLayout.IsMenu(CatalogReviewGridCardActions.Action.Remove);

        public bool IsGamepadFocused
        {
            get => _isGamepadFocused;
            set
            {
                if (_isGamepadFocused == value)
                    return;

                _isGamepadFocused = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGamepadFocused)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldScrollTitle)));
            }
        }
    }

    public static class CatalogCompareService
    {
        private static readonly string[] CompareFields =
        [
            "name",
            "project",
            "appIconUrl",
            "tags",
            "filesToAdd",
            "mods",
            "releaseAssetFilter",
            "repository",
            "repositorySource",
        ];

        public static Dictionary<string, GameInfo> IndexByIdentityKey(IEnumerable<GameInfo> apps) =>
            apps
                .Where(a => !string.IsNullOrWhiteSpace(a.IdentityKey))
                .GroupBy(a => a.IdentityKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, GameInfo> IndexByInstanceKey(IEnumerable<GameInfo> apps) =>
            apps
                .Where(a => !string.IsNullOrWhiteSpace(a.InstanceKey))
                .GroupBy(a => a.InstanceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Apps in <paramref name="currentApps"/> that were added or whose install layout
        /// (folder, path, files-to-add, manual flag) changed versus <paramref name="previousApps"/>.
        /// </summary>
        public static List<GameInfo> GetAppsNeedingInstallSync(
            IEnumerable<GameInfo> previousApps,
            IEnumerable<GameInfo> currentApps)
        {
            var previousList = previousApps as IList<GameInfo> ?? previousApps.ToList();
            var previousByKey = IndexByInstanceKey(previousList);
            var result = new List<GameInfo>();

            foreach (var app in currentApps)
            {
                GameInfo? previous = null;
                if (!string.IsNullOrWhiteSpace(app.InstanceKey))
                    previousByKey.TryGetValue(app.InstanceKey, out previous);

                if (previous == null && !string.IsNullOrWhiteSpace(app.FolderName))
                {
                    previous = previousList.FirstOrDefault(p =>
                        string.Equals(p.FolderName, app.FolderName, StringComparison.OrdinalIgnoreCase));
                }

                if (previous == null || NeedsInstallSync(previous, app))
                    result.Add(app);
            }

            return result;
        }

        private static bool NeedsInstallSync(GameInfo previous, GameInfo current) =>
            previous.IsManuallyManaged != current.IsManuallyManaged ||
            !string.Equals(previous.FolderName, current.FolderName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previous.InstallPath, current.InstallPath, StringComparison.OrdinalIgnoreCase) ||
            !AppFilesToAddService.AreEquivalent(previous.FilesToAdd, current.FilesToAdd);

        private static Dictionary<string, GameInfo> IndexByFolderName(IEnumerable<GameInfo> apps) =>
            apps
                .Where(a => !string.IsNullOrWhiteSpace(a.FolderName))
                .GroupBy(a => a.FolderName!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<CatalogSyncRowItem> BuildCompareRows(
            List<GameInfo> localApps,
            List<GameInfo> externalApps,
            IReadOnlyDictionary<string, CatalogSyncRowItem>? unchangedDefinitions = null)
        {
            var localByInstance = IndexByInstanceKey(localApps);
            var localByIdentity = localApps
                .Where(a => !string.IsNullOrWhiteSpace(a.IdentityKey))
                .GroupBy(a => a.IdentityKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var localByFolder = IndexByFolderName(localApps);

            var rows = new List<CatalogSyncRowItem>();
            var seenInstanceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchedLocalInstanceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var external in externalApps)
            {
                if (string.IsNullOrWhiteSpace(external.Repository) &&
                    string.IsNullOrWhiteSpace(external.FolderName))
                    continue;

                var instanceKey = external.InstanceKey;
                if (string.IsNullOrWhiteSpace(instanceKey) || !seenInstanceKeys.Add(instanceKey))
                    continue;

                GameInfo? local = null;
                if (localByInstance.TryGetValue(instanceKey, out var byInstance) &&
                    matchedLocalInstanceKeys.Add(byInstance.InstanceKey))
                {
                    local = byInstance;
                }
                else if (localByIdentity.TryGetValue(external.IdentityKey, out var sameRepoLocals) &&
                         sameRepoLocals.Count == 1 &&
                         matchedLocalInstanceKeys.Add(sameRepoLocals[0].InstanceKey))
                {
                    local = sameRepoLocals[0];
                }
                else if (!string.IsNullOrWhiteSpace(external.FolderName) &&
                         localByFolder.TryGetValue(external.FolderName.Trim(), out var byFolder) &&
                         matchedLocalInstanceKeys.Add(byFolder.InstanceKey))
                {
                    local = byFolder;
                }

                GameInfo? folderOccupiedBy = null;
                if (local == null &&
                    !string.IsNullOrWhiteSpace(external.FolderName) &&
                    localByFolder.TryGetValue(external.FolderName.Trim(), out var occupying))
                {
                    folderOccupiedBy = occupying;
                }

                // Single additions do not change existing definitions. Still run the matching
                // pass (folder/repository matches can affect other rows), but only build diffs
                // and detail snapshots for rows whose actual match/conflict changed.
                if (unchangedDefinitions != null && unchangedDefinitions.TryGetValue(instanceKey, out var previous) &&
                    previous.Local?.InstanceKey == local?.InstanceKey &&
                    (local == null || previous.Local != null && IsLibrarySyncedWithCatalog(previous.Local, local) &&
                        IsLibrarySyncedWithCatalog(local, previous.Local)) &&
                    previous.AddBlockedReason == (local == null ? FormatAddBlockedReason(external, folderOccupiedBy) : ""))
                    rows.Add(previous);
                else rows.Add(CreateCompareRow(external.Repository ?? "", local, external, folderOccupiedBy));
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

        private static CatalogSyncRowItem CreateCompareRow(
            string repo,
            GameInfo? local,
            GameInfo? external,
            GameInfo? folderOccupiedBy = null)
        {
            CatalogSyncStatus status;
            IReadOnlyList<string> changedFields = [];

            if (local != null && external == null)
                status = CatalogSyncStatus.InLocalOnly;
            else if (local == null && external != null)
                status = CatalogSyncStatus.InExternalOnly;
            else if (local != null && external != null &&
                     IsLibrarySyncedWithCatalog(local, external))
                status = CatalogSyncStatus.Unchanged;
            else
            {
                status = CatalogSyncStatus.Changed;
                changedFields = GetChangedFields(local!, external!);
            }

            var fieldDiffs = CatalogSyncFieldDiffBuilder.BuildFieldDiffs(status, local, external, changedFields);
            var detailsFields = fieldDiffs.Count > 0
                ? fieldDiffs
                : CatalogSyncFieldDiffBuilder.BuildSnapshot(local ?? external);

            return new CatalogSyncRowItem
            {
                Status = status,
                Repository = repo,
                IdentityKey = local?.InstanceKey
                    ?? external?.InstanceKey
                    ?? RepositorySourceHelper.GetInstanceKey(null, repo, external?.FolderName ?? local?.FolderName),
                // Catalog review keeps project in the title so ports of the same game stay distinct in a dense list.
                DisplayName = AppDisplayName.Resolve(
                    external?.Name ?? local?.Name,
                    external?.Project ?? local?.Project,
                    customDisplayName: null,
                    LibraryNameStyle.NameAndProjectInTitle) is { Length: > 0 } composed
                    ? composed
                    : (!string.IsNullOrWhiteSpace(repo) ? repo : external?.FolderName ?? local?.FolderName ?? ""),
                Local = local,
                External = external,
                ChangedFields = changedFields,
                FieldDiffs = fieldDiffs,
                DetailsFields = detailsFields,
                IdentityChangeKind = GetIdentityChangeKind(local, external),
                AddBlockedReason = status == CatalogSyncStatus.InExternalOnly
                    ? FormatAddBlockedReason(external, folderOccupiedBy)
                    : "",
            };
        }

        public static CatalogIdentityChangeKind GetIdentityChangeKind(GameInfo? local, GameInfo? external)
        {
            if (local == null || external == null)
                return CatalogIdentityChangeKind.None;
            if (local.IsManuallyManaged && !external.IsManuallyManaged)
                return CatalogIdentityChangeKind.Promote;
            if (!local.IsManuallyManaged && external.IsManuallyManaged)
                return CatalogIdentityChangeKind.Demote;
            if (IsRepositoryRetarget(local, external))
                return CatalogIdentityChangeKind.Retarget;
            return CatalogIdentityChangeKind.None;
        }

        public static string FormatAddBlockedReason(GameInfo? external, GameInfo? occupyingLocal)
        {
            if (external == null || occupyingLocal == null ||
                string.IsNullOrWhiteSpace(external.FolderName))
                return "";

            var occupant = AppDisplayName.Resolve(
                occupyingLocal.Name,
                occupyingLocal.Project,
                occupyingLocal.CustomDisplayName,
                LibraryNameStyle.NameAndProjectInTitle);
            if (string.IsNullOrWhiteSpace(occupant))
                occupant = occupyingLocal.FolderName ?? occupyingLocal.Repository ?? "another library app";

            return $"Folder \"{external.FolderName.Trim()}\" is already used by {occupant}.";
        }

        public static string FormatAddBlockedMessage(IReadOnlyList<CatalogSyncRowItem> blockedRows)
        {
            if (blockedRows.Count == 0)
                return "";
            if (blockedRows.Count == 1)
                return blockedRows[0].AddBlockedReason;

            var lines = blockedRows
                .Select(row => string.IsNullOrWhiteSpace(row.AddBlockedReason)
                    ? row.DisplayName
                    : row.AddBlockedReason);
            return $"{blockedRows.Count} apps were not added because their folders are already used:\n" +
                   string.Join("\n", lines.Select(line => "• " + line));
        }

        /// <summary>
        /// Library review sync. Catalog-owned fields must match; extra local tags and
        /// <see cref="GameInfo.PreferredVersion"/> are local-owned and do not keep a row Changed.
        /// </summary>
        public static bool IsLibrarySyncedWithCatalog(GameInfo local, GameInfo external) =>
            string.Equals(local.Repository ?? "", external.Repository ?? "", StringComparison.OrdinalIgnoreCase) &&
            (local.IsManuallyManaged || external.IsManuallyManaged ||
             string.Equals(local.EffectiveRepositorySource, external.EffectiveRepositorySource, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(local.Name, external.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(local.Project ?? "", external.Project ?? "", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(local.GameIconUrl ?? "", external.GameIconUrl ?? "", StringComparison.OrdinalIgnoreCase) &&
            TagHelper.ContainsAllTags(local.Tags, external.Tags) &&
            AppFilesToAddService.AreEquivalent(local.FilesToAdd, external.FilesToAdd) &&
            string.Equals(
                RepositorySourceHelper.NormalizeReleaseAssetFilter(local.ReleaseAssetFilter) ?? "",
                RepositorySourceHelper.NormalizeReleaseAssetFilter(external.ReleaseAssetFilter) ?? "",
                StringComparison.OrdinalIgnoreCase) &&
            GameModsConfig.AreEquivalent(
                local.ModsPath, local.ModsSources, local.ModsLayout,
                external.ModsPath, external.ModsSources, external.ModsLayout);

        public static IReadOnlyList<string> GetChangedFields(GameInfo local, GameInfo external)
        {
            var changed = new List<string>();

            if (!string.Equals(local.Repository ?? "", external.Repository ?? "", StringComparison.OrdinalIgnoreCase))
                changed.Add("repository");
            if (!local.IsManuallyManaged && !external.IsManuallyManaged &&
                !string.Equals(local.EffectiveRepositorySource, external.EffectiveRepositorySource, StringComparison.OrdinalIgnoreCase))
                changed.Add("repositorySource");
            if (!string.Equals(local.Name, external.Name, StringComparison.OrdinalIgnoreCase))
                changed.Add("name");
            if (!string.Equals(local.Project ?? "", external.Project ?? "", StringComparison.OrdinalIgnoreCase))
                changed.Add("project");
            // folderName and installPath are not actionable sync fields (preserved on accept).
            if (!string.Equals(local.GameIconUrl ?? "", external.GameIconUrl ?? "", StringComparison.OrdinalIgnoreCase))
                changed.Add("appIconUrl");
            if (!TagHelper.ContainsAllTags(local.Tags, external.Tags))
                changed.Add("tags");
            if (!AppFilesToAddService.AreEquivalent(local.FilesToAdd, external.FilesToAdd))
                changed.Add("filesToAdd");
            if (!string.Equals(
                    RepositorySourceHelper.NormalizeReleaseAssetFilter(local.ReleaseAssetFilter) ?? "",
                    RepositorySourceHelper.NormalizeReleaseAssetFilter(external.ReleaseAssetFilter) ?? "",
                    StringComparison.OrdinalIgnoreCase))
                changed.Add("releaseAssetFilter");
            if (!GameModsConfig.AreEquivalent(
                    local.ModsPath, local.ModsSources, local.ModsLayout,
                    external.ModsPath, external.ModsSources, external.ModsLayout))
                changed.Add("mods");

            return changed;
        }

        public static GameInfo CloneForLocal(GameInfo external, bool autoUpdate = false)
        {
            var manual = external.IsManuallyManaged;
            return new GameInfo
            {
                Name = external.Name,
                Project = string.IsNullOrWhiteSpace(external.Project) ? null : external.Project.Trim(),
                Repository = manual ? string.Empty : external.Repository,
                RepositorySource = manual || RepositorySourceHelper.IsGitHub(external.RepositorySource)
                    ? null
                    : RepositorySourceHelper.Normalize(external.RepositorySource),
                FolderName = external.FolderName,
                InstallPath = external.InstallPath,
                GameIconUrl = external.GameIconUrl,
                PreferredVersion = manual ? null : external.PreferredVersion,
                SkippedUpdateVersion = manual ? null : external.SkippedUpdateVersion,
                AutoUpdate = autoUpdate && !manual,
                DeferUpdateTracking = false,
                Tags = TagHelper.NormalizeTags(external.Tags),
                FilesToAdd = AppFilesToAddService.Normalize(external.FilesToAdd),
                ReleaseAssetFilter = manual
                    ? null
                    : RepositorySourceHelper.NormalizeReleaseAssetFilter(external.ReleaseAssetFilter),
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
        }

        private static bool IsRepositoryRetarget(GameInfo local, GameInfo external) =>
            !local.IsManuallyManaged &&
            !external.IsManuallyManaged &&
            !string.Equals(local.IdentityKey, external.IdentityKey, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Replaces catalog fields from external. Preserves local <see cref="GameInfo.FolderName"/>
        /// and <see cref="GameInfo.InstallPath"/> so accepting catalog updates does not retarget
        /// installed folders (conservative policy). Also preserves <see cref="GameInfo.CustomDisplayName"/>
        /// and <see cref="GameInfo.PreferredVersion"/> / <see cref="GameInfo.SkippedUpdateVersion"/>
        /// (unless promotion, demotion, or retarget resets tracking).
        /// Promoting a manual app onto a repository, or changing which repository an app uses,
        /// keeps files and leaves Auto Update off.
        /// </summary>
        public static GameInfo ReplaceFromExternal(GameInfo local, GameInfo external)
        {
            var promotion = local.IsManuallyManaged && !external.IsManuallyManaged;
            var demotion = !local.IsManuallyManaged && external.IsManuallyManaged;
            var retarget = IsRepositoryRetarget(local, external);
            var manual = external.IsManuallyManaged;
            var resetTracking = promotion || demotion || retarget || manual;
            return new GameInfo
            {
                Name = external.Name,
                Project = string.IsNullOrWhiteSpace(external.Project) ? null : external.Project.Trim(),
                CustomDisplayName = local.CustomDisplayName,
                Repository = manual ? string.Empty : external.Repository,
                RepositorySource = manual || RepositorySourceHelper.IsGitHub(external.RepositorySource)
                    ? null
                    : RepositorySourceHelper.Normalize(external.RepositorySource),
                FolderName = !string.IsNullOrWhiteSpace(local.FolderName) ? local.FolderName : external.FolderName,
                InstallPath = !string.IsNullOrWhiteSpace(local.InstallPath) ? local.InstallPath : external.InstallPath,
                GameIconUrl = external.GameIconUrl,
                PreferredVersion = resetTracking ? null : local.PreferredVersion,
                SkippedUpdateVersion = resetTracking ? null : local.SkippedUpdateVersion,
                AutoUpdate = resetTracking ? false : local.AutoUpdate,
                DeferUpdateTracking = promotion || retarget,
                Tags = TagHelper.NormalizeTags(external.Tags),
                FilesToAdd = AppFilesToAddService.Normalize(external.FilesToAdd),
                ReleaseAssetFilter = manual
                    ? null
                    : RepositorySourceHelper.NormalizeReleaseAssetFilter(external.ReleaseAssetFilter),
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
        }

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

            var promotion = local.IsManuallyManaged && !external.IsManuallyManaged;
            var demotion = !local.IsManuallyManaged && external.IsManuallyManaged;
            var retarget = IsRepositoryRetarget(local, external);
            var manual = external.IsManuallyManaged;
            var resetTracking = promotion || demotion || retarget || manual;

            return new GameInfo
            {
                Name = external.Name,
                Project = string.IsNullOrWhiteSpace(external.Project) ? null : external.Project.Trim(),
                CustomDisplayName = local.CustomDisplayName,
                Repository = manual ? string.Empty : external.Repository,
                RepositorySource = manual || RepositorySourceHelper.IsGitHub(external.RepositorySource)
                    ? null
                    : RepositorySourceHelper.Normalize(external.RepositorySource),
                // Keep installed folder mapping and custom install path stable across catalog updates.
                FolderName = !string.IsNullOrWhiteSpace(local.FolderName) ? local.FolderName : external.FolderName,
                InstallPath = !string.IsNullOrWhiteSpace(local.InstallPath) ? local.InstallPath : external.InstallPath,
                GameIconUrl = external.GameIconUrl,
                PreferredVersion = resetTracking ? null : local.PreferredVersion,
                SkippedUpdateVersion = resetTracking ? null : local.SkippedUpdateVersion,
                AutoUpdate = resetTracking ? false : local.AutoUpdate,
                DeferUpdateTracking = promotion || retarget,
                Tags = mergedTags,
                FilesToAdd = AppFilesToAddService.Normalize(external.FilesToAdd),
                ReleaseAssetFilter = manual
                    ? null
                    : RepositorySourceHelper.NormalizeReleaseAssetFilter(external.ReleaseAssetFilter),
                ModsPath = modsPath.Length > 0 ? modsPath : null,
                ModsSources = modsSources,
                ModsLayout = modsLayout != GameModsConfig.LayoutFlat ? modsLayout : null,
                IsExperimental = local.IsExperimental,
                IsCustom = local.IsCustom,
                GameManager = local.GameManager,
                CatalogSourceId = null,
            };
        }

        public static bool MatchesLocalApp(GameInfo app, CatalogSyncRowItem row)
        {
            if (row.Local != null)
            {
                return string.Equals(
                    app.InstanceKey,
                    row.Local.InstanceKey,
                    StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(row.IdentityKey) &&
                   string.Equals(app.InstanceKey, row.IdentityKey, StringComparison.OrdinalIgnoreCase);
        }

        public static int FindRowIndexForLibraryApp(
            IReadOnlyList<CatalogSyncRowItem> rows,
            GameInfo game)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (MatchesLocalApp(game, row) ||
                    string.Equals(row.IdentityKey, game.InstanceKey, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public static List<GameInfo> ApplyAddAllExternalOnly(
            List<GameInfo> localApps,
            IReadOnlyList<CatalogSyncRowItem> rows,
            bool autoUpdateNewlyAdded = false)
        {
            var result = new List<GameInfo>(localApps);
            var localKeys = new HashSet<string>(
                result.Select(a => a.InstanceKey),
                StringComparer.OrdinalIgnoreCase);
            var localFolders = new HashSet<string>(
                result.Where(a => !string.IsNullOrWhiteSpace(a.FolderName)).Select(a => a.FolderName!),
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows.Where(r => r.CanAdd && r.External != null))
            {
                var folderName = row.External!.FolderName;
                if (!string.IsNullOrWhiteSpace(folderName) && localFolders.Contains(folderName))
                    continue;

                if (!localKeys.Add(row.IdentityKey))
                    continue;

                result.Add(CloneForLocal(row.External, autoUpdateNewlyAdded));
                if (!string.IsNullOrWhiteSpace(folderName))
                    localFolders.Add(folderName);
            }

            return result;
        }

        public static List<GameInfo> ApplyReplaceAllChanged(
            List<GameInfo> localApps,
            IReadOnlyList<CatalogSyncRowItem> rows)
        {
            var replaceRows = rows
                .Where(r => r.Status == CatalogSyncStatus.Changed && r.Local != null && r.External != null)
                .ToList();

            return localApps
                .Select(app =>
                {
                    var row = replaceRows.FirstOrDefault(r => MatchesLocalApp(app, r));
                    return row?.External != null ? ReplaceFromExternal(app, row.External) : app;
                })
                .ToList();
        }

        public static List<GameInfo> ApplyMergeAllChanged(
            List<GameInfo> localApps,
            IReadOnlyList<CatalogSyncRowItem> rows)
        {
            var mergeRows = rows
                .Where(r => r.Status == CatalogSyncStatus.Changed && r.Local != null && r.External != null)
                .ToList();

            return localApps
                .Select(app =>
                {
                    var row = mergeRows.FirstOrDefault(r => MatchesLocalApp(app, r));
                    return row?.External != null ? MergeExternalIntoLocal(app, row.External) : app;
                })
                .ToList();
        }

        public static List<GameInfo> ApplyRowAdd(
            List<GameInfo> localApps,
            CatalogSyncRowItem row,
            bool autoUpdateNewlyAdded = false)
        {
            if (row.External == null || !row.CanAdd)
                return localApps;

            var exists = localApps.Any(a =>
                string.Equals(a.InstanceKey, row.IdentityKey, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(row.External.FolderName) &&
                 string.Equals(a.FolderName, row.External.FolderName, StringComparison.OrdinalIgnoreCase)));

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
                    MatchesLocalApp(app, row)
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
                    MatchesLocalApp(app, row)
                        ? MergeExternalIntoLocal(app, row.External)
                        : app)
                .ToList();
        }

        public static List<GameInfo> ApplyRowRemove(List<GameInfo> localApps, CatalogSyncRowItem row)
        {
            if (row.Local == null)
                return localApps;

            return localApps
                .Where(app => !MatchesLocalApp(app, row))
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

        public static void ApplyReviewActionButtons(
            CatalogSyncRowItem row,
            AppCatalogSource? source,
            CatalogReviewFilter filter)
        {
            var isHiddenFilter = filter == CatalogReviewFilter.Hidden;
            row.ShowHideButton = !isHiddenFilter &&
                                 source != null &&
                                 !IsHiddenFromReview(source, row.ReviewKey);
            row.ShowCardHideButton = row.ShowHideButton && !row.CanRemoveFromLibrary;
            row.ShowUnhideButton = isHiddenFilter;
            row.ShowRemoveFromLibrary = row.CanRemoveFromLibrary && !isHiddenFilter;
        }

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
            if (IsHiddenFromReview(source, row.ReviewKey))
                return false;

            if (row.Status == CatalogSyncStatus.Unchanged)
                return false;

            if (IsIgnoredForCurrentVersion(source, row.ReviewKey))
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
                if (IsHiddenFromReview(source, row.ReviewKey))
                    continue;

                if (IsIgnoredForCurrentVersion(source, row.ReviewKey))
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
                var isHidden = IsHiddenFromReview(source, row.ReviewKey);

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
                    IsIgnoredForCurrentVersion(source, row.ReviewKey))
                    continue;

                var include = filter switch
                {
                    CatalogReviewFilter.All => true,
                    CatalogReviewFilter.NeedsReview => IsActionableRow(row, source),
                    CatalogReviewFilter.New => row.Status == CatalogSyncStatus.InExternalOnly &&
                                               !IsIgnoredForCurrentVersion(source, row.ReviewKey),
                    CatalogReviewFilter.NotInLibrary => row.Status == CatalogSyncStatus.InExternalOnly,
                    CatalogReviewFilter.Changed => row.Status == CatalogSyncStatus.Changed &&
                                                   !IsIgnoredForCurrentVersion(source, row.ReviewKey),
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
