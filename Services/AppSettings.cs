using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher
{
    public enum AppListScope
    {
        AllApps,
        InstalledOnly,
        HiddenOnly,
    }

    public enum TagFilterMatchMode
    {
        Any,
        All,
    }

    public enum LibraryNameStyle
    {
        NameOnly = 0,
        /// <summary>Title is name; project shown on a separate line under the title.</summary>
        NameAndProject = 1,
        ProjectOnly = 2,
        /// <summary>Title is "Name (Project)".</summary>
        NameAndProjectInTitle = 3,
    }

    public enum LibraryTagDisplayMode
    {
        Featured = 0,
        All = 1,
        Hidden = 2,
    }

    public class TagDisplayFilter
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public TagFilterMatchMode MatchMode { get; set; } = TagFilterMatchMode.Any;
        public List<string> ExcludeTags { get; set; } = new List<string>();
        public TagFilterMatchMode ExcludeMatchMode { get; set; } = TagFilterMatchMode.Any;
    }

    public class AppCatalogSource
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Location { get; set; } = "";
        public string? RemoteLocation { get; set; }
        public bool IsCommunityManaged { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime? LastFetchedUtc { get; set; }
        public string? LastError { get; set; }
        public string? CachedListVersion { get; set; }
        public string? AcknowledgedListVersion { get; set; }
        public bool UpdateAvailable { get; set; }

        /// <summary>
        /// Optional curated quick-filter tags declared by the catalog list JSON (<c>featuredTags</c>).
        /// Used as the review-chip pin list when <see cref="PreferredTagFilters"/> is empty.
        /// </summary>
        public List<string> FeaturedTags { get; set; } = new List<string>();

        /// <summary>
        /// Optional review-chip tags that should appear first for this list (<c>preferredTagFilters</c>).
        /// </summary>
        public List<string> PreferredTagFilters { get; set; } = new List<string>();

        /// <summary>
        /// Optional tags that must never appear as review-filter chips for this list (<c>hiddenTagFilters</c>).
        /// </summary>
        public List<string> HiddenTagFilters { get; set; } = new List<string>();

        [JsonIgnore]
        public int PendingReviewCount { get; set; }

        [JsonIgnore]
        public int LibraryAppCount { get; set; }

        [JsonIgnore]
        public int ListAppCount { get; set; }

        /// <summary>
        /// Repository → cached list version when the user chose to ignore external changes for that app.
        /// </summary>
        public Dictionary<string, string> IgnoredChangesAtVersion { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Repositories the user chose to hide from review until unhidden.
        /// </summary>
        public List<string> HiddenFromReviewRepositories { get; set; } = new();
    }

    public class AppSettings
    {
        public bool FirstStartup { get; set; } = true;
        public bool IconFill { get; set; } = false;
        public bool UseGridView { get; set; } = true;
        public bool GridCompactCards { get; set; } = false;
        public float IconOpacity { get; set; } = 1.0f;
        public int IconSize { get; set; } = 124;
        public int IconMargin { get; set; } = 0;
        public int SlotTextMargin { get; set; } = 0;
        public int SlotSize { get; set; } = 180;
        public int ActionButtonSize { get; set; } = 36;
        public bool ShowOSTopBar { get; set; } = false;
        public string PrimaryColor { get; set; } = "#18181b";
        public string SecondaryColor { get; set; } = "#404040";
        public TargetOS Platform { get; set; } = TargetOS.Auto;
        public List<string> HiddenApps { get; set; } = new List<string>();
        public List<string> ManuallyHiddenApps { get; set; } = new List<string>();
        public string AppsPath { get; set; } = string.Empty;
        public string GitHubApiToken { get; set; } = string.Empty;
        public string GitLabApiToken { get; set; } = string.Empty;
        /// <summary>
        /// Base URL of the local Kubo (go-ipfs) node's HTTP RPC API used for "ipfs" apps and
        /// catalog sources. Empty means use the default (<see cref="QuiverLauncher.Core.Services.IpfsSettings.DefaultApiBaseUrl"/>)
        /// or the QUIVERLAUNCHER_IPFS_API_URL environment variable.
        /// </summary>
        public string IpfsApiUrl { get; set; } = string.Empty;
        public string SortBy { get; set; } = "LastPlayed";
        public string CatalogReviewSortBy { get; set; } = "Name";
        /// <summary>Catalog review browse layout. Independent of library <see cref="UseGridView"/>.</summary>
        public bool CatalogReviewUseGridView { get; set; } = true;
        public string ModsSortBy { get; set; } = "InstalledFirst";
        public bool ModsIncludeNsfw { get; set; }
        public List<string> DismissedAnnouncementIds { get; set; } = new List<string>();
        public bool IgnoreArticlesWhenSorting { get; set; } = true;
        public LibraryNameStyle LibraryNameStyle { get; set; } = LibraryNameStyle.NameAndProject;
        /// <summary>When true, library card name and project ellipsize and marquee on hover or focus.</summary>
        public bool TruncateLibraryCardTitles { get; set; } = true;
        public LibraryTagDisplayMode LibraryTagDisplayMode { get; set; } = LibraryTagDisplayMode.Featured;
        /// <summary>
        /// Max wrapped lines of tags on each library card. 0 = hidden; 99 = no limit.
        /// </summary>
        public int LibraryCardTagMaxLines { get; set; } = TagChipHelper.DefaultLibraryCardTagMaxLines;
        /// <summary>
        /// True after 0 was remapped from “no limit” to “hidden” (and old unlimited 0 became 99).
        /// </summary>
        public bool LibraryCardTagZeroMeansHidden { get; set; } = true;
        /// <summary>User-pinned tags preferred for quick-filter chips when present in the current set.</summary>
        public List<string> PinnedFilterTags { get; set; } = new List<string>();
        public bool StartFullscreen { get; set; } = false;
        public bool CloseAfterLaunch {  get; set; } = false;
        public bool CloseToTray { get; set; }
        public bool BackgroundUpdateCheckEnabled { get; set; }
        public int BackgroundUpdateCheckIntervalMinutes { get; set; } = BackgroundUpdateCheckIntervals.DefaultMinutes;
        /// <summary>When true, show modal prompts when catalog sources have reviewable updates.</summary>
        public bool PromptCatalogUpdates { get; set; }
        /// <summary>When true, show modal prompts for pending library app updates.</summary>
        public bool PromptAppUpdateReviews { get; set; }
        /// <summary>When true, show a small update badge on library cards with available updates.</summary>
        public bool ShowLibraryAppUpdateBadges { get; set; } = true;
        /// <summary>
        /// When true, Velopack also considers GitHub prereleases for Quiver self-updates.
        /// Intended for development; default is off. RC installs (version contains '-') still follow prereleases.
        /// </summary>
        public bool AllowPrereleaseLauncherUpdates { get; set; }
        public bool AutoUpdateNewlyAddedApps { get; set; }
        public string BackgroundImagePath { get; set; } = string.Empty;
        public string LauncherMusicPath { get; set; } = string.Empty;
        public float MusicVolume { get; set; } = 0.2f;
        public float BackgroundOpacity { get; set; } = 0.15f;
        public bool EnableGamepadInput { get; set; } = true;
        public Dictionary<GamepadAction, List<GamepadBinding>> GamepadBindings { get; set; } =
            GamepadBindingDefaults.Create();
        public Dictionary<GamepadAction, List<KeyboardBinding>> KeyboardBindings { get; set; } =
            KeyboardBindingDefaults.Create();
        public string LinuxWindowsLaunchCommand { get; set; } = string.Empty;
        public List<AppCatalogSource> AppCatalogSources { get; set; } = new List<AppCatalogSource>();
        public bool LocalFirstCatalogMigrationComplete { get; set; }
        public List<TagDisplayFilter> TagDisplayFilters { get; set; } = new List<TagDisplayFilter>();
        public string? ActiveTagDisplayFilterId { get; set; }
        public AppListScope ListScope { get; set; } = AppListScope.AllApps;
        public Dictionary<string, List<string>> UserAppTags { get; set; } = new Dictionary<string, List<string>>();
        /// <summary>Repository → custom library display name override (when app is not in local apps.json).</summary>
        public Dictionary<string, string> UserAppDisplayNames { get; set; } = new Dictionary<string, string>();

        public void EnsureInitialized()
        {
            AppCatalogSources ??= new List<AppCatalogSource>();
            HiddenApps ??= new List<string>();
            ManuallyHiddenApps ??= new List<string>();
            TagDisplayFilters ??= new List<TagDisplayFilter>();
            PinnedFilterTags ??= new List<string>();
            UserAppTags ??= new Dictionary<string, List<string>>();
            UserAppDisplayNames ??= new Dictionary<string, string>();
            DismissedAnnouncementIds ??= new List<string>();
            GamepadBindings ??= GamepadBindingDefaults.Create();
            GamepadBindingDefaults.EnsureComplete(GamepadBindings);
            KeyboardBindings ??= KeyboardBindingDefaults.Create();
            KeyboardBindingDefaults.EnsureComplete(KeyboardBindings);

            foreach (var source in AppCatalogSources)
            {
                source.IgnoredChangesAtVersion ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                source.HiddenFromReviewRepositories ??= new List<string>();
                source.FeaturedTags ??= new List<string>();
                source.PreferredTagFilters ??= new List<string>();
                source.HiddenTagFilters ??= new List<string>();
            }

            if (HiddenApps.Count > 0)
            {
                ListScope = AppListScope.InstalledOnly;
                HiddenApps.Clear();
            }

            // Legacy sort mode removed in favor of IgnoreArticlesWhenSorting.
            if (string.Equals(SortBy, "NameIgnoreArticles", StringComparison.OrdinalIgnoreCase))
                SortBy = "Name";
            if (string.Equals(CatalogReviewSortBy, "NameIgnoreArticles", StringComparison.OrdinalIgnoreCase))
                CatalogReviewSortBy = "Name";

            BackgroundUpdateCheckIntervalMinutes =
                BackgroundUpdateCheckIntervals.Normalize(BackgroundUpdateCheckIntervalMinutes);

            if (!LibraryCardTagZeroMeansHidden)
            {
                if (LibraryTagDisplayMode == LibraryTagDisplayMode.Hidden)
                    LibraryCardTagMaxLines = 0;
                else if (LibraryCardTagMaxLines == 0)
                    LibraryCardTagMaxLines = TagChipHelper.UnlimitedLibraryCardTagMaxLines;

                LibraryCardTagZeroMeansHidden = true;
            }

            LibraryCardTagMaxLines = TagChipHelper.NormalizeLibraryCardTagMaxLines(LibraryCardTagMaxLines);
        }

        public static AppSettings Load() => SettingsStoreProvider.Default.Load();

        public static void Save(AppSettings settings) => SettingsStoreProvider.Default.Save(settings);
    }
}

