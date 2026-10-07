using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

internal sealed record StartupVersionEvidence(string Version, DateTimeOffset VerifiedAt, GitHubRelease? Release = null)
{
    public bool IsFresh(DateTimeOffset now) => VerifiedAt <= now.AddMinutes(5) && now - VerifiedAt < TimeSpan.FromHours(24);
}

/// <summary>Version evidence only: a cached tag does not authorize or describe a download.</summary>
internal static class StartupVersionResolver
{
    internal static StartupVersionEvidence? Resolve(GameInfo app)
    {
        if (app.IsManuallyManaged || string.IsNullOrWhiteSpace(app.Repository)) return null;
        // quiverlauncher.com already said which release a catalog app updates to; GitHub needn't be asked at startup.
        if (string.IsNullOrWhiteSpace(app.PreferredVersion) && app.CatalogVerifiedVersion is { Length: > 0 } verified &&
            app.CatalogVerifiedAt is { } verifiedAt)
        {
            var known = GitHubApiCache.TryGetLastKnownVersion(app.RepositorySource, app.Repository, out var last) &&
                last?.CachedRelease is { } cachedRelease && ReleaseVersionIdentity.AreVersionsEquivalent(cachedRelease.tag_name, verified)
                ? last.CachedRelease : null;
            return new(verified, verifiedAt, known);
        }
        if (!GitHubApiCache.TryGetLastKnownVersion(app.RepositorySource, app.Repository, out var cached) || cached == null ||
            !app.MatchesReleaseTarget(cached.Version))
            return null;
        var release = cached.CachedRelease;
        if (release != null && !ReleaseVersionIdentity.AreVersionsEquivalent(release.tag_name, cached.Version)) release = null;
        // A version chosen by older selection rules is shown, but checked again.
        var checkedAt = cached.SelectionRevision == GameVersionCache.CurrentSelectionRevision
            ? new DateTimeOffset(DateTime.SpecifyKind(cached.LastChecked, DateTimeKind.Utc)) : DateTimeOffset.MinValue;
        return new(cached.Version, checkedAt, release);
    }

    internal static bool Apply(GameInfo app)
    {
        var evidence = Resolve(app);
        if (evidence == null) return false;
        app.ApplyStartupVersion(evidence);
        return evidence.IsFresh(DateTimeOffset.UtcNow);
    }
}
