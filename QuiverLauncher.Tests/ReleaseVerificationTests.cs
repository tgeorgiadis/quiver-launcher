using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

/// <summary>
/// Catalog apps follow quiverlauncher.com: they update to the release Quiver verified, ask before
/// installing any other, and install only the files Quiver pinned.
/// </summary>
public class ReleaseVerificationTests
{
    private const string Asset = "alpha-windows.zip";
    private static readonly byte[] OldExe = [0x4D, 0x5A, 0x01, 0x01];

    [Fact]
    public async Task Release_status_links_catalog_apps_and_tells_shared_repositories_apart()
    {
        using var launcher = new Launcher();
        var token = TestContext.Current.CancellationToken;
        string alphaRepo = Repo(), sharedRepo = Repo();
        var site = launcher.Network.Site;
        // Two pages: an app listed on the second one still links.
        site["/release-status?limit=100"] = Page([Status("alpha", alphaRepo, "v1.0", latest: "v1.1"), Status("shared-pc", sharedRepo, "v3.0")], next: "c2");
        site["/release-status?limit=100&cursor=c2"] = Page([Status("shared-android", sharedRepo, "v3.1"),
            Status("moved", "new-owner/new-name", "v2.0"), Status("sibling", "new-owner/other-app", "v9.0")]);
        // The listing gives every app's folder and download filter, in pages of up to 100.
        site["/apps?limit=100&sort=added"] = Page([Listed("alpha", "Alpha"), Listed("shared-pc", "Shared", "windows")], next: "a2");
        site["/apps?limit=100&sort=added&cursor=a2"] = Page([Listed("shared-android", "Shared", "android"), Listed("moved", "Moved"),
            Listed("sibling", "Sibling")]);
        var alpha = App("Alpha", alphaRepo);
        var pc = App("Shared PC", sharedRepo, filter: " Windows ");
        var android = App("Shared Android", sharedRepo, filter: "android");
        // Neither filter nor folder picks one entry: better unlinked than checked against another app's files.
        var ambiguous = App("Shared Linux", sharedRepo, filter: "linux", folder: "Shared");
        var outsider = App("Outsider", Repo());
        // Added before the repository moved to another name and owner: GitHub still follows the old one, and the
        // catalog's folder says it's the same app.
        var moved = App("Moved", "old-owner/old-name");
        // The same, but it remembers the catalog entry it was added from: that settles it.
        var remembered = App("Shared Linux", sharedRepo, filter: "linux", folder: "Shared");
        remembered.CatalogEntryId = "id-shared-android";

        await launcher.Manager.CatalogReleases.RefreshAsync([alpha, pc, android, ambiguous, outsider, moved, remembered], token);

        (alpha.CatalogSlug, alpha.CatalogVerifiedVersion, alpha.ReleaseTarget).Should().Be(("alpha", "v1.0", "v1.0"));
        // The library says why it stays on v1.0 while v1.1 is out.
        (alpha.ShowUnverifiedRelease, alpha.UnverifiedReleaseLabel, alpha.LatestVersionCaption).Should().Be((true, "v1.1 not verified yet", "Verified: "));
        pc.CatalogUnverifiedVersion.Should().BeNull();
        (pc.CatalogSlug, pc.CatalogVerifiedVersion).Should().Be(("shared-pc", "v3.0"));
        (android.CatalogSlug, android.CatalogVerifiedVersion).Should().Be(("shared-android", "v3.1"));
        ambiguous.CatalogSlug.Should().BeNull();
        ambiguous.CatalogVerifiedVersion.Should().BeNull();
        outsider.CatalogSlug.Should().BeNull();
        outsider.ReleaseTarget.Should().BeNull();
        (moved.CatalogSlug, moved.ReleaseTarget).Should().Be(("moved", "v2.0"));
        (remembered.CatalogSlug, remembered.CatalogVerifiedVersion).Should().Be(("shared-android", "v3.1"));
        launcher.Network.SitePaths().Should().Equal("/api/v1/release-status", "/api/v1/release-status", "/api/v1/apps", "/api/v1/apps");

        // An app outside the catalog is left alone, and a fresh status is not read again.
        (await launcher.Manager.CatalogReleases.CheckAsync(outsider, "v1.0", token)).Should().BeNull();
        await launcher.Manager.CatalogReleases.RefreshAsync([alpha, pc, android], token);
        launcher.Network.SitePaths().Should().HaveCount(4);

        // What was read is kept, so a moved app still links offline after a restart.
        var offline = new CatalogReleases(new QuiverCatalogClient(new HttpClient(new Network())),
            Path.Combine(QuiverLauncherPaths.UserDataRoot, "Cache"));
        var movedAgain = App("Moved", "old-owner/old-name");
        offline.ApplyKnown(movedAgain);
        movedAgain.CatalogSlug.Should().Be("moved");
    }

