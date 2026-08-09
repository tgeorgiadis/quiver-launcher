using System.Collections.Concurrent;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    public class GameVersionCache
    {
        public string Version { get; set; } = string.Empty;
        public DateTime LastChecked { get; set; }
        public string ETag { get; set; } = string.Empty;
        public GitHubRelease? CachedRelease { get; set; }
        public DateTime LastUpdateCheck { get; set; }
    }

    public static class GitHubApiCache
    {
        private static readonly ConcurrentDictionary<string, GameVersionCache> _cache = new();
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);
        private static readonly TimeSpan InstalledGameUpdateInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan NotInstalledGameUpdateInterval = TimeSpan.FromHours(24);
        private static string? _cacheFilePath;

        public static string GetCacheKey(string? repositorySource, string repository) =>
            RepositorySourceHelper.GetIdentityKey(repositorySource, repository);

        public static void Initialize(string cacheDirectory)
        {
            _cacheFilePath = Path.Combine(cacheDirectory, "version_cache.json");
            LoadFromDisk();
        }

        private static void LoadFromDisk()
        {
            if (string.IsNullOrEmpty(_cacheFilePath) || !File.Exists(_cacheFilePath))
                return;

            try
            {
                var json = File.ReadAllText(_cacheFilePath);
                var diskCache = JsonSerializer.Deserialize<Dictionary<string, GameVersionCache>>(json);
                if (diskCache != null)
                {
                    foreach (var kvp in diskCache)
                    {
                        _cache.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load version cache: {ex.Message}");
            }
        }

        private static void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_cacheFilePath))
                return;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_cache.ToDictionary(k => k.Key, v => v.Value), options);
                File.WriteAllText(_cacheFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save version cache: {ex.Message}");
            }
        }

        private static bool TryResolveCacheEntry(
            string? repositorySource,
            string repository,
            out string cacheKey,
            out GameVersionCache? cache)
        {
            cacheKey = GetCacheKey(repositorySource, repository);
            if (_cache.TryGetValue(cacheKey, out var found))
            {
                cache = found;
                return true;
            }

            // Legacy bare repository keys (pre-repositorySource) apply to GitHub only.
            if (RepositorySourceHelper.IsGitHub(repositorySource) &&
                !string.IsNullOrWhiteSpace(repository) &&
                _cache.TryGetValue(repository, out var legacy))
            {
                cacheKey = repository;
                cache = legacy;
                return true;
            }

            cache = null;
            return false;
        }

        public static bool TryGetCachedVersion(
            string? repositorySource,
            string? repository,
            out GameVersionCache? cache)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                cache = null;
                return false;
            }

            if (TryResolveCacheEntry(repositorySource, repository, out _, out var foundCache) &&
                foundCache != null &&
                DateTime.UtcNow - foundCache.LastChecked < CacheExpiry)
            {
                cache = foundCache;
                return true;
            }

            cache = null;
            return false;
        }

        /// <summary>Legacy overload: treats repository as GitHub.</summary>
        public static bool TryGetCachedVersion(string repository, out GameVersionCache? cache) =>
            TryGetCachedVersion(RepositorySourceIds.GitHub, repository, out cache);

        public static bool NeedsUpdateCheck(
            string? repositorySource,
            string repository,
            bool isInstalledGame = true)
        {
            if (!TryResolveCacheEntry(repositorySource, repository, out _, out var cache) || cache == null)
                return true;

            var interval = isInstalledGame ? InstalledGameUpdateInterval : NotInstalledGameUpdateInterval;
            return DateTime.UtcNow - cache.LastUpdateCheck >= interval;
        }

        /// <summary>Legacy overload: treats repository as GitHub.</summary>
        public static bool NeedsUpdateCheck(string repository, bool isInstalledGame = true) =>
            NeedsUpdateCheck(RepositorySourceIds.GitHub, repository, isInstalledGame);

        public static void SetCache(
            string? repositorySource,
            string repository,
            string version,
            string etag,
            GitHubRelease? release = null)
        {
            var cacheKey = GetCacheKey(repositorySource, repository);
            _cache.AddOrUpdate(cacheKey,
                new GameVersionCache
                {
                    Version = version,
                    LastChecked = DateTime.UtcNow,
                    LastUpdateCheck = DateTime.UtcNow,
                    ETag = etag,
                    CachedRelease = release
                },
                (key, old) => new GameVersionCache
                {
                    Version = version,
                    LastChecked = DateTime.UtcNow,
                    LastUpdateCheck = DateTime.UtcNow,
                    ETag = etag ?? old.ETag,
                    CachedRelease = release ?? old.CachedRelease
                });

            // Drop legacy bare key once migrated to composite GitHub key.
            if (RepositorySourceHelper.IsGitHub(repositorySource) &&
                !string.IsNullOrWhiteSpace(repository) &&
                !string.Equals(cacheKey, repository, StringComparison.OrdinalIgnoreCase))
            {
                _cache.TryRemove(repository, out _);
            }

            SaveToDisk();
        }

        /// <summary>Legacy overload: treats repository as GitHub.</summary>
        public static void SetCache(string repository, string version, string etag, GitHubRelease? release = null) =>
            SetCache(RepositorySourceIds.GitHub, repository, version, etag, release);

        public static string GetETag(string? repositorySource, string repository)
        {
            if (TryResolveCacheEntry(repositorySource, repository, out _, out var cache) && cache != null)
                return cache.ETag;
            return "";
        }

        /// <summary>Legacy overload: treats repository as GitHub.</summary>
        public static string GetETag(string repository) =>
            GetETag(RepositorySourceIds.GitHub, repository);

        public static void RemoveCache(string? repositorySource, string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
                return;

            var cacheKey = GetCacheKey(repositorySource, repository);
            _cache.TryRemove(cacheKey, out _);
            if (RepositorySourceHelper.IsGitHub(repositorySource))
                _cache.TryRemove(repository, out _);
            SaveToDisk();
        }

        /// <summary>Legacy overload: treats repository as GitHub.</summary>
        public static void RemoveCache(string repository) =>
            RemoveCache(RepositorySourceIds.GitHub, repository);
    }
}
