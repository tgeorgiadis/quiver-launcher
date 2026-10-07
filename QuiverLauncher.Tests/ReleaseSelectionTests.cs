using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ReleaseSelectionTests
{
    [Fact]
    public void SelectLatestRelease_skips_prerelease_at_top_for_stable_with_assets()
    {
        var nightly = Release("0.9.0-nightly", prerelease: true);
        var stable = Release("0.8.0", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease([nightly, stable]);

        selected.Should().BeSameAs(stable);
    }

    [Fact]
    public void SelectLatestRelease_skips_stable_without_assets_for_next_stable()
    {
        var emptyStable = NoAssets("2.0.0", prerelease: false);
        var stableWithAssets = Release("1.0.0", prerelease: false);
        var prerelease = Release("2.1.0-beta", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease([emptyStable, stableWithAssets, prerelease]);

        selected.Should().BeSameAs(stableWithAssets);
    }

    [Fact]
    public void SelectLatestRelease_falls_back_to_prerelease_when_no_stable_has_assets()
    {
        var emptyStable = NoAssets("1.0.0", prerelease: false);
        var prerelease = Release("1.1.0-beta", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease([emptyStable, prerelease]);

        selected.Should().BeSameAs(prerelease);
    }

    [Fact]
    public void SelectLatestRelease_prerelease_only_repo_picks_first_with_assets()
    {
        var newer = Release("0.2.0-beta.1", prerelease: true);
        var older = Release("0.1.0-beta.1", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease([newer, older]);

        selected.Should().BeSameAs(newer);
    }

    [Fact]
    public void SelectLatestRelease_relive_picks_github_stable_over_appveyor()
    {
        var stable = Release("github-v1.0.9", prerelease: false);
        var beta = Release("v0.0.1-beta", prerelease: true);
        var appveyor = Release("appveyor_1.0.4687", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease([stable, beta, appveyor]);

        selected.Should().BeSameAs(stable);
    }

    [Fact]
    public void SelectLatestRelease_honors_preferred_pin()
    {
        var latestStable = Release("1.2.0", prerelease: false);
        var pinned = Release("1.0.0-beta.1", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease(
            [latestStable, pinned],
            preferredVersion: "1.0.0-beta.1",
            installedVersion: "1.0.0-beta.1");

        selected.Should().BeSameAs(pinned);
    }

    [Fact]
    public void SelectLatestRelease_prefers_github_latest_over_first_unflagged_stable()
    {
        var rc5 = Release("1.1-rc5", prerelease: false);
        var rc4 = Release("1.1-rc4", prerelease: false);
        var stable = Release("1.1.0", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease(
            [rc5, rc4, stable],
            githubLatestTag: "1.1.0");

        selected.Should().BeSameAs(stable);
    }

    [Fact]
    public void SelectLatestRelease_without_github_latest_keeps_first_stable()
    {
        var rc5 = Release("1.1-rc5", prerelease: false);
        var rc4 = Release("1.1-rc4", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease([rc5, rc4]);

        selected.Should().BeSameAs(rc5);
    }

    [Fact]
    public void SelectLatestRelease_pin_beats_github_latest()
    {
        var latest = Release("1.1.0", prerelease: false);
        var pinned = Release("1.1-rc5", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease(
            [pinned, latest],
            preferredVersion: "1.1-rc5",
            githubLatestTag: "1.1.0");

        selected.Should().BeSameAs(pinned);
    }

    [Fact]
    public void SelectLatestRelease_prerelease_only_without_latest_picks_first_with_assets()
    {
        var newer = Release("0.2.0-beta.1", prerelease: true);
        var older = Release("0.1.0-beta.1", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease([newer, older], githubLatestTag: null);

        selected.Should().BeSameAs(newer);
    }

    [Fact]
    public void SelectLatestRelease_skips_github_latest_without_assets()
    {
        var emptyLatest = NoAssets("1.1.0", prerelease: false);
        var rc5 = Release("1.1-rc5", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease(
            [emptyLatest, rc5],
            githubLatestTag: "1.1.0");

        selected.Should().BeSameAs(rc5);
    }

    [Fact]
    public void SelectLatestRelease_skips_prerelease_without_assets()
    {
        var noAssets = NoAssets("0.2.0-beta.2", prerelease: true);
        var withAssets = Release("v0.1.0-public-test.16", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease([noAssets, withAssets]);

        selected.Should().BeSameAs(withAssets);
    }

    [Fact]
    public void IsSameInstalledRelease_matches_equivalent_tags()
    {
        ReleaseSelection.IsSameInstalledRelease("v0.1.0-public-test.16", "v0.1.0-public-test.16")
            .Should().BeTrue();
        ReleaseSelection.IsSameInstalledRelease("0.1.0-public-test.16", "v0.1.0-public-test.16")
            .Should().BeTrue();
        ReleaseSelection.IsSameInstalledRelease("v0.1.0-public-test.16", "0.2.0-beta.2")
            .Should().BeFalse();
    }

    [Fact]
    public void SelectLatestRelease_github_latest_matches_its_own_tag_when_tags_have_a_word_prefix()
    {
        // DKR-R: GitHub's latest is Version1.0.4, newer betas are listed first.
        var beta10 = Release("Version1.0.5beta10", prerelease: true);
        var beta9 = Release("Version1.0.5beta9", prerelease: true);
        var stable = Release("Version1.0.4", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease(
            [beta10, beta9, stable],
            githubLatestTag: "Version1.0.4");

        selected.Should().BeSameAs(stable);
    }

    [Fact]
    public void SelectLatestRelease_pin_matches_its_own_tag_when_tags_have_a_word_prefix()
    {
        var beta = Release("Version1.0.5beta9", prerelease: true);
        var stable = Release("Version1.0.4", prerelease: false);

        ReleaseSelection.SelectLatestRelease([beta, stable], preferredVersion: "Version1.0.4")
            .Should().BeSameAs(stable);
    }

    [Theory]
    [InlineData("Version1.0.4", "Version1.0.5beta9", false)]
    [InlineData("Version1.0.4", "Version1.0.4", true)]
    [InlineData("version1.0.4", "Version1.0.4", true)]
    [InlineData("nightly", "latest", false)]
    [InlineData("1.0.5beta9", "1.0.5", false)]
    [InlineData("r23", "r24", false)]
    [InlineData("v1.2", "1.2.0", true)]
    [InlineData("1.2.3", "v1.2.3+build.5", true)]
    [InlineData("0.2.0-beta.2", "0.2.0", false)]
    public void IsSameInstalledRelease_only_compares_numbers_for_plain_numeric_tags(string first, string second, bool expected)
    {
        ReleaseSelection.IsSameInstalledRelease(first, second).Should().Be(expected);
        ReleaseSelection.IsSameInstalledRelease(second, first).Should().Be(expected);
    }

    private static GitHubRelease Release(string tag, bool prerelease) =>
        new()
        {
            tag_name = tag,
            prerelease = prerelease,
            assets = [new GitHubAsset { name = "app.zip", browser_download_url = "https://example.com/app.zip" }],
        };

    private static GitHubRelease NoAssets(string tag, bool prerelease) =>
        new()
        {
            tag_name = tag,
            prerelease = prerelease,
            assets = [],
        };
}
