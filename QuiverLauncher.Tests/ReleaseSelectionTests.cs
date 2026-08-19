using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ReleaseSelectionTests
{
    [Fact]
    public void SelectLatestRelease_skips_prerelease_at_top_for_stable_installs()
    {
        var nightly = Release("0.9.0-nightly", prerelease: true);
        var stable = Release("0.8.0", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease(
            [nightly, stable],
            installedVersion: "0.7.0");

        selected.Should().BeSameAs(stable);
    }

    [Fact]
    public void SelectLatestRelease_stays_on_beta_instead_of_older_stable()
    {
        var publicTest = Release("0.1.0 Public Test 16", prerelease: false);
        var beta = Release("0.2.0-beta.2", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease(
            [publicTest, beta],
            installedVersion: "0.2.0-beta.2");

        selected.Should().BeSameAs(beta);
    }

    [Fact]
    public void SelectLatestRelease_picks_highest_version_among_stables()
    {
        var olderStable = Release("1.0.0", prerelease: false);
        var newerPublishOlderVersion = Release("0.9.0", prerelease: false);

        var selected = ReleaseSelection.SelectLatestRelease(
            [newerPublishOlderVersion, olderStable],
            installedVersion: "0.8.0");

        selected.Should().BeSameAs(olderStable);
    }

    [Fact]
    public void SelectLatestRelease_prerelease_only_repo_still_picks_a_release()
    {
        var older = Release("0.1.0-beta.1", prerelease: true);
        var newer = Release("0.2.0-beta.1", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease([older, newer]);

        selected.Should().BeSameAs(newer);
    }

    [Fact]
    public void SelectLatestRelease_sentinel_install_does_not_jump_to_older_labeled_stable()
    {
        var publicTest = Release("0.1.0 Public Test 16", prerelease: false);
        var beta = Release("0.2.0-beta.2", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease(
            [publicTest, beta],
            installedVersion: "0.0.0");

        selected.Should().BeSameAs(beta);
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
    public void SelectLatestRelease_prefers_releases_with_assets()
    {
        var noAssets = new GitHubRelease { tag_name = "0.2.0-beta.2", prerelease = true, assets = [] };
        var withAssets = Release("v0.1.0-public-test.16", prerelease: true);

        var selected = ReleaseSelection.SelectLatestRelease(
            [noAssets, withAssets],
            installedVersion: "v0.1.0-public-test.16");

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

    private static GitHubRelease Release(string tag, bool prerelease) =>
        new()
        {
            tag_name = tag,
            prerelease = prerelease,
            assets = [new GitHubAsset { name = "app.zip", browser_download_url = "https://example.com/app.zip" }],
        };
}
