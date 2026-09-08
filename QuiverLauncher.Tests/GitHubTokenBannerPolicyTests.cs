using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GitHubTokenBannerPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldShow_is_true_when_token_is_missing()
    {
        GitHubTokenBannerPolicy.ShouldShow(null, false, null, Now).Should().BeTrue();
        GitHubTokenBannerPolicy.ShouldShow("", false, null, Now).Should().BeTrue();
        GitHubTokenBannerPolicy.ShouldShow("   ", false, null, Now).Should().BeTrue();
    }

    [Fact]
    public void ShouldShow_is_false_when_token_is_set()
    {
        GitHubTokenBannerPolicy.ShouldShow("ghp_token", false, null, Now).Should().BeFalse();
        GitHubTokenBannerPolicy.ShouldShow("  ghp_token  ", false, null, Now).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_is_false_when_permanently_dismissed()
    {
        GitHubTokenBannerPolicy
            .ShouldShow(null, permanentlyDismissed: true, snoozedUntilUtc: null, Now)
            .Should()
            .BeFalse();
        GitHubTokenBannerPolicy
            .ShouldShow(
                null,
                permanentlyDismissed: true,
                snoozedUntilUtc: Now.AddDays(-1),
                Now)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ShouldShow_is_false_while_snooze_has_not_expired()
    {
        GitHubTokenBannerPolicy
            .ShouldShow(null, false, Now.AddDays(7), Now)
            .Should()
            .BeFalse();
        GitHubTokenBannerPolicy
            .ShouldShow(null, false, Now.AddSeconds(1), Now)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ShouldShow_is_true_when_snooze_has_expired()
    {
        GitHubTokenBannerPolicy
            .ShouldShow(null, false, Now, Now)
            .Should()
            .BeTrue();
        GitHubTokenBannerPolicy
            .ShouldShow(null, false, Now.AddDays(-7), Now)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void SnoozeUntil_is_one_week_later()
    {
        GitHubTokenBannerPolicy.SnoozeUntil(Now).Should().Be(Now.AddDays(7));
    }
}