    [Fact]
    public async Task Release_history_reports_each_state_and_the_checksums_Quiver_pinned()
    {
        using var launcher = new Launcher();
        var token = TestContext.Current.CancellationToken;
        var repo = Repo();
        var checkEnds = DateTimeOffset.UtcNow.AddHours(3);
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v1.0")]);
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
        [
            History("v1.1", "unverified", ["Quiver is still checking this release."], [(Asset, [1])],
                scan: new { verdict = "pending" }, checkEndsAt: checkEnds.ToUnixTimeMilliseconds()),
            History("v1.0", "verified", [], [(Asset, [2]), ("alpha-linux.tar.gz", [3])], scan: new { verdict = "clean" }),
            History("v0.9", "blocked", ["The developer pulled this release."], []),
        ]);
        var app = App("Alpha", repo);
        var releases = launcher.Manager.CatalogReleases;

        var verified = (await releases.CheckAsync(app, "1.0", token))!;
        verified.State.Should().Be(ReleaseCheckState.Verified);
        verified.VerifiedVersion.Should().Be("v1.0");
        verified.ScanVerdict.Should().Be("clean");
        verified.ChecksumFor(Asset).Should().Be(Sha256([2]).ToLowerInvariant());
        verified.ChecksumFor("alpha-linux.tar.gz").Should().Be(Sha256([3]).ToLowerInvariant());
        verified.ChecksumFor("alpha-macos.zip").Should().BeNull();

        var unverified = (await releases.CheckAsync(app, "v1.1", token))!;
        unverified.State.Should().Be(ReleaseCheckState.Unverified);
        unverified.Reasons.Should().Equal("Quiver is still checking this release.");
        unverified.ChecksumFor(Asset).Should().Be(Sha256([1]).ToLowerInvariant());
        unverified.VerifiedAt!.Value.ToUnixTimeMilliseconds().Should().Be(checkEnds.ToUnixTimeMilliseconds());

        var blocked = (await releases.CheckAsync(app, "v0.9", token))!;
        blocked.State.Should().Be(ReleaseCheckState.Blocked);
        blocked.Reasons.Should().Equal("The developer pulled this release.");

        var unseen = (await releases.CheckAsync(app, "v2.0", token))!;
        unseen.State.Should().Be(ReleaseCheckState.Unverified);
        unseen.Reasons.Should().Equal(CatalogReleases.NotSeen);
        unseen.VerifiedVersion.Should().Be("v1.0");
        unseen.Checksums.Should().BeEmpty();

