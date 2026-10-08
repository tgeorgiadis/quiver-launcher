using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services.Mods;
using System.Net.Http;
using AppSettings = QuiverLauncher.AppSettings;
using System.Text;
using System.Text.Json;

namespace QuiverLauncher.Services
{
    public class AppCatalogService
    {
        private readonly string _appsConfigPath;
        private readonly string _legacyGamesConfigPath;
        private readonly GameManager? _gameManager;
        private readonly ICatalogLocationReader _locationReader;
        private readonly LibraryFileStore _libraryStore;

        public AppCatalogService(
            GameManager? gameManager = null,
            ICatalogLocationReader? locationReader = null,
            string? dataDirectory = null)
        {
            _gameManager = gameManager;
            _locationReader = locationReader ?? CatalogLocationReader.Default;
            var baseDir = dataDirectory ?? QuiverLauncherPaths.UserDataRoot;
            _appsConfigPath = Path.Combine(baseDir, "apps.json");
            _legacyGamesConfigPath = Path.Combine(baseDir, "games.json");
            Directory.CreateDirectory(baseDir);
            _libraryStore = new LibraryFileStore(_appsConfigPath, bytes => ParseLocalLibrary(bytes));
        }

        public string AppsConfigPath => _appsConfigPath;
        public string LibraryBackupDirectory => _libraryStore.BackupDirectory;

        // A mutation must never interpret an unreadable/corrupt library as an empty one.
        public Task<List<GameInfo>> LoadLocalAppsForMutationAsync() => LoadLocalAppsAsync();

        public async Task<List<GameInfo>> LoadLocalAppsAsync()
        {
            var bytes = await _libraryStore.ReadAsync(_legacyGamesConfigPath).ConfigureAwait(false);
            return bytes == null ? [] : ParseLocalLibrary(bytes);
        }

        public async Task ValidateAndFixLocalAppsJsonAsync()
        {
            // Validation must never rewrite the source, even to normalize formatting.
            await LoadLocalAppsAsync().ConfigureAwait(false);
        }

        private List<GameInfo> ParseLocalLibrary(byte[] bytes)
        {
            try
            {
                using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                using var document = JsonDocument.Parse(reader.ReadToEnd());
                return ParseAppsFromRootStatic(document.RootElement, _gameManager, strict: true);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                throw new JsonException($"Quiver could not read the complete library. No library data has been overwritten. " +
                    $"Close Quiver and check {_appsConfigPath} (or games.json during migration). " +
                    $"Saved backups, if available, are in {LibraryBackupDirectory}. {ex.Message}", ex);
            }
        }

        public async Task<List<GameInfo>> LoadLocalCatalogAsync(AppSettings settings)
        {
            settings.EnsureInitialized();
            var localApps = await LoadLocalAppsAsync().ConfigureAwait(false);
            foreach (var app in localApps)
            {
                app.GameManager = _gameManager;
            }

            foreach (var app in localApps)
            {
                ApplyUserAppTags(app, settings);
                ApplyUserAppDisplayNames(app, settings);
                app.LibraryNameStyle = settings.LibraryNameStyle;
                app.LibraryCardTagMaxLines = settings.LibraryCardTagMaxLines;
                app.TruncateLibraryCardTitles = settings.TruncateLibraryCardTitles;
            }

            RefreshLibraryCardTags(localApps, settings);
            return localApps;
        }

        public static void RefreshLibraryCardTags(IEnumerable<GameInfo> apps, AppSettings settings)
        {
            settings.EnsureInitialized();
            var appList = apps as IList<GameInfo> ?? apps.ToList();
            foreach (var app in appList)
            {
                app.LibraryCardTagMaxLines = settings.LibraryCardTagMaxLines;
                app.RefreshLibraryCardTags();
            }
        }

