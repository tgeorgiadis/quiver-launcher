using System.Diagnostics;
using System.Globalization;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

/// <summary>
/// The file an app was installed from and its SHA-256, kept beside version.txt. A rolling release (a nightly) is
/// rebuilt under the same tag, so the tag alone can't tell a new build: its file's checksum can. When GitHub said
/// when that file was uploaded, that's kept too, for a nightly that names each build's file differently.
/// </summary>
public sealed record InstalledBuild(string FileName, string Sha256, DateTimeOffset? UploadedAt = null)
{
    public const string FileNameOnDisk = "installed-build.txt";

    public static InstalledBuild? Read(string gamePath)
    {
        try
        {
            var path = Path.Combine(gamePath, FileNameOnDisk);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2 || lines[0].Trim() is not { Length: > 0 } name || lines[1].Trim() is not { Length: 64 } sha)
                return null;
            DateTimeOffset? uploadedAt = lines.Length >= 3 && DateTimeOffset.TryParse(lines[2].Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            return new(name, sha.ToLowerInvariant(), uploadedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't read the installed build in {gamePath}: {ex.Message}");
            return null;
        }
    }

    public async Task WriteAsync(string gamePath)
    {
        var uploadedAt = UploadedAt is { } at ? at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "\n" : "";
        try { await File.WriteAllTextAsync(Path.Combine(gamePath, FileNameOnDisk), $"{FileName}\n{Sha256}\n{uploadedAt}").ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't save the installed build in {gamePath}: {ex.Message}");
        }
    }

    /// <summary>When version.txt in <paramref name="gamePath"/> was last written; null when there's none.</summary>
    public static DateTimeOffset? InstalledAt(string gamePath)
    {
        try
        {
            var versionFile = Path.Combine(gamePath, "version.txt");
            return File.Exists(versionFile) ? new DateTimeOffset(File.GetLastWriteTimeUtc(versionFile), TimeSpan.Zero) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't read when {gamePath} was installed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="release"/>, under the installed tag, now publishes a different build than the one installed.
    /// The installed file's checksum decides while the release lists it. When it's gone (a nightly naming each build's file
    /// by date or commit), a file uploaded after it is a new build: preferably the same build of it, renamed. A release
    /// quiverlauncher.com says is <paramref name="rolling"/> lists no upload times, so there the renamed file alone tells.
    /// Without <paramref name="build"/> (an install from before it was kept), a file uploaded after
    /// <paramref name="installedAt"/> tells, under a tag naming no version only. False when nothing tells.
    /// </summary>
    public static bool IsRebuilt(InstalledBuild? build, DateTimeOffset? installedAt, GitHubRelease? release, string? installedVersion,
        bool rolling = false)
    {
        if (release == null || !ReleaseVersionIdentity.AreVersionsEquivalent(release.tag_name, installedVersion)) return false;
        var downloads = GitHubReleaseService.GetDownloadableAssets(release);
        if (build == null)
            return installedAt is { } at && ReleaseVersionIdentity.IsRollingTag(release.tag_name) && downloads.Any(a => a.updated_at > at);
        var file = release.assets?.FirstOrDefault(a => string.Equals(a.name, build.FileName, StringComparison.OrdinalIgnoreCase));
        if (file?.digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return !string.Equals(digest["sha256:".Length..], build.Sha256, StringComparison.OrdinalIgnoreCase);
        var renamed = file == null ? DownloadAssetPolicy.MatchInstalled(new(downloads, [], null), build.FileName, installedVersion,
            release.tag_name) : null;
        if (renamed != null && rolling) return true;
        // A build recorded before its upload time was kept: version.txt was written with it.
        if ((build.UploadedAt ?? installedAt) is not { } since) return false;
        return (file ?? renamed) is { } same ? same.updated_at > since : downloads.Any(a => a.updated_at > since);
    }

    /// <summary>
    /// Whether <paramref name="release"/> has a file uploaded after the installed build was (or, without a record of
    /// that, after <paramref name="installedAt"/>): what tells a newer release when its tag's numbers can't.
    /// </summary>
    public static bool IsUploadedSince(InstalledBuild? build, DateTimeOffset? installedAt, GitHubRelease? release) =>
        release != null && (build?.UploadedAt ?? installedAt) is { } since &&
        GitHubReleaseService.GetDownloadableAssets(release).Any(a => a.updated_at > since);
}