        launcher.Network.SitePaths().Count(p => p.EndsWith("/release-history")).Should().Be(1, "the history is kept for a while");
    }

    [Fact]
    public async Task Tags_with_a_word_prefix_are_only_verified_for_the_exact_release()
    {
        using var launcher = new Launcher();
        var token = TestContext.Current.CancellationToken;
        var repo = Repo();
        // Tags like DKR-R's: no leading number, so they must not all count as the same version.
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("dkr", repo, "Version1.0.4", latest: "Version1.0.5beta9")]);
        launcher.Network.Site["/apps/dkr/release-history?limit=100"] = Page(
        [
            History("Version1.0.4", "verified", [], [(Asset, [2])]),
            History("Version1.0.5beta9", "unverified", ["Quiver is still checking this release."], [(Asset, [1])]),
        ]);
        var app = App("DKR", repo);
        var releases = launcher.Manager.CatalogReleases;

        (await releases.CheckAsync(app, "Version1.0.4", token))!.State.Should().Be(ReleaseCheckState.Verified);
        var beta = (await releases.CheckAsync(app, "Version1.0.5beta9", token))!;
        beta.State.Should().Be(ReleaseCheckState.Unverified);
        beta.ChecksumFor(Asset).Should().Be(Sha256([1]).ToLowerInvariant());
        // Not in the history at all: the verified version in the status feed must not vouch for it.
        (await releases.CheckAsync(app, "Version1.0.6", token))!.State.Should().Be(ReleaseCheckState.Unverified);
        app.CatalogUnverifiedVersion.Should().Be("Version1.0.5beta9");
    }

    [Theory]
    [InlineData(null, "v1", GameStatus.Installed)]
    [InlineData("v2", "v2", GameStatus.UpdateAvailable)]
    public async Task Update_check_offers_the_verified_release_unless_the_player_pinned_another(
        string? pin, string expectedLatest, GameStatus expectedStatus)
    {
        using var launcher = new Launcher();
        var token = TestContext.Current.CancellationToken;
        var repo = Repo();
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v1")]);
        // GitHub's latest is v2, which Quiver hasn't verified.
        launcher.Network.GitHub[$"/repos/{repo}/releases/latest"] = Json(OnGitHub("v2"));
        launcher.Network.GitHub[$"/repos/{repo}/releases"] = Json(new[] { OnGitHub("v2"), OnGitHub("v1") });
        var app = App("Alpha", repo, manager: launcher.Manager);
        InstallOnDisk(app, launcher.Manager.GamesFolder, "v1");
        app.PreferredVersion = pin;
        launcher.Manager.Games.Add(app);

        var result = await launcher.Manager.CheckInstalledUpdatesAsync(true, null, token);

        result.Successful.Should().Be(1);
        app.CatalogVerifiedVersion.Should().Be("v1");
        app.InstalledVersion.Should().Be("v1");
        app.LatestVersion.Should().Be(expectedLatest);
        app.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Update_checks_and_version_lists_for_catalog_apps_never_ask_GitHub()
    {
        using var launcher = new Launcher();
        var token = TestContext.Current.CancellationToken;
        var (alphaRepo, betaRepo) = (Repo(), Repo());
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", alphaRepo, "v1", latest: "v2"), Status("beta", betaRepo, "v1", latest: "v2")]);
        foreach (var slug in new[] { "alpha", "beta" })
            launcher.Network.Site[$"/apps/{slug}/release-history?limit=100"] = Page(
            [
                History("v3", "blocked", ["The developer pulled this release."], [(Asset, Zip("v3.txt"))]),
                History("v2", "unverified", ["Quiver is still checking this release."], [(Asset, Zip("v2.txt"))]),
                History("v1", "verified", [], [(Asset, Zip("v1.txt"))]),
            ]);
        var alpha = App("Alpha", alphaRepo, manager: launcher.Manager);
        var beta = App("Beta", betaRepo, manager: launcher.Manager);
        beta.PreferredVersion = "v2";
        foreach (var app in new[] { alpha, beta })
        {
            InstallOnDisk(app, launcher.Manager.GamesFolder, "v1");
            launcher.Manager.Games.Add(app);
        }

        var result = await launcher.Manager.CheckInstalledUpdatesAsync(true, null, token);
        var versions = await alpha.FetchReleasesAsync(launcher.Manager.HttpClient, token);

        result.Successful.Should().Be(2);
        (alpha.LatestVersion, alpha.Status).Should().Be(("v1", GameStatus.Installed));
        (beta.LatestVersion, beta.Status).Should().Be(("v2", GameStatus.UpdateAvailable), "the player pinned v2");
        beta.GetLatestRelease()!.assets.Single().browser_download_url.Should().Be(SiteLink("v2", Asset));
        versions.Releases.Select(r => r.tag_name).Should().Equal("v3", "v2", "v1");
        versions.LatestTag.Should().Be("v1");
        launcher.Network.Requests.Should().NotContain(u => u.Host == "api.github.com");
    }

    [AvaloniaFact]
    public async Task Catalog_app_installs_the_verified_release_from_the_sites_file_link_without_asking_GitHub()
    {
        using var launcher = new Launcher();
        var repo = Repo();
        var served = Zip("v2.txt");
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v2", latest: "v3")]);
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
        [
            History("v3", "unverified", ["Quiver is still checking this release."], [(Asset, Zip("v3.txt"))]),
            History("v2", "verified", [], [(Asset, served)]),
        ]);
        launcher.Network.Files[SiteLink("v2", Asset)] = served;
        var app = App("Alpha", repo, manager: launcher.Manager);
        var player = new Dialogs(confirm: true);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            null, launcher.Store.Current, GameStatus.NotInstalled, player);

        player.Errors.Should().BeEmpty();
        player.Confirmations.Should().BeEmpty();
        (app.InstalledVersion, app.Status).Should().Be(("v2", GameStatus.Installed));
        launcher.Network.Downloads.Should().Be(1);
        launcher.Network.Requests.Should().NotContain(u => u.Host == "api.github.com");
    }

    [AvaloniaFact]
    public async Task Automatic_update_to_an_unverified_release_downloads_nothing_and_keeps_the_installed_one()
    {
        using var launcher = new Launcher();
        var repo = Repo();
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v1")]);
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
            [History("v2", "unverified", ["Quiver is still checking this release."], [(Asset, Zip("v2.txt"))])]);
        var release = OnGitHub("v2");
        launcher.Network.Files[release.assets[0].browser_download_url] = Zip("v2.txt");
        var app = InstalledApp(launcher, repo, latest: "v2");
        var player = new Dialogs(confirm: true);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            release, launcher.Store.Current, GameStatus.UpdateAvailable, new AutomaticGameDownloadDialogs(player), releaseMode: ReleaseInstallMode.ExplicitRelease);

        launcher.Network.Downloads.Should().Be(0);
        player.Confirmations.Should().BeEmpty("an automatic update never asks, and never installs what Quiver hasn't verified");
        player.Errors.Should().BeEmpty();
        app.Status.Should().Be(GameStatus.UpdateAvailable);
        app.InstalledVersion.Should().Be("v1");
        app.DownloadProgress.Should().Be(0);
        AssertStillV1(app, launcher.Manager.GamesFolder);
    }

    [AvaloniaFact]
    public async Task Download_that_is_not_the_file_Quiver_pinned_is_not_installed()
    {
        using var launcher = new Launcher();
        var repo = Repo();
        var served = Zip("v2.txt");
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v2")]);
        // Quiver pinned other bytes than the server now sends, though GitHub's own digest matches them.
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
            [History("v2", "verified", [], [(Asset, Zip("v2-as-Quiver-saw-it.txt"))])]);
        var release = OnGitHub("v2", digest: "sha256:" + Sha256(served));
        launcher.Network.Files[release.assets[0].browser_download_url] = served;
        var app = InstalledApp(launcher, repo, latest: "v2");
        var dialogs = new Dialogs(confirm: true);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            release, launcher.Store.Current, GameStatus.UpdateAvailable, dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease);

        launcher.Network.Downloads.Should().Be(1);
        dialogs.Confirmations.Should().BeEmpty("the release itself is verified");
        var error = dialogs.Errors.Should().ContainSingle().Subject;
        error.Title.Should().Be("Download Not Verified");
        error.Message.Should().Contain($"{Asset} isn't the file Quiver checked");
        // The site is told, so it reads v2 back from GitHub now instead of at its daily check.
        launcher.Network.Reports.Should().Equal(("alpha", "v2", Asset, "mismatch"));
        app.InstalledVersion.Should().Be("v1");
        app.Status.Should().Be(GameStatus.UpdateAvailable);
        AssertStillV1(app, launcher.Manager.GamesFolder);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Removed_file_is_reported_and_the_newest_other_verified_release_offered(bool switchRelease)
    {
        using var launcher = new Launcher();
        var repo = Repo();
        var older = Zip("v2.txt");
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v3")]);
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
        [
            History("v3", "verified", [], [(Asset, Zip("v3.txt"))]),
            History("v2", "verified", [], [(Asset, older)]),
            History("v1", "verified", [], [(Asset, Zip("v1.txt"))]),
        ]);
        // The developer deleted v3's file after Quiver verified it: the link is a 404.
        launcher.Network.Files[SiteLink("v2", Asset)] = older;
        var app = App("Alpha", repo, manager: launcher.Manager);
        var player = new Dialogs(confirm: true, switchRelease);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            null, launcher.Store.Current, GameStatus.NotInstalled, player);

        launcher.Network.Reports.Should().Equal(("alpha", "v3", Asset, "missing"));
        var offered = player.Offers.Should().ContainSingle().Subject;
        (offered.App, offered.Version).Should().Be(("Alpha", "v2"));
        offered.Problem.Should().Be($"{Asset} was removed from v3 by its developer, so it can't be downloaded.");
        if (switchRelease)
        {
            player.Errors.Should().BeEmpty();
            (app.InstalledVersion, app.Status).Should().Be(("v2", GameStatus.Installed));
            File.Exists(Path.Combine(app.GetInstallPath(launcher.Manager.GamesFolder), "v2.txt")).Should().BeTrue();
        }
        else
        {
            var error = player.Errors.Should().ContainSingle().Subject;
            (error.Message, error.Title).Should().Be((offered.Problem, "Download Removed"));
            app.Status.Should().Be(GameStatus.NotInstalled);
            launcher.Network.Downloads.Should().Be(1);
        }
    }

    [AvaloniaFact]
    public async Task Removed_file_with_no_other_verified_release_says_so_and_keeps_the_installed_one()
    {
        using var launcher = new Launcher();
        var repo = Repo();
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v2")]);
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
            [History("v2", "verified", [], [(Asset, Zip("v2.txt"))])]);
        var release = OnGitHub("v2");
        var app = InstalledApp(launcher, repo, latest: "v2");
        var player = new Dialogs(confirm: true, switchRelease: true);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            release, launcher.Store.Current, GameStatus.UpdateAvailable, player, releaseMode: ReleaseInstallMode.ExplicitRelease);

        launcher.Network.Reports.Should().Equal(("alpha", "v2", Asset, "missing"));
        player.Offers.Should().BeEmpty();
        player.Errors.Should().ContainSingle().Which.Title.Should().Be("Download Removed");
        (app.InstalledVersion, app.Status).Should().Be(("v1", GameStatus.UpdateAvailable));
        AssertStillV1(app, launcher.Manager.GamesFolder);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rolling_release_rebuilt_since_Quiver_checked_it_installs_the_build_GitHub_publishes(bool stillOnGitHub)
    {
        using var launcher = new Launcher();
        var repo = Repo();
        var rebuilt = Zip("nightly-2.txt");
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "nightly")]);
        // Quiver saw last night's build; the developer has replaced it under the same tag since.
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
            [History("nightly", "verified", [], [(Asset, Zip("nightly-1.txt"))], rolling: true)]);
        var release = OnGitHub("nightly");
        launcher.Network.Files[release.assets[0].browser_download_url] = rebuilt;
        launcher.Network.GitHub[$"/repos/{repo}/releases/tags/nightly"] = Json(new
        {
            tag_name = "nightly",
            assets = stillOnGitHub
                ? new[] { new { name = Asset, browser_download_url = release.assets[0].browser_download_url, digest = "sha256:" + Sha256(rebuilt) } }
                : [],
        });
        var app = App("Alpha", repo, manager: launcher.Manager);
        var player = new Dialogs(confirm: true);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            release, launcher.Store.Current, GameStatus.NotInstalled, player, releaseMode: ReleaseInstallMode.ExplicitRelease);

        if (stillOnGitHub)
        {
            player.Errors.Should().BeEmpty();
            launcher.Network.Reports.Should().BeEmpty();
            (app.InstalledVersion, app.Status).Should().Be(("nightly", GameStatus.Installed));
        }
        else
        {
            // Not rebuilt but removed.
            player.Errors.Should().ContainSingle().Which.Title.Should().Be("Download Removed");
            launcher.Network.Reports.Should().Equal(("alpha", "nightly", Asset, "missing"));
            app.Status.Should().Be(GameStatus.NotInstalled);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Download_matching_the_pinned_checksum_installs(bool verified)
    {
        using var launcher = new Launcher();
        var repo = Repo();
        var served = Zip("v2.txt");
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, verified ? "v2" : "v1")]);
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
            [History("v2", verified ? "verified" : "unverified", verified ? [] : ["Quiver is still checking this release."], [(Asset, served)])]);
        var release = OnGitHub("v2");
        launcher.Network.Files[release.assets[0].browser_download_url] = served;
        var app = App("Alpha", repo, manager: launcher.Manager);
        var player = new Dialogs(confirm: true);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            release, launcher.Store.Current, GameStatus.NotInstalled, player, releaseMode: ReleaseInstallMode.ExplicitRelease);

        player.Errors.Should().BeEmpty();
        if (verified) player.Confirmations.Should().BeEmpty();
        else
        {
            // The player chose to install it anyway; Quiver's pinned file is still required.
            var asked = player.Confirmations.Should().ContainSingle().Subject;
            (asked.App, asked.Version, asked.Check.State).Should().Be(("Alpha", "v2", ReleaseCheckState.Unverified));
        }
        app.Status.Should().Be(GameStatus.Installed);
        app.InstalledVersion.Should().Be("v2");
        var path = app.GetInstallPath(launcher.Manager.GamesFolder);
        File.ReadAllText(Path.Combine(path, "version.txt")).Trim().Should().Be("v2");
        File.Exists(Path.Combine(path, "v2.txt")).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task File_Quiver_did_not_check_in_a_verified_release_asks_first()
    {
        using var launcher = new Launcher();
        var repo = Repo();
        // Quiver checked another file of v2; GitHub now offers this one, which it never saw.
        launcher.Network.Site["/release-status?limit=100"] = Page([Status("alpha", repo, "v2")]);
        launcher.Network.Site["/apps/alpha/release-history?limit=100"] = Page(
            [History("v2", "verified", [], [("alpha-windows-old.zip", Zip("old.txt"))])]);
        var release = OnGitHub("v2");
        launcher.Network.Files[release.assets[0].browser_download_url] = Zip("v2.txt");
        var app = App("Alpha", repo, manager: launcher.Manager);
        var player = new Dialogs(confirm: false);

        await GameDownloadInstallService.DownloadAndInstallAsync(app, launcher.Manager.HttpClient, launcher.Manager.GamesFolder,
            release, launcher.Store.Current, GameStatus.NotInstalled, player, releaseMode: ReleaseInstallMode.ExplicitRelease);

        var asked = player.Confirmations.Should().ContainSingle().Subject;
        asked.Check.State.Should().Be(ReleaseCheckState.Unverified);
        asked.Check.Reasons.Should().Contain(r => r.Contains(Asset));
        launcher.Network.Downloads.Should().Be(0);
        app.Status.Should().Be(GameStatus.NotInstalled);
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(1, false, 2)]
    [InlineData(2, true, 2)]
    public async Task Blocked_release_asks_twice_and_stops_at_the_first_no(int yes, bool installs, int questions)
    {
        var check = new ReleaseCheck(ReleaseCheckState.Blocked, ["The developer pulled this release."], "v1.0",
            new Dictionary<string, string> { [Asset] = "ab12" });
        var asked = new List<(string Message, string Title)>();

        var result = await ReleaseWarnings.ConfirmAsync("Alpha", "v0.9", check, (message, title) =>
        {
            asked.Add((message, title));
            return Task.FromResult(asked.Count <= yes);
        });

        result.Should().Be(installs);
        asked.Should().HaveCount(questions);
        asked[0].Title.Should().Be("Blocked release");
        asked[0].Message.Should().Be(
            "Quiver blocked Alpha v0.9, so it shouldn't be installed.\n\n• The developer pulled this release.\n\n" +
            "The verified version is v1.0.\n\nInstall it anyway?");
        if (questions == 2)
            asked[1].Should().Be(("Are you sure? Quiver blocked Alpha v0.9.", "Install a blocked release?"));
    }

    [Fact]
    public async Task Unverified_release_asks_once_and_points_at_the_verified_release()
    {
        var none = new Dictionary<string, string>();
        async Task<(bool Result, List<(string Message, string Title)> Asked)> Confirm(string version, ReleaseCheck check)
        {
            var asked = new List<(string Message, string Title)>();
            var result = await ReleaseWarnings.ConfirmAsync("Alpha", version, check, (message, title) =>
            {
                asked.Add((message, title));
                return Task.FromResult(true);
            });
            return (result, asked);
        }

        var unseen = new ReleaseCheck(ReleaseCheckState.Unverified, [CatalogReleases.NotSeen], "v1.0", none,
            VerifiedAt: DateTimeOffset.UtcNow.AddHours(2.5));
        var (result, asked) = await Confirm("v1.1", unseen);
        result.Should().BeTrue();
        var (message, title) = asked.Should().ContainSingle().Subject;
        title.Should().Be("Not verified yet");
        message.Should().Be(
            $"Quiver hasn't verified Alpha v1.1 yet.\n\n• {CatalogReleases.NotSeen}\n\n" +
            "It should be verified in about 3 hours.\nThe verified version is v1.0.\n\nInstall it anyway?");

        // With nothing verified yet there is no release to point at; a detection is worth a line.
        var flagged = unseen with { VerifiedVersion = null, VerifiedAt = null, ScanVerdict = "flagged", ScanEngines = "3 engines" };
        (await Confirm("v1.1", flagged)).Asked.Single().Message.Should().Be(
            $"Quiver hasn't verified Alpha v1.1 yet.\n\n• {CatalogReleases.NotSeen}\n• VirusTotal: 3 engines flag one of its files.\n\n" +
            "No version of this app is verified yet.\n\nInstall it anyway?");
    }

    // ---- Fixture ----

    private static string Repo() => "release-verification/" + Guid.NewGuid().ToString("N");

    private static GameInfo App(string name, string repository, string? filter = null, string? folder = null, GameManager? manager = null) => new()
    {
        Name = name, Repository = repository, ReleaseAssetFilter = filter,
        FolderName = folder ?? name.Replace(" ", ""), GameManager = manager,
    };

    /// <summary>An app with v1 on disk that a check has offered <paramref name="latest"/> for.</summary>
    private static GameInfo InstalledApp(Launcher launcher, string repository, string latest)
    {
        var app = App("Alpha", repository, manager: launcher.Manager);
        InstallOnDisk(app, launcher.Manager.GamesFolder, "v1");
        app.InstalledVersion = "v1";
        app.LatestVersion = latest;
        app.Status = GameStatus.UpdateAvailable;
        return app;
    }

    private static void InstallOnDisk(GameInfo app, string gamesFolder, string version)
    {
        var path = app.GetInstallPath(gamesFolder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "version.txt"), version);
        File.WriteAllBytes(Path.Combine(path, "game.exe"), OldExe);
    }

    private static void AssertStillV1(GameInfo app, string gamesFolder)
    {
        var path = app.GetInstallPath(gamesFolder);
        File.ReadAllText(Path.Combine(path, "version.txt")).Trim().Should().Be("v1");
        File.ReadAllBytes(Path.Combine(path, "game.exe")).Should().Equal(OldExe);
        Directory.GetFiles(path).Select(f => Path.GetFileName(f)).Should().BeEquivalentTo(new[] { "version.txt", "game.exe" }, "nothing of v2 was installed");
    }

    private static GitHubRelease OnGitHub(string tag, string? digest = null) => new()
    {
        tag_name = tag,
        assets = [new() { name = Asset, browser_download_url = $"https://downloads.example/{tag}/{Asset}", digest = digest }],
    };

    /// <summary>A Windows build: a launchable game.exe and a file naming the release.</summary>
    private static byte[] Zip(string marker)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var exe = zip.CreateEntry("game.exe").Open()) exe.Write([0x4D, 0x5A, 0x90, 0x00]);
            using var note = new StreamWriter(zip.CreateEntry(marker).Open());
            note.Write(marker);
        }
        return stream.ToArray();
    }

    // Upper case, as a checksum may come; the launcher compares hex case-insensitively.
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string Page(object[] items, string? next = null) =>
        Json(new { items, nextCursor = next, isDone = next == null });

    private static object Status(string slug, string repository, string? verified, string? latest = null) => new
    {
        id = "id-" + slug, slug, provider = "github", repository,
        verified = verified == null ? null : new { version = verified }, latestUpstream = new { version = latest ?? verified },
    };

    private static object Listed(string slug, string folderName, string? releaseAssetFilter = null) =>
        new { slug, launcher = new { folderName, releaseAssetFilter } };

    private static object History(string version, string state, string[] reasons, (string File, byte[] Bytes)[] files,
        object? scan = null, long? checkEndsAt = null, bool rolling = false) => new
    {
        version, state, reasons, scan, checkEndsAt, rolling,
        assets = files.Select(f => new { filename = f.File, url = SiteLink(version, f.File), checksum = "sha256:" + Sha256(f.Bytes) }).ToArray(),
    };

    /// <summary>Where quiverlauncher.com says a release file downloads from.</summary>
    private static string SiteLink(string version, string file) => $"https://files.example/{version}/{file}";

    private sealed class Network : HttpMessageHandler
    {
        /// <summary>quiverlauncher.com answers by path and query after /api/v1; anything else is a 404.</summary>
        public Dictionary<string, string> Site { get; } = [];
        public Dictionary<string, string> GitHub { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = [];
        public ConcurrentQueue<Uri> Requests { get; } = new();
        /// <summary>Broken downloads reported to quiverlauncher.com: app, version, file and problem.</summary>
        public ConcurrentQueue<(string App, string Version, string File, string Problem)> Reports { get; } = new();
        public int Downloads;

        public List<string> SitePaths() => Requests.Select(u => u.AbsolutePath).Where(p => p.StartsWith("/api/v1/")).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue(uri);
            string? body = null;
            if (request.Method == HttpMethod.Post && uri.AbsolutePath.StartsWith("/api/v1/apps/") && uri.AbsolutePath.EndsWith("/download-problem"))
            {
                using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                string Field(string name) => sent.RootElement.GetProperty(name).GetString()!;
                Reports.Enqueue((uri.Segments[^2].TrimEnd('/'), Field("version"), Field("file"), Field("problem")));
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"status\":\"noted\"}") };
            }
            if (uri.AbsolutePath.StartsWith("/api/v1/"))
                Site.TryGetValue(uri.PathAndQuery["/api/v1".Length..], out body);
            else if (uri.Host == "api.github.com")
                GitHub.TryGetValue(uri.AbsolutePath, out body);
            else
            {
                Interlocked.Increment(ref Downloads);
                if (Files.TryGetValue(uri.AbsoluteUri, out var bytes))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            return body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class Dialogs(bool confirm, bool switchRelease = false) : IGameDownloadDialogs
    {
        public List<(string Message, string Title)> Errors { get; } = [];
        public List<(string App, string Version, ReleaseCheck Check)> Confirmations { get; } = [];
        public List<(string App, string Problem, string Version)> Offers { get; } = [];

        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(true);
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string gamePath, LinuxWindowsRunnerConfig? existing = null, bool isInstall = true) =>
            HeadlessGameDownloadDialogs.Instance.ConfigureWindowsRunnerAsync(gamePath, existing, isInstall);
        public Task ShowRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string title)
        {
            Errors.Add((message, title));
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check)
        {
            Confirmations.Add((appName, version, check));
            return Task.FromResult(confirm);
        }
        public Task<bool> OfferOtherReleaseAsync(string appName, string problem, string version)
        {
            Offers.Add((appName, problem, version));
            return Task.FromResult(switchRelease);
        }
    }

    /// <summary>A launcher with its own data folder whose only network is <see cref="Network"/>.</summary>
    private sealed class Launcher : IDisposable
    {
        private readonly string? _previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        private readonly ISettingsStore _previousSettings = SettingsStoreProvider.Default;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-release-verification", Guid.NewGuid().ToString("N"));
        public Network Network { get; } = new();
        public FileSettingsStore Store { get; }
        public GameManager Manager { get; }

        public Launcher()
        {
            Directory.CreateDirectory(_root);
            QuiverLauncherPaths.OverrideUserDataRoot = _root;
            Store = new FileSettingsStore(Path.Combine(_root, "settings.json"));
            Store.Current.Platform = TargetOS.Windows;
            Store.Current.AppsPath = Path.Combine(_root, "Apps");
            Store.Save(Store.Current);
            SettingsStoreProvider.Default = Store;
            Manager = new GameManager(Store, new HttpClient(Network), new AppCatalogService(dataDirectory: _root));
        }

        public void Dispose()
        {
            Manager.Dispose();
            SettingsStoreProvider.Default = _previousSettings;
            QuiverLauncherPaths.OverrideUserDataRoot = _previousRoot;
            TestFixtures.CleanupDirectory(_root);
        }
    }
}