        /// <summary>Reads an app list file (a local path or a URL) in the apps.json format.</summary>
        public async Task<(List<GameInfo> Apps, string? Error)> TryLoadListAsync(
            HttpClient httpClient,
            string location,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var json = await _locationReader.ReadAsync(httpClient, location, cancellationToken).ConfigureAwait(false);
                var apps = ParseAppsFromJson(json);
                foreach (var app in apps)
                    app.GameManager = _gameManager;

                return (apps, null);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return ([], ex.Message);
            }
        }

        public static void ApplyUserAppTags(GameInfo app, AppSettings settings)
        {
            settings.EnsureInitialized();

            if (string.IsNullOrWhiteSpace(app.Repository))
            {
                app.Tags = TagHelper.NormalizeTags(app.Tags);
                return;
            }

            if (settings.UserAppTags.TryGetValue(app.Repository, out var userTags))
                app.Tags = TagHelper.NormalizeTags(userTags);
            else
                app.Tags = TagHelper.NormalizeTags(app.Tags);
        }

        public static void ApplyUserAppDisplayNames(GameInfo app, AppSettings settings)
        {
            settings.EnsureInitialized();
            app.LibraryNameStyle = settings.LibraryNameStyle;
            app.LibraryCardTagMaxLines = settings.LibraryCardTagMaxLines;
            app.TruncateLibraryCardTitles = settings.TruncateLibraryCardTitles;

            if (string.IsNullOrWhiteSpace(app.Repository))
                return;

            // Local apps.json CustomDisplayName wins; settings overlay applies when not set on the app.
            if (!string.IsNullOrWhiteSpace(app.CustomDisplayName))
                return;

            if (settings.UserAppDisplayNames.TryGetValue(app.Repository, out var custom) &&
                !string.IsNullOrWhiteSpace(custom))
            {
                app.CustomDisplayName = custom.Trim();
            }
        }

