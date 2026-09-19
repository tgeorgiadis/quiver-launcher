using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services.Mods;
using System.Net;
using System.Net.Http;
using AppSettings = QuiverLauncher.AppSettings;
using AppCatalogSource = QuiverLauncher.AppCatalogSource;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuiverLauncher.Services
{
    public class AppCatalogService
    {
        private readonly string _appsConfigPath;
        private readonly string _legacyGamesConfigPath;
        private readonly string _catalogSourcesCacheFolder;
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
            _catalogSourcesCacheFolder = Path.Combine(baseDir, "Cache", "CatalogSources");
            Directory.CreateDirectory(_catalogSourcesCacheFolder);
            _libraryStore = new LibraryFileStore(_appsConfigPath, bytes => ParseLocalLibrary(bytes));
        }

        public string AppsConfigPath => _appsConfigPath;
        public string CatalogSourcesCacheFolder => _catalogSourcesCacheFolder;
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
                app.CatalogSourceId = null;
                app.GameManager = _gameManager;
            }

            foreach (var app in localApps)
            {
                ApplyUserAppTags(app, settings);
                ApplyUserAppDisplayNames(app, settings);
                app.LibraryNameStyle = settings.LibraryNameStyle;
                app.ShowLibraryUpdateBadges = settings.ShowLibraryAppUpdateBadges;
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

        public async Task RefreshAllSourcesAsync(HttpClient httpClient, AppSettings settings, CancellationToken cancellationToken = default)
        {
            settings.EnsureInitialized();

            var bootstrap = new CommunityCatalogBootstrap(_locationReader);
            await bootstrap.SyncCommunitySourcesFromIndexAsync(httpClient, settings, cancellationToken, forcePlatformRefresh: true).ConfigureAwait(false);

            foreach (var source in settings.AppCatalogSources.Where(s => s.Enabled))
                await FetchSourceAsync(httpClient, source, cancellationToken).ConfigureAwait(false);
        }

        public bool HasSourceCache(string sourceId) =>
            File.Exists(GetSourceCachePath(sourceId));

        public async Task<CommunityCatalogSyncResult> EnsureCommunitySourcesCachedAsync(
            HttpClient httpClient,
            AppSettings settings)
        {
            settings.EnsureInitialized();

            var bootstrap = new CommunityCatalogBootstrap(_locationReader);
            var syncResult = await bootstrap.SyncCommunitySourcesFromIndexAsync(httpClient, settings).ConfigureAwait(false);

            foreach (var source in settings.AppCatalogSources.Where(s => s.IsCommunityManaged && s.Enabled))
            {
                if (HasSourceCache(source.Id))
                    continue;

                await FetchSourceAsync(httpClient, source).ConfigureAwait(false);
            }

            return syncResult;
        }

        [Obsolete("Use EnsureCommunitySourcesCachedAsync instead.")]
        public Task EnsureDefaultCommunitySourceFetchedAsync(HttpClient httpClient, AppSettings settings) =>
            EnsureCommunitySourcesCachedAsync(httpClient, settings);

        public async Task<(List<GameInfo> Apps, string? Version, string? Error)> TryLoadSourceAsync(
            HttpClient httpClient,
            string location)
        {
            try
            {
                var json = await _locationReader.ReadAsync(httpClient, location).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var version = ResolveListVersion(document.RootElement, out var apps);
                foreach (var app in apps)
                    app.GameManager = _gameManager;

                return (apps, version, null);
            }
            catch (Exception ex)
            {
                return ([], null, ex.Message);
            }
        }

        public async Task<bool> FetchSourceAsync(HttpClient httpClient, AppCatalogSource source, CancellationToken cancellationToken = default)
        {
            var cachePath = GetSourceCachePath(source.Id);
            var locations = GetFetchLocationCandidates(source);

            Exception? lastError = null;
            foreach (var location in locations)
            {
                try
                {
                    var json = await _locationReader.ReadAsync(httpClient, location, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);

                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    var version = ResolveListVersion(root, out _);
                    ApplyListMetadata(source, root);

                    source.LastFetchedUtc = DateTime.UtcNow;
                    source.LastError = null;
                    source.CachedListVersion = version;
                    CatalogCompareService.PruneIgnoredChanges(source);
                    await RefreshUpdateAvailableAsync(source).ConfigureAwait(false);

                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (File.Exists(cachePath))
            {
                source.LastError = FormatCatalogFetchError(lastError, usingCache: true);
                await ApplyCachedVersionMetadataAsync(source).ConfigureAwait(false);
                return true;
            }

            source.LastError = FormatCatalogFetchError(lastError, usingCache: false);
            return false;
        }

        public static string FormatCatalogFetchError(Exception? error, bool usingCache)
        {
            if (LooksLikeNotFound(error))
                return usingCache ? "List not found (kept last copy)" : "List not found";

            var message = string.IsNullOrWhiteSpace(error?.Message)
                ? "Failed to fetch catalog source."
                : error.Message;
            return usingCache ? $"{message} (using cached copy)" : message;
        }

        public static string FormatCatalogFetchError(string? lastError)
        {
            if (string.IsNullOrEmpty(lastError))
                return lastError ?? "";

            if (!LooksLikeNotFoundMessage(lastError))
                return lastError;

            var usingCache = lastError.Contains("using cached copy", StringComparison.OrdinalIgnoreCase)
                || lastError.Contains("kept last copy", StringComparison.OrdinalIgnoreCase);
            return usingCache ? "List not found (kept last copy)" : "List not found";
        }

        private static bool LooksLikeNotFound(Exception? error)
        {
            if (error is HttpRequestException http && http.StatusCode == HttpStatusCode.NotFound)
                return true;

            return LooksLikeNotFoundMessage(error?.Message);
        }

        private static bool LooksLikeNotFoundMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.Contains("404", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Not Found", StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<string> GetFetchLocationCandidates(AppCatalogSource source)
        {
            if (!string.IsNullOrWhiteSpace(source.RemoteLocation))
                return [source.RemoteLocation.Trim()];

            if (!string.IsNullOrWhiteSpace(source.Location))
                return [source.Location.Trim()];

            return [];
        }

        public async Task RegisterNewSourceAsync(
            HttpClient httpClient,
            AppCatalogSource source,
            string? rawJson = null)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                rawJson = await _locationReader.ReadAsync(httpClient, source.Location).ConfigureAwait(false);
            }

            var cachePath = GetSourceCachePath(source.Id);
            await File.WriteAllTextAsync(cachePath, rawJson).ConfigureAwait(false);

            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            var version = ResolveListVersion(root, out _);
            ApplyListMetadata(source, root);

            source.LastFetchedUtc = DateTime.UtcNow;
            source.LastError = null;
            source.CachedListVersion = version;
            source.AcknowledgedListVersion = version;
            source.UpdateAvailable = false;
        }

        public void AcknowledgeSourceVersion(AppCatalogSource source)
        {
            source.AcknowledgedListVersion = source.CachedListVersion;
            source.UpdateAvailable = false;
            source.IgnoredChangesAtVersion?.Clear();
        }

        public async Task RefreshSourceUsageStatsAsync(AppCatalogSource source)
        {
            var localApps = await LoadLocalAppsAsync().ConfigureAwait(false);
            var externalApps = await LoadCachedAppsAsync(source.Id).ConfigureAwait(false);
            var (usingCount, totalCount) = CatalogCompareService.ComputeLibraryUsageStats(localApps, externalApps);
            source.LibraryAppCount = usingCount;
            source.ListAppCount = totalCount;
        }

        public async Task RefreshAllSourcesUsageStatsAsync(AppSettings settings)
        {
            using var timing = QuiverLauncher.Core.Services.CatalogPerformance.Measure("sources-usage", settings.AppCatalogSources.Count);
            settings.EnsureInitialized();
            var localApps = await LoadLocalAppsForMutationAsync().ConfigureAwait(false);
            foreach (var source in settings.AppCatalogSources.ToArray())
            {
                // Membership and review status must describe the same saved snapshot.
                // A missing/unreadable cache is not evidence that review is complete.
                var path = GetSourceCachePath(source.Id);
                if (!File.Exists(path)) continue;
                try
                {
                    var externalApps = await LoadAppsFromFileAsync(path, throwOnError: true).ConfigureAwait(false);
                    RefreshUpdateAvailable(source, localApps, externalApps);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // One damaged source must not suppress every subsequent summary.
                    System.Diagnostics.Debug.WriteLine($"Catalog usage refresh skipped an unreadable source ({ex.GetType().Name}).");
                }
            }
        }

        public async Task RefreshUpdateAvailableAsync(AppCatalogSource source)
        {
            var localApps = await LoadLocalAppsAsync().ConfigureAwait(false);
            var externalApps = await LoadCachedAppsAsync(source.Id).ConfigureAwait(false);
            RefreshUpdateAvailable(source, localApps, externalApps);
        }

        public void RefreshUpdateAvailable(
            AppCatalogSource source,
            List<GameInfo> localApps,
            List<GameInfo> externalApps)
        {
            (source.LibraryAppCount, source.ListAppCount) =
                CatalogCompareService.ComputeLibraryUsageStats(localApps, externalApps);

            var rows = CatalogCompareService.BuildCompareRows(localApps, externalApps);
            CatalogReviewEligibility.Reconcile(source, rows, _gameManager?.CurrentSettings);
        }

        /// <summary>
        /// Sets <see cref="GameInfo.HasPendingCatalogChanges"/> for library apps that have
        /// actionable catalog field changes (not new-in-catalog-only rows).
        /// </summary>
        public async Task ApplyPendingCatalogChangeFlagsAsync(
            IEnumerable<GameInfo> libraryGames,
            AppSettings settings)
        {
            settings.EnsureInitialized();
            var games = libraryGames as IList<GameInfo> ?? libraryGames.ToList();
            foreach (var game in games)
                game.HasPendingCatalogChanges = false;

            var byInstance = games
                .GroupBy(g => g.InstanceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            if (byInstance.Count == 0)
                return;

            var localApps = await LoadLocalAppsAsync().ConfigureAwait(false);
            foreach (var source in settings.AppCatalogSources.Where(s => s.Enabled))
            {
                var externalApps = await LoadCachedAppsAsync(source.Id).ConfigureAwait(false);
                var rows = CatalogCompareService.BuildCompareRows(localApps, externalApps);
                foreach (var row in rows)
                {
                    if (row.Status != CatalogSyncStatus.Changed)
                        continue;
                    if (!CatalogReviewEligibility.IsPending(row, source, [CatalogPlatformSupport.DetectRuntimePlatform()],
                            (row.External ?? row.Local)?.GetReleaseApiToken(settings)))
                        continue;
                    if (string.IsNullOrWhiteSpace(row.IdentityKey))
                        continue;
                    if (!byInstance.TryGetValue(row.IdentityKey, out var matches))
                        continue;

                    foreach (var game in matches)
                        game.HasPendingCatalogChanges = true;
                }
            }
        }

        /// <summary>
        /// First enabled catalog source that has an actionable Changed row for this library app.
        /// </summary>
        public async Task<string?> FindPendingCatalogSourceIdAsync(GameInfo game, AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(game);
            ArgumentNullException.ThrowIfNull(settings);
            settings.EnsureInitialized();

            if (string.IsNullOrWhiteSpace(game.InstanceKey))
                return null;

            var instanceKey = game.InstanceKey;
            var localApps = await LoadLocalAppsAsync().ConfigureAwait(false);
            foreach (var source in settings.AppCatalogSources.Where(s => s.Enabled))
            {
                var externalApps = await LoadCachedAppsAsync(source.Id).ConfigureAwait(false);
                var rows = CatalogCompareService.BuildCompareRows(localApps, externalApps);
                var match = rows.FirstOrDefault(row =>
                    row.Status == CatalogSyncStatus.Changed &&
                    CatalogReviewEligibility.IsPending(row, source, [CatalogPlatformSupport.DetectRuntimePlatform()],
                        (row.External ?? row.Local)?.GetReleaseApiToken(settings)) &&
                    string.Equals(row.IdentityKey, instanceKey, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return source.Id;
            }

            return null;
        }

        public async Task IgnoreRepositoryInMatchingSourcesAsync(AppSettings settings, string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
                return;

            settings.EnsureInitialized();
            foreach (var source in settings.AppCatalogSources)
            {
                if (!source.Enabled)
                    continue;

                var externalApps = await LoadCachedAppsAsync(source.Id).ConfigureAwait(false);
                var matches = externalApps.Any(app =>
                    !string.IsNullOrWhiteSpace(app.Repository) &&
                    app.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));
                if (!matches)
                    continue;

                if (!string.IsNullOrWhiteSpace(source.CachedListVersion))
                    CatalogCompareService.IgnoreChangesForCurrentVersion(source, repository);

                await RefreshUpdateAvailableAsync(source).ConfigureAwait(false);
            }
        }

        public static bool TryAutoAcknowledgeIfReviewComplete(AppCatalogSource source, int pendingCount)
        {
            if (pendingCount > 0)
                return false;

            if (string.IsNullOrWhiteSpace(source.CachedListVersion))
                return false;

            if (CatalogCompareService.IsReviewedVersion(source.AcknowledgedListVersion) &&
                string.Equals(source.CachedListVersion, source.AcknowledgedListVersion, StringComparison.Ordinal))
                return false;

            source.AcknowledgedListVersion = source.CachedListVersion;
            source.UpdateAvailable = false;
            source.PendingReviewCount = 0;
            return true;
        }

        public async Task<List<GameInfo>> LoadCachedAppsAsync(string sourceId)
        {
            var path = GetSourceCachePath(sourceId);
            if (!File.Exists(path))
                return [];

            return await LoadAppsFromFileAsync(path).ConfigureAwait(false);
        }

        public void DeleteSourceCache(string sourceId)
        {
            var cachePath = GetSourceCachePath(sourceId);
            if (File.Exists(cachePath))
                File.Delete(cachePath);

            var legacyAcceptedPath = GetLegacyAcceptedCachePath(sourceId);
            if (File.Exists(legacyAcceptedPath))
                File.Delete(legacyAcceptedPath);
        }

        public static bool MigrateLegacyCatalogSources(AppSettings settings, string? cacheFolder = null)
        {
            settings.EnsureInitialized();
            CommunityCatalogBootstrap.MigrateLegacyDefaultSource(settings);

            cacheFolder ??= Path.Combine(QuiverLauncherPaths.CacheDirectory, "CatalogSources");
            Directory.CreateDirectory(cacheFolder);

            var changed = false;
            foreach (var source in settings.AppCatalogSources)
            {
                var legacyAcceptedPath = Path.Combine(cacheFolder, $"{source.Id}.accepted.json");
                if (File.Exists(legacyAcceptedPath))
                {
                    File.Delete(legacyAcceptedPath);
                    changed = true;
                }

                var cachePath = Path.Combine(cacheFolder, $"{source.Id}.json");
                if (string.IsNullOrWhiteSpace(source.CachedListVersion) && File.Exists(cachePath))
                {
                    try
                    {
                        var json = File.ReadAllText(cachePath);
                        using var document = JsonDocument.Parse(json);
                        source.CachedListVersion = ResolveListVersion(document.RootElement, out _);
                        changed = true;
                    }
                    catch
                    {
                        source.CachedListVersion = "0";
                        changed = true;
                    }
                }

                if (source.AcknowledgedListVersion == "0")
                {
                    source.AcknowledgedListVersion = null;
                    changed = true;
                }

                var updateAvailable = !string.IsNullOrWhiteSpace(source.CachedListVersion) &&
                    (!CatalogCompareService.IsReviewedVersion(source.AcknowledgedListVersion) ||
                     !string.Equals(
                         source.CachedListVersion,
                         source.AcknowledgedListVersion,
                         StringComparison.Ordinal));

                if (source.UpdateAvailable != updateAvailable)
                {
                    source.UpdateAvailable = updateAvailable;
                    changed = true;
                }
            }

            return changed;
        }

        public async Task<List<GameInfo>> GetExternalOnlyAppsForSourceAsync(string sourceId, List<GameInfo>? localApps = null)
        {
            localApps ??= await LoadLocalAppsAsync().ConfigureAwait(false);
            var localKeys = new HashSet<string>(
                localApps.Select(a => a.InstanceKey),
                StringComparer.OrdinalIgnoreCase);

            var externalApps = await LoadCachedAppsAsync(sourceId).ConfigureAwait(false);
            return externalApps
                .Where(a => !string.IsNullOrWhiteSpace(a.InstanceKey) && !localKeys.Contains(a.InstanceKey))
                .ToList();
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
            app.ShowLibraryUpdateBadges = settings.ShowLibraryAppUpdateBadges;
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

        public async Task PromoteAppsToLocalAsync(IEnumerable<GameInfo> apps, bool autoUpdateNewlyAdded = false)
        {
            var localApps = await LoadLocalAppsAsync().ConfigureAwait(false);
            var localKeys = new HashSet<string>(
                localApps.Select(a => a.InstanceKey),
                StringComparer.OrdinalIgnoreCase);
            var localFolders = new HashSet<string>(
                localApps
                    .Where(a => !string.IsNullOrWhiteSpace(a.FolderName))
                    .Select(a => a.FolderName!),
                StringComparer.OrdinalIgnoreCase);

            foreach (var app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.InstanceKey) || localKeys.Contains(app.InstanceKey))
                    continue;
                if (!string.IsNullOrWhiteSpace(app.FolderName) && localFolders.Contains(app.FolderName))
                    continue;

                localApps.Add(CatalogCompareService.CloneForLocal(app, autoUpdateNewlyAdded));
                localKeys.Add(app.InstanceKey);
                if (!string.IsNullOrWhiteSpace(app.FolderName))
                    localFolders.Add(app.FolderName);
            }

            await SaveLocalAppsAsync(localApps).ConfigureAwait(false);
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

        public static string ComputeCatalogContentHash(IEnumerable<GameInfo> apps)
        {
            var entries = apps
                .Where(a => !string.IsNullOrWhiteSpace(a.IdentityKey))
                .Select(a => string.Join("|",
                    a.InstanceKey,
                    a.IdentityKey,
                    a.Repository?.Trim() ?? "",
                    a.Name ?? "",
                    a.Project ?? "",
                    a.FolderName ?? "",
                    a.InstallPath ?? "",
                    a.GameIconUrl ?? "",
                    a.PreferredVersion ?? "",
                    a.SkippedUpdateVersion ?? "",
                    RepositorySourceHelper.NormalizeReleaseAssetFilter(a.ReleaseAssetFilter) ?? "",
                    TagHelper.FormatTagsForDisplay(a.Tags),
                    AppFilesToAddService.FormatForDisplay(a.FilesToAdd)))
                .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

            var payload = string.Join("\n", entries);
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes);
        }

        public static CatalogDiff GetCatalogDiff(List<GameInfo> baselineApps, List<GameInfo> remoteApps)
        {
            var baselineByRepo = baselineApps
                .Where(a => !string.IsNullOrWhiteSpace(a.Repository))
                .GroupBy(a => a.InstanceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var remoteByRepo = remoteApps
                .Where(a => !string.IsNullOrWhiteSpace(a.Repository))
                .GroupBy(a => a.InstanceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var diff = new CatalogDiff();

            foreach (var (repo, remote) in remoteByRepo)
            {
                if (!baselineByRepo.ContainsKey(repo))
                    diff.Added.Add(remote);
                else if (!AreCatalogFieldsEquivalent(baselineByRepo[repo], remote))
                    diff.Changed.Add(remote);
            }

            foreach (var (repo, baseline) in baselineByRepo)
            {
                if (!remoteByRepo.ContainsKey(repo))
                    diff.Removed.Add(baseline);
            }

            return diff;
        }

        /// <summary>
        /// Catalog-vs-catalog equivalence. <see cref="GameInfo.FolderName"/>,
        /// <see cref="GameInfo.InstallPath"/>, and <see cref="GameInfo.PreferredVersion"/>
        /// are excluded (folder mapping and version pins are local-owned). Tag sets must
        /// match exactly so catalog authors adding or removing tags still count as a change.
        /// Library review uses <see cref="CatalogCompareService.IsLibrarySyncedWithCatalog"/>
        /// which allows extra local tags.
        /// </summary>
        public static bool AreCatalogFieldsEquivalent(GameInfo a, GameInfo b) =>
            string.Equals(a.Repository ?? "", b.Repository ?? "", StringComparison.OrdinalIgnoreCase) &&
            (a.IsManuallyManaged || b.IsManuallyManaged ||
             string.Equals(a.EffectiveRepositorySource, b.EffectiveRepositorySource, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Project ?? "", b.Project ?? "", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.GameIconUrl ?? "", b.GameIconUrl ?? "", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(TagHelper.FormatTagsForDisplay(a.Tags), TagHelper.FormatTagsForDisplay(b.Tags), StringComparison.OrdinalIgnoreCase) &&
            AppFilesToAddService.AreEquivalent(a.FilesToAdd, b.FilesToAdd) &&
            string.Equals(
                RepositorySourceHelper.NormalizeReleaseAssetFilter(a.ReleaseAssetFilter) ?? "",
                RepositorySourceHelper.NormalizeReleaseAssetFilter(b.ReleaseAssetFilter) ?? "",
                StringComparison.OrdinalIgnoreCase) &&
            GameModsConfig.AreEquivalent(
                a.ModsPath, a.ModsSources, a.ModsLayout,
                b.ModsPath, b.ModsSources, b.ModsLayout);

        private async Task ApplyCachedVersionMetadataAsync(AppCatalogSource source)
        {
            var cachePath = GetSourceCachePath(source.Id);
            if (!File.Exists(cachePath))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                source.CachedListVersion = ResolveListVersion(root, out _);
                ApplyListMetadata(source, root);
                CatalogCompareService.PruneIgnoredChanges(source);
                await RefreshUpdateAvailableAsync(source).ConfigureAwait(false);
            }
            catch
            {
                // Keep existing metadata when cache is unreadable.
            }
        }

        public static void ApplyListMetadata(AppCatalogSource source, JsonElement root)
        {
            source.IconUrl = root.TryGetProperty("iconUrl", out var iconElement) &&
                iconElement.ValueKind == JsonValueKind.String
                    ? CatalogListMetadata.NormalizeIconUrl(iconElement.GetString())
                    : null;

            if (root.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String)
            {
                var name = nameElement.GetString()?.Trim();
                if (!string.IsNullOrEmpty(name))
                    source.Name = name;
            }

            if (string.IsNullOrWhiteSpace(source.Name))
                source.Name = CatalogListMetadata.DeriveDisplayNameFromLocation(source.Location);

            if (root.TryGetProperty("description", out var descriptionElement) &&
                descriptionElement.ValueKind == JsonValueKind.String)
            {
                source.Description = descriptionElement.GetString()?.Trim() ?? "";
            }

            source.FeaturedTags = ParseTagArrayProperty(root, "featuredTags");
            source.PreferredTagFilters = ParseTagArrayProperty(root, "preferredTagFilters");
            source.HiddenTagFilters = ParseTagArrayProperty(root, "hiddenTagFilters");
        }

        private static List<string> ParseTagArrayProperty(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var tagsElement) ||
                tagsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

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

        private static string ResolveListVersion(JsonElement root, out List<GameInfo> apps)
        {
            apps = ParseAppsFromRootStatic(root);
            if (root.TryGetProperty("version", out var versionElement) &&
                versionElement.ValueKind == JsonValueKind.String)
            {
                var version = versionElement.GetString()?.Trim();
                if (!string.IsNullOrEmpty(version))
                    return version;
            }

            return ComputeCatalogContentHash(apps);
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

        private string GetSourceCachePath(string sourceId) =>
            Path.Combine(_catalogSourcesCacheFolder, $"{sourceId}.json");

        private string GetLegacyAcceptedCachePath(string sourceId) =>
            Path.Combine(_catalogSourcesCacheFolder, $"{sourceId}.accepted.json");

        private async Task<List<GameInfo>> LoadAppsFromFileAsync(string path, bool throwOnError = false)
        {
            using var timing = QuiverLauncher.Core.Services.CatalogPerformance.Measure("catalog-read");
            try
            {
                string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var apps = ParseAppsRoot(document.RootElement);
                foreach (var app in apps)
                    app.GameManager = _gameManager;

                return apps;
            }
            catch (Exception ex) when (!throwOnError)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading {Path.GetFileName(path)}: {ex.Message}");
                return [];
            }
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
