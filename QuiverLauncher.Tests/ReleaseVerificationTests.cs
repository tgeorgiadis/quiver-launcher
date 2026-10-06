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
        site["/release-status?limit=100"] = Page([Status("alpha", alphaRepo, "v1.0"), Status("shared-pc", sharedRepo, "v3.0")], next: "c2");
        site["/release-status?limit=100&cursor=c2"] = Page([Status("shared-android", sharedRepo, "v3.1")]);
        site["/apps/shared-pc"] = Detail("Shared", "windows", sharedRepo);
        site["/apps/shared-android"] = Detail("Shared", "android", sharedRepo);
        var alpha = App("Alpha", alphaRepo);
        var pc = App("Shared PC", sharedRepo, filter: " Windows ");
        var android = App("Shared Android", sharedRepo, filter: "android");
        // Neither filter nor folder picks one entry: better unlinked than checked against another app's files.
        var ambiguous = App("Shared Linux", sharedRepo, filter: "linux", folder: "Shared");
        var outsider = App("Outsider", Repo());

        await launcher.Manager.CatalogReleases.RefreshAsync([alpha, pc, android, ambiguous, outsider], token);

        (alpha.CatalogSlug, alpha.CatalogVerifiedVersion, alpha.ReleaseTarget).Should().Be(("alpha", "v1.0", "v1.0"));
        (pc.CatalogSlug, pc.CatalogVerifiedVersion).Should().Be(("shared-pc", "v3.0"));
        (android.CatalogSlug, android.CatalogVerifiedVersion).Should().Be(("shared-android", "v3.1"));
        ambiguous.CatalogSlug.Should().BeNull();
        ambiguous.CatalogVerifiedVersion.Should().BeNull();
        outsider.CatalogSlug.Should().BeNull();
        outsider.ReleaseTarget.Should().BeNull();
        launcher.Network.SitePaths().Should().BeEquivalentTo(
            new[] { "/api/v1/release-status", "/api/v1/release-status", "/api/v1/apps/shared-pc", "/api/v1/apps/shared-android" },
            "only apps sharing a repository need their page");

        // An app outside the catalog is left alone, and a fresh status is not read again.
        (await launcher.Manager.CatalogReleases.CheckAsync(outsider, "v1.0", token)).Should().BeNull();
        await launcher.Manager.CatalogReleases.RefreshAsync([alpha, pc, android], token);
        launcher.Network.SitePaths().Should().HaveCount(4);
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
            release, launcher.Store.Current, GameStatus.UpdateAvailable, new AutomaticGameDownloadDialogs(player));

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
            release, launcher.Store.Current, GameStatus.UpdateAvailable, dialogs);

        launcher.Network.Downloads.Should().Be(1);
        dialogs.Confirmations.Should().BeEmpty("the release itself is verified");
        var error = dialogs.Errors.Should().ContainSingle().Subject;
        error.Title.Should().Be("Download Not Verified");
        error.Message.Should().Contain($"{Asset} isn't the file Quiver checked");
        app.InstalledVersion.Should().Be("v1");
        app.Status.Should().Be(GameStatus.UpdateAvailable);
        AssertStillV1(app, launcher.Manager.GamesFolder);
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
            release, launcher.Store.Current, GameStatus.NotInstalled, player);

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
            release, launcher.Store.Current, GameStatus.NotInstalled, player);

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
        asked[0].Title.Should().Be("Install a blocked release?");
        asked[0].Message.Should().Contain("Quiver blocked Alpha v0.9").And.Contain("• The developer pulled this release.")
            .And.Contain("The verified release is v1.0");
        if (questions == 2)
            asked[1].Message.Should().StartWith("Quiver blocked Alpha v0.9. Install it anyway?");
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
        title.Should().Be("Install before it's verified?");
        message.Should().StartWith("Quiver hasn't verified Alpha v1.1.")
            .And.Contain("• " + CatalogReleases.NotSeen)
            .And.Contain("downloads them as the developer published them")
            .And.Contain("verified in about 3 hours")
            .And.Contain("The verified release is v1.0.")
            .And.EndWith("Install v1.1 anyway?");

        // Pinned files are promised; with nothing verified yet there is no release to point at.
        var pinned = unseen with { VerifiedVersion = null, VerifiedAt = null, Checksums = new Dictionary<string, string> { [Asset] = "ab12" } };
        (await Confirm("v1.1", pinned)).Asked.Single().Message.Should()
            .Contain("refuses any that changed since").And.Contain("No release of this app is verified yet.")
            .And.NotContain("The verified release is");
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

    private static object Status(string slug, string repository, string? verified) =>
        new { id = "id-" + slug, slug, provider = "github", repository, verified = verified == null ? null : new { version = verified } };

    private static string Detail(string folderName, string releaseAssetFilter, string repository) => Json(new
    {
        entry = new { slug = folderName, launcher = new { folderName, releaseAssetFilter } },
        project = new { provider = "github", repository },
    });

    private static object History(string version, string state, string[] reasons, (string File, byte[] Bytes)[] files,
        object? scan = null, long? checkEndsAt = null) => new
    {
        version, state, reasons, scan, checkEndsAt,
        assets = files.Select(f => new { filename = f.File, checksum = "sha256:" + Sha256(f.Bytes) }).ToArray(),
    };

    private sealed class Network : HttpMessageHandler
    {
        /// <summary>quiverlauncher.com answers by path and query after /api/v1; anything else is a 404.</summary>
        public Dictionary<string, string> Site { get; } = [];
        public Dictionary<string, string> GitHub { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = [];
        public ConcurrentQueue<Uri> Requests { get; } = new();
        public int Downloads;

        public List<string> SitePaths() => Requests.Select(u => u.AbsolutePath).Where(p => p.StartsWith("/api/v1/")).ToList();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue(uri);
            string? body = null;
            if (uri.AbsolutePath.StartsWith("/api/v1/"))
                Site.TryGetValue(uri.PathAndQuery["/api/v1".Length..], out body);
            else if (uri.Host == "api.github.com")
                GitHub.TryGetValue(uri.AbsolutePath, out body);
            else
            {
                Interlocked.Increment(ref Downloads);
                if (Files.TryGetValue(uri.AbsoluteUri, out var bytes))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
            return Task.FromResult(body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class Dialogs(bool confirm) : IGameDownloadDialogs
    {
        public List<(string Message, string Title)> Errors { get; } = [];
        public List<(string App, string Version, ReleaseCheck Check)> Confirmations { get; } = [];

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
