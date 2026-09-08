namespace QuiverLauncher.Services;

public static class GitHubTokenBannerPolicy
{
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(7);

    public static bool HasGitHubToken(string? token) =>
        !string.IsNullOrWhiteSpace(token);

    public static bool ShouldShow(
        string? gitHubApiToken,
        bool permanentlyDismissed,
        DateTimeOffset? snoozedUntilUtc,
        DateTimeOffset utcNow)
    {
        if (HasGitHubToken(gitHubApiToken))
            return false;

        if (permanentlyDismissed)
            return false;

        if (snoozedUntilUtc is { } until && until > utcNow)
            return false;

        return true;
    }

    public static DateTimeOffset SnoozeUntil(DateTimeOffset utcNow) =>
        utcNow + SnoozeDuration;
}
