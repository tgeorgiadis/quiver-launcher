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
        if (!GitHubApiCache.TryGetLastKnownVersion(app.RepositorySource, app.Repository, out var cached) || cached == null ||
            (!string.IsNullOrWhiteSpace(app.PreferredVersion) && !ReleaseVersionIdentity.AreVersionsEquivalent(app.PreferredVersion, cached.Version)))
            return null;
        var release = cached.CachedRelease;
        if (release != null && !ReleaseVersionIdentity.AreVersionsEquivalent(release.tag_name, cached.Version)) release = null;
        return new(cached.Version, new DateTimeOffset(DateTime.SpecifyKind(cached.LastChecked, DateTimeKind.Utc)), release);
    }

    internal static bool Apply(GameInfo app)
    {
        var evidence = Resolve(app);
        if (evidence == null) return false;
        app.ApplyStartupVersion(evidence);
        return evidence.IsFresh(DateTimeOffset.UtcNow);
    }
}