        public async Task SaveLocalAppsAsync(List<GameInfo> apps)
        {
            ArgumentNullException.ThrowIfNull(apps);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { apps = apps.Select(SerializeApp).ToList() },
                new JsonSerializerOptions { WriteIndented = true });
            await _libraryStore.SaveAsync(bytes).ConfigureAwait(false);
        }

        public async Task ExportLocalAppsToFileAsync(string exportPath, List<GameInfo>? apps = null)
        {
            apps ??= await LoadLocalAppsAsync().ConfigureAwait(false);
            if (string.Equals(Path.GetFullPath(exportPath), Path.GetFullPath(_appsConfigPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                await SaveLocalAppsAsync(apps).ConfigureAwait(false);
                return;
            }
            await WriteAppsToFileAsync(exportPath, apps).ConfigureAwait(false);
        }

        private static async Task WriteAppsToFileAsync(string path, List<GameInfo> apps)
        {
            var data = new
            {
                apps = apps.Select(SerializeApp).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    65536, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, data, options).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                // Same-directory replacement is atomic; a failed write/replacement leaves the old file intact.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void SaveLocalApps(List<GameInfo> apps)
        {
            SaveLocalAppsAsync(apps).GetAwaiter().GetResult();
        }

        public List<GameInfo> ParseAppsFromDictionary(Dictionary<string, JsonElement> gamesData)
        {
            // This parser is used by library import, so partial results are unsafe.
            return ParseLocalLibrary(JsonSerializer.SerializeToUtf8Bytes(gamesData));
        }

        public List<GameInfo> ParseAppsFromJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            return ParseAppsRoot(document.RootElement);
        }

        public static bool IsRemoteLocation(string location)
        {
            return location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveLocalPath(string location)
        {
            if (IsRemoteLocation(location) || Path.IsPathRooted(location))
                return location;

            return Path.Combine(QuiverLauncherPaths.UserDataRoot, location);
        }

        private List<GameInfo> ParseAppsRoot(JsonElement root) =>
            ParseAppsFromRootStatic(root, _gameManager);

        private static List<GameInfo> ParseAppsFromRootStatic(JsonElement root, GameManager? gameManager = null, bool strict = false)
        {
            var apps = new List<GameInfo>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                apps.AddRange(ParseAppArrayStatic(root, gameManager, strict));
                return DedupeByRepository(apps);
            }

            if (strict && (root.ValueKind != JsonValueKind.Object ||
                !new[] { "apps", "standard", "experimental", "custom" }.Any(key => root.TryGetProperty(key, out _))))
                throw new JsonException("Expected an apps array or a supported legacy library.");

            if (root.TryGetProperty("apps", out var appsArray))
                apps.AddRange(ParseAppArrayStatic(appsArray, gameManager, strict));

            foreach (var legacySection in new[] { "standard", "experimental", "custom" })
            {
                if (root.TryGetProperty(legacySection, out var legacyArray))
                    apps.AddRange(ParseAppArrayStatic(legacyArray, gameManager, strict));
            }

            return DedupeByRepository(apps);
        }

        private static List<GameInfo> DedupeByRepository(List<GameInfo> apps) =>
            apps
                .GroupBy(app => app.InstanceKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

        private static List<GameInfo> ParseAppArrayStatic(JsonElement appsArray, GameManager? gameManager, bool strict = false)
        {
            var apps = new List<GameInfo>();

            foreach (var appElement in appsArray.EnumerateArray())
            {
                try
                {
                    if (strict) ValidateLocalAppShape(appElement);
                    var rawRepositorySource = appElement.TryGetProperty("repositorySource", out var repositorySourceElement)
                        ? repositorySourceElement.GetString()
                        : null;
                    var normalizedRepositorySource = RepositorySourceHelper.Normalize(rawRepositorySource, out var unsupportedSource);
                    if (unsupportedSource)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Unsupported repositorySource '{rawRepositorySource}' for repository '{(appElement.TryGetProperty("repository", out var warnRepo) ? warnRepo.GetString() : null)}'; defaulting to GitHub.");
                    }

                    var app = new GameInfo
                    {
                        Name = (appElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null) ?? string.Empty,
                        Project = appElement.TryGetProperty("project", out var projectElement)
                            ? projectElement.GetString()
                            : null,
                        CustomDisplayName = appElement.TryGetProperty("customDisplayName", out var customNameElement)
                            ? customNameElement.GetString()
                            : null,
                        Repository = (appElement.TryGetProperty("repository", out var repoElement) ? repoElement.GetString() : null) ?? string.Empty,
                        RepositorySource = RepositorySourceHelper.IsGitHub(normalizedRepositorySource)
                            ? null
                            : normalizedRepositorySource,
                        FolderName = (appElement.TryGetProperty("folderName", out var folderElement) ? folderElement.GetString() : null) ?? string.Empty,
                        InstallPath = appElement.TryGetProperty("installPath", out var installPathElement) ? installPathElement.GetString() : null,
                        GameIconUrl = GetIconUrl(appElement),
                        PreferredVersion = appElement.TryGetProperty("preferredVersion", out var preferredVersionElement) ? preferredVersionElement.GetString() : null,
                        SkippedUpdateVersion = appElement.TryGetProperty("skippedUpdateVersion", out var skippedUpdateVersionElement) ? skippedUpdateVersionElement.GetString() : null,
                        AutoUpdate = appElement.TryGetProperty("autoUpdate", out var autoUpdateElement) &&
                                     autoUpdateElement.ValueKind == JsonValueKind.True,
                        DeferUpdateTracking = appElement.TryGetProperty("deferUpdateTracking", out var deferElement) &&
                                              deferElement.ValueKind == JsonValueKind.True,
                        Tags = ParseTagsProperty(appElement),
                        FilesToAdd = ParseFilesToAddProperty(appElement),
                        ReleaseAssetFilter = RepositorySourceHelper.NormalizeReleaseAssetFilter(
                            appElement.TryGetProperty("releaseAssetFilter", out var releaseAssetFilterElement)
                                ? releaseAssetFilterElement.GetString()
                                : null),
                        ModsPath = ParseModsPathProperty(appElement),
                        ModsSources = ParseModsSourcesProperty(appElement),
                        ModsLayout = ParseModsLayoutProperty(appElement),
                        LinuxRunner = appElement.TryGetProperty("linuxRunner", out var linuxRunnerElement)
                            ? linuxRunnerElement.GetString()
                            : null,
                        LinuxPrefixPath = appElement.TryGetProperty("linuxPrefixPath", out var linuxPrefixElement)
                            ? linuxPrefixElement.GetString()
                            : null,
                        LinuxProtonPath = appElement.TryGetProperty("linuxProtonPath", out var linuxProtonElement)
                            ? linuxProtonElement.GetString()
                            : null,
                        LinuxCustomLaunchCommand = appElement.TryGetProperty("linuxCustomLaunchCommand", out var linuxCustomElement)
                            ? linuxCustomElement.GetString()
                            : null,
                        IsExperimental = false,
                        IsCustom = true,
                        GameManager = gameManager,
                    };

                    app.CatalogSnapshot = ParseCatalogSnapshot(appElement);
                    app.CatalogEntryId = appElement.TryGetProperty("catalogEntryId", out var entryId) && entryId.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(entryId.GetString()) ? entryId.GetString()!.Trim() : null;
                    if (string.IsNullOrWhiteSpace(app.Project))
                        app.Project = null;
                    if (string.IsNullOrWhiteSpace(app.CustomDisplayName))
                        app.CustomDisplayName = null;
                    if (app.IsManuallyManaged)
                    {
                        app.Repository = string.Empty;
                        app.RepositorySource = null;
                        app.AutoUpdate = false;
                        app.PreferredVersion = null;
                        app.SkippedUpdateVersion = null;
                        app.DeferUpdateTracking = false;
                        app.ReleaseAssetFilter = null;
                    }

                    apps.Add(app);
                }
                catch (Exception ex)
                {
                    if (strict) throw new JsonException("A library app entry could not be parsed.", ex);
                    System.Diagnostics.Debug.WriteLine($"Error parsing app: {ex.Message}");
                }
            }

            return apps;
        }

        private static void ValidateLocalAppShape(JsonElement app)
        {
            if (app.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected a library app object.");
            foreach (var key in new[] { "tags", "filesToAdd" })
            {
                if (!app.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) continue;
                if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                    throw new JsonException($"Invalid {key} in a library app.");
            }
            foreach (var key in new[] { "autoUpdate", "deferUpdateTracking" })
            {
                if (app.TryGetProperty(key, out var value) && value.ValueKind is not
                    (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                    throw new JsonException($"Invalid {key} in a library app.");
            }
            if (!app.TryGetProperty("mods", out var mods) || mods.ValueKind == JsonValueKind.Null) return;
            if (mods.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid mods in a library app.");
            foreach (var key in new[] { "path", "layout" })
                if (mods.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new JsonException($"Invalid mods {key} in a library app.");
            if (!mods.TryGetProperty("sources", out var sources) || sources.ValueKind == JsonValueKind.Null) return;
            if (sources.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid mod sources in a library app.");
            foreach (var source in sources.EnumerateArray())
            {
                if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("sourceUrl", out var url) ||
                    url.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(url.GetString()))
                    throw new JsonException("Invalid mod source in a library app.");
                if (source.TryGetProperty("provider", out var provider) && provider.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new JsonException("Invalid mod provider in a library app.");
            }
        }

        private static string? GetIconUrl(JsonElement appElement)
        {
            if (appElement.TryGetProperty("appIconUrl", out var appIconUrlElement) && appIconUrlElement.ValueKind != JsonValueKind.Null)
                return appIconUrlElement.GetString();

            if (appElement.TryGetProperty("gameIconUrl", out var gameIconUrlElement) && gameIconUrlElement.ValueKind != JsonValueKind.Null)
                return gameIconUrlElement.GetString();

            if (appElement.TryGetProperty("customDefaultIconUrl", out var legacyIconElement) && legacyIconElement.ValueKind != JsonValueKind.Null)
                return legacyIconElement.GetString();

            return null;
        }

        private static CatalogSnapshot? ParseCatalogSnapshot(JsonElement appElement)
        {
            if (!appElement.TryGetProperty("catalog", out var catalog) || catalog.ValueKind != JsonValueKind.Object)
                return null;
            string? Text(string name) => catalog.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return new CatalogSnapshot(Text("name"), Text("project"), Text("appIconUrl"), ParseTagsProperty(catalog));
        }

        private static List<string> ParseTagsProperty(JsonElement appElement)
        {
            if (!appElement.TryGetProperty("tags", out var tagsElement) || tagsElement.ValueKind != JsonValueKind.Array)
                return [];

            var tags = new List<string>();
            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                if (tagElement.ValueKind == JsonValueKind.String)
                {
                    var tag = tagElement.GetString();
                    if (!string.IsNullOrWhiteSpace(tag))
                        tags.Add(tag);
                }
            }

            return TagHelper.NormalizeTags(tags);
        }

        private static List<string> ParseFilesToAddProperty(JsonElement appElement)
        {
            if (!appElement.TryGetProperty("filesToAdd", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
                return [];

            var files = new List<string>();
            foreach (var fileElement in filesElement.EnumerateArray())
            {
                if (fileElement.ValueKind == JsonValueKind.String)
                {
                    var fileName = fileElement.GetString();
                    if (!string.IsNullOrWhiteSpace(fileName))
                        files.Add(fileName);
                }
            }

            return AppFilesToAddService.Normalize(files);
        }

        private static string? ParseModsPathProperty(JsonElement appElement)
        {
            if (!appElement.TryGetProperty("mods", out var modsElement) ||
                modsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!modsElement.TryGetProperty("path", out var pathElement) ||
                pathElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var normalized = GameModsConfig.NormalizePath(pathElement.GetString());
            return normalized.Length == 0 ? null : normalized;
        }

        private static List<GameModSource> ParseModsSourcesProperty(JsonElement appElement)
        {
            if (!appElement.TryGetProperty("mods", out var modsElement) ||
                modsElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (!modsElement.TryGetProperty("sources", out var sourcesElement) ||
                sourcesElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var sources = new List<GameModSource>();
            foreach (var sourceElement in sourcesElement.EnumerateArray())
            {
                if (sourceElement.ValueKind != JsonValueKind.Object)
                    continue;

                var provider = ModProviderIds.Thunderstore;
                if (sourceElement.TryGetProperty("provider", out var providerElement) &&
                    providerElement.ValueKind == JsonValueKind.String)
                {
                    provider = GameModsConfig.NormalizeProvider(providerElement.GetString());
                }

                var sourceUrl = string.Empty;
                if (sourceElement.TryGetProperty("sourceUrl", out var urlElement) &&
                    urlElement.ValueKind == JsonValueKind.String)
                {
                    sourceUrl = urlElement.GetString()?.Trim() ?? string.Empty;
                }

                if (sourceUrl.Length == 0)
                    continue;

                sources.Add(new GameModSource
                {
                    Provider = provider,
                    SourceUrl = sourceUrl,
                });
            }

            return GameModsConfig.NormalizeSources(sources);
        }

        private static string? ParseModsLayoutProperty(JsonElement appElement)
        {
            if (!appElement.TryGetProperty("mods", out var modsElement) ||
                modsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!modsElement.TryGetProperty("layout", out var layoutElement) ||
                layoutElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var normalized = GameModsConfig.NormalizeLayout(layoutElement.GetString());
            return normalized == GameModsConfig.LayoutFlat ? null : normalized;
        }

        internal static object SerializeApp(GameInfo app)
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = app.Name,
                ["folderName"] = app.FolderName,
                ["installPath"] = app.InstallPath,
                ["appIconUrl"] = app.GameIconUrl,
            };

            if (!app.IsManuallyManaged && !string.IsNullOrWhiteSpace(app.Repository))
                payload["repository"] = app.Repository;

            if (!app.IsManuallyManaged)
            {
                payload["preferredVersion"] = app.PreferredVersion;
                payload["skippedUpdateVersion"] = app.SkippedUpdateVersion;
            }

            if (!string.IsNullOrWhiteSpace(app.Project))
                payload["project"] = app.Project.Trim();

            if (!string.IsNullOrWhiteSpace(app.CustomDisplayName))
                payload["customDisplayName"] = app.CustomDisplayName.Trim();

            if (!app.IsManuallyManaged)
            {
                var effectiveSource = RepositorySourceHelper.Normalize(app.RepositorySource);
                if (!RepositorySourceHelper.IsGitHub(effectiveSource))
                    payload["repositorySource"] = effectiveSource;

                if (app.AutoUpdate)
                    payload["autoUpdate"] = true;

                if (app.DeferUpdateTracking)
                    payload["deferUpdateTracking"] = true;
            }

            if (!string.IsNullOrWhiteSpace(app.LinuxRunner) &&
                !string.Equals(app.LinuxRunner, "auto", StringComparison.OrdinalIgnoreCase))
            {
                payload["linuxRunner"] = app.LinuxRunner;
            }

            if (!string.IsNullOrWhiteSpace(app.LinuxPrefixPath))
                payload["linuxPrefixPath"] = app.LinuxPrefixPath;

            if (!string.IsNullOrWhiteSpace(app.LinuxProtonPath))
                payload["linuxProtonPath"] = app.LinuxProtonPath;

            if (!string.IsNullOrWhiteSpace(app.LinuxCustomLaunchCommand))
                payload["linuxCustomLaunchCommand"] = app.LinuxCustomLaunchCommand;

            var normalizedTags = TagHelper.NormalizeTags(app.Tags);
            if (normalizedTags.Count > 0)
                payload["tags"] = normalizedTags;

            // What the catalog last set, so its later changes don't overwrite the player's own.
            // Older launchers ignore this field, and drop it if they save apps.json; it is filled in again from the repository.
            if (!string.IsNullOrWhiteSpace(app.CatalogEntryId))
                payload["catalogEntryId"] = app.CatalogEntryId;
            if (app.CatalogSnapshot is { } catalog)
                payload["catalog"] = new Dictionary<string, object?>
                {
                    ["name"] = catalog.Name,
                    ["project"] = catalog.Project,
                    ["appIconUrl"] = catalog.IconUrl,
                    ["tags"] = catalog.Tags,
                };

            var normalizedFilesToAdd = AppFilesToAddService.Normalize(app.FilesToAdd);
            if (normalizedFilesToAdd.Count > 0)
                payload["filesToAdd"] = normalizedFilesToAdd;

            var releaseAssetFilter = RepositorySourceHelper.NormalizeReleaseAssetFilter(app.ReleaseAssetFilter);
            if (!app.IsManuallyManaged && releaseAssetFilter != null)
                payload["releaseAssetFilter"] = releaseAssetFilter;

            var modsPath = GameModsConfig.NormalizePath(app.ModsPath);
            var modsSources = GameModsConfig.NormalizeSources(app.ModsSources);
            var modsLayout = GameModsConfig.NormalizeLayout(app.ModsLayout);
            if (modsPath.Length > 0 || modsSources.Count > 0 || modsLayout != GameModsConfig.LayoutFlat)
            {
                var modsPayload = new Dictionary<string, object?>();
                if (modsPath.Length > 0)
                    modsPayload["path"] = modsPath;
                if (modsLayout != GameModsConfig.LayoutFlat)
                    modsPayload["layout"] = modsLayout;
                if (modsSources.Count > 0)
                {
                    modsPayload["sources"] = modsSources.Select(s => new Dictionary<string, object?>
                    {
                        ["provider"] = s.Provider,
                        ["sourceUrl"] = s.SourceUrl,
                    }).ToList();
                }

                payload["mods"] = modsPayload;
            }

            return payload;
        }
    }
}
