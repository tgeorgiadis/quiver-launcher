using System.Diagnostics;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Sources;

namespace QuiverLauncher.Services;

/// <summary>
/// Quiver self-updates via Velopack + GitHub Releases.
/// </summary>
public sealed class VelopackUpdateService
{
    public const string GitHubRepoUrl = "https://github.com/tgeorgiadis/quiver";

    private UpdateInfo? _lastUpdateInfo;
    private bool _lastIncludePrerelease;

    public bool IsInstalled
    {
        get
        {
            try
            {
                return VelopackLocator.Current?.CurrentlyInstalledVersion != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? CurrentVersion
    {
        get
        {
            try
            {
                return VelopackLocator.Current?.CurrentlyInstalledVersion?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }

    public bool IsUpdatePendingRestart
    {
        get
        {
            try
            {
                if (!IsInstalled)
                    return false;
                var mgr = CreateManager(_lastIncludePrerelease);
                return mgr.UpdatePendingRestart != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public UpdateInfo? LastUpdateInfo => _lastUpdateInfo;

    /// <summary>
    /// Prereleases are included when the setting is on, or when the installed version is already a prerelease (contains '-').
    /// </summary>
    public static bool EffectiveIncludePrerelease(string? currentVersion, bool allowPrereleaseLauncherUpdates)
    {
        if (allowPrereleaseLauncherUpdates)
            return true;

        return !string.IsNullOrWhiteSpace(currentVersion)
            && currentVersion.Contains('-', StringComparison.Ordinal);
    }

    public static bool ShouldSkipAutomaticSelfUpdate()
    {
#if DEBUG
        return true;
#else
        var skip = Environment.GetEnvironmentVariable("QuiverLauncher_SKIP_UPDATES");
        return string.Equals(skip, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skip, "true", StringComparison.OrdinalIgnoreCase);
#endif
    }

    public async Task<VelopackCheckResult> CheckForUpdatesAsync(
        string? gitHubToken = null,
        bool? includePrerelease = null,
        bool allowPrereleaseLauncherUpdates = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return VelopackCheckResult.NotInstalled(
                LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory));
        }

        try
        {
            var current = CurrentVersion
                ?? LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory);
            var prerelease = includePrerelease
                ?? EffectiveIncludePrerelease(current, allowPrereleaseLauncherUpdates);
            _lastIncludePrerelease = prerelease;
            var mgr = CreateManager(prerelease, gitHubToken);
            var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            _lastUpdateInfo = update;

            if (update == null)
            {
                return new VelopackCheckResult
                {
                    CheckSucceeded = true,
                    InstalledVersion = current,
                    UpdateAvailable = false,
                    IncludedPrerelease = prerelease,
                };
            }

            return new VelopackCheckResult
            {
                CheckSucceeded = true,
                InstalledVersion = current,
                UpdateAvailable = true,
                AvailableVersion = update.TargetFullRelease.Version.ToString(),
                UpdateInfo = update,
                IncludedPrerelease = prerelease,
            };
        }
        catch (NotInstalledException)
        {
            return VelopackCheckResult.NotInstalled(
                LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Velopack update check failed: {ex.Message}");
            return new VelopackCheckResult
            {
                CheckSucceeded = false,
                ErrorMessage = ex.Message,
                InstalledVersion = CurrentVersion
                    ?? LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory),
            };
        }
    }

    public async Task DownloadUpdatesAsync(
        UpdateInfo updateInfo,
        Action<int>? progress = null,
        string? gitHubToken = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        _lastIncludePrerelease = includePrerelease;
        var mgr = CreateManager(includePrerelease, gitHubToken);
        await mgr.DownloadUpdatesAsync(updateInfo, progress).ConfigureAwait(false);
    }

    public void ApplyUpdatesAndRestart(UpdateInfo updateInfo, bool includePrerelease = false)
    {
        _lastIncludePrerelease = includePrerelease;
        var mgr = CreateManager(includePrerelease);
        mgr.ApplyUpdatesAndRestart(updateInfo);
    }

    private static UpdateManager CreateManager(bool includePrerelease, string? gitHubToken = null)
    {
        var source = new GithubSource(GitHubRepoUrl, gitHubToken, includePrerelease);
        return new UpdateManager(source);
    }
}

public sealed class VelopackCheckResult
{
    public bool CheckSucceeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string InstalledVersion { get; init; } = "0.0";
    public bool UpdateAvailable { get; init; }
    public string? AvailableVersion { get; init; }
    public UpdateInfo? UpdateInfo { get; init; }
    public bool IsNotInstalled { get; init; }
    public bool IncludedPrerelease { get; init; }

    public static VelopackCheckResult NotInstalled(string installedVersion) => new()
    {
        CheckSucceeded = true,
        IsNotInstalled = true,
        InstalledVersion = installedVersion,
        UpdateAvailable = false,
        ErrorMessage = "Not a Velopack install (local/dev build).",
    };
}
