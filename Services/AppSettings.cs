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

    /// <summary>An app list from Quiver Launcher 3.4 and older, read once to keep a player's own list.</summary>
    public class LegacyAppCatalogSource
    {
        public string Location { get; set; } = "";
        public string? RemoteLocation { get; set; }
        public bool IsCommunityManaged { get; set; }
        public bool Enabled { get; set; } = true;
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
        public int? ListRowHeight { get; set; }
        public int ActionButtonSize { get; set; } = 36;
        public bool ShowOSTopBar { get; set; } = false;
        public int InterfaceScalePercent { get; set; } = 100;
        public bool DesktopSidebarCollapsed { get; set; }
        public string PrimaryColor { get; set; } = "#18181b";
        public string SecondaryColor { get; set; } = "#404040";
        public TargetOS Platform { get; set; } = TargetOS.Auto;
        public List<string> HiddenApps { get; set; } = new List<string>();
        public List<string> ManuallyHiddenApps { get; set; } = new List<string>();
        public string AppsPath { get; set; } = string.Empty;
        public string GitHubApiToken { get; set; } = string.Empty;
        public string GitLabApiToken { get; set; } = string.Empty;
        public string SortBy { get; set; } = "LastPlayed";
        /// <summary>The player's own app list for Browse: a local JSON file or a URL, in the apps.json format.</summary>
        public string CustomAppListLocation { get; set; } = string.Empty;
        /// <summary>The App Catalog's AI filter ("no-generated" or "no-ai"), kept like the website keeps it; empty shows every app.</summary>
        public string CatalogAiFilter { get; set; } = string.Empty;
        public string ModsSortBy { get; set; } = "InstalledFirst";
        public bool ModsIncludeNsfw { get; set; }
        public List<string> DismissedAnnouncementIds { get; set; } = new List<string>();
        /// <summary>When true, the GitHub token rate-limit warning never shows again.</summary>
        public bool GitHubTokenBannerPermanentlyDismissed { get; set; }
        /// <summary>UTC time until which the GitHub token warning stays hidden after the user closed it.</summary>
        public DateTimeOffset? GitHubTokenBannerSnoozedUntilUtc { get; set; }
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

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DesktopWindowPlacement? DesktopWindowPlacement { get; set; }
        public bool CloseAfterLaunch {  get; set; } = false;
        public bool CloseToTray { get; set; }
        public int MouseWheelScrollSpeed { get; set; } = 1;
        public bool BackgroundUpdateCheckEnabled { get; set; }
        public int BackgroundUpdateCheckIntervalMinutes { get; set; } = BackgroundUpdateCheckIntervals.DefaultMinutes;
        /// <summary>When true, show modal prompts for pending library app updates.</summary>
        public bool PromptAppUpdateReviews { get; set; }
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
        /// <summary>Only read from older settings files; see <see cref="CustomAppListLocation"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<LegacyAppCatalogSource>? AppCatalogSources { get; set; }
        public bool LocalFirstCatalogMigrationComplete { get; set; }
        public List<TagDisplayFilter> TagDisplayFilters { get; set; } = new List<TagDisplayFilter>();
        public string? ActiveTagDisplayFilterId { get; set; }
        public AppListScope ListScope { get; set; } = AppListScope.AllApps;
        public Dictionary<string, List<string>> UserAppTags { get; set; } = new Dictionary<string, List<string>>();
        /// <summary>Repository → custom library display name override (when app is not in local apps.json).</summary>
        public Dictionary<string, string> UserAppDisplayNames { get; set; } = new Dictionary<string, string>();

        public void EnsureInitialized()
        {
            InterfaceScalePercent = InterfaceScale.Normalize(InterfaceScalePercent);
            ListRowHeight = Math.Clamp(ListRowHeight ?? (UseGridView ? 96 : SlotSize), 72, 400);
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

            CustomAppListLocation ??= string.Empty;
            CatalogAiFilter ??= string.Empty;
            if (AppCatalogSources != null)
            {
                // The catalog now comes from quiverlauncher.com. A list the player added themselves
                // stays available in Browse; the community lists it replaces are dropped.
                if (string.IsNullOrWhiteSpace(CustomAppListLocation))
                    CustomAppListLocation = AppCatalogSources
                        .Where(source => source is { Enabled: true, IsCommunityManaged: false })
                        .Select(source => (string.IsNullOrWhiteSpace(source.RemoteLocation) ? source.Location : source.RemoteLocation).Trim())
                        .FirstOrDefault(location => location.Length > 0 &&
                            !location.Contains("tgeorgiadis/quiver-community-app-catalog", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
                AppCatalogSources = null;
            }

            if (HiddenApps.Count > 0)
            {
                ListScope = AppListScope.InstalledOnly;
                HiddenApps.Clear();
            }

            // Legacy sort mode removed in favor of IgnoreArticlesWhenSorting.
            if (string.Equals(SortBy, "NameIgnoreArticles", StringComparison.OrdinalIgnoreCase))
                SortBy = "Name";

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
