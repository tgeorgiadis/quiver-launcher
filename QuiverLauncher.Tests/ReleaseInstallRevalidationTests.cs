using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using static QuiverLauncher.Tests.ReleaseIdentityRegressionTests;

namespace QuiverLauncher.Tests;

public class ReleaseInstallRevalidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-release-install-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = QuiverLauncherPaths.OverrideUserDataRoot;
    private readonly AppSettings _settings = new() { Platform = TargetOS.Windows };
    public ReleaseInstallRevalidationTests()
    {
        Directory.CreateDirectory(_root);
        QuiverLauncherPaths.OverrideUserDataRoot = _root;
    }
    public void Dispose() { QuiverLauncherPaths.OverrideUserDataRoot = _previous; Directory.Delete(_root, true); }
    private static GameInfo App() => new() { Name = "DKR-R", FolderName = "game", Repository = "regression/" + Guid.NewGuid().ToString("N") };
    private static HttpResponseMessage Download()
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
        using (var file = zip.CreateEntry(OperatingSystem.IsMacOS() ? "game" : "game.exe").Open())
            file.Write([0x4d, 0x5a, 0x90, 0]);
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_install_revalidates_disk_and_supplied_beta_selections(bool supplied)
    {
        var app = App();
        var beta = Release(Beta, true);
        app.ApplyCachedRelease(Beta, beta);
        GameDownloadService.SelectExplicit(app, beta, _settings, beta.assets[0]);
        GitHubApiCache.SetCache("github", app.Repository!, Beta, "", beta, persist: false);
        var paths = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.Host == "api.github.com" ? Metadata(request) : Download();
        }));
        await GameDownloadInstallService.DownloadAndInstallAsync(app, client, _root, supplied ? beta : null,
            _settings, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance);
        paths.Should().Contain(path => path.EndsWith("/latest"));
        paths.Should().Contain("/" + Stable + ".zip").And.NotContain("/" + Beta + ".zip");
        app.InstalledVersion.Should().Be(Stable);
        File.ReadAllText(Path.Combine(_root, "game", "version.txt")).Trim().Should().Be(Stable);
    }

    [Fact]
    public async Task Automatic_install_revalidates_304_even_inside_background_cache_scope()
    {
        var app = App();
        var calls = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host != "api.github.com") return Download();
            if (++calls == 1)
            {
                var response = Metadata(request);
                response.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
                return response;
            }
            request.Headers.IfNoneMatch.Should().ContainSingle();
            return new(HttpStatusCode.NotModified);
        }));
        await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, app.Repository!, cancellationToken: TestContext.Current.CancellationToken);
        using var scope = ReleaseRequestCoordinator.AllowCachedMetadata(TimeSpan.FromDays(1));
        await GameDownloadInstallService.DownloadAndInstallAsync(app, client, _root, Release(Beta, true),
            _settings, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance);
        calls.Should().Be(2);
        app.InstalledVersion.Should().Be(Stable);
    }

    [Fact]
    public async Task Explicit_prerelease_does_not_get_replaced_by_latest()
    {
        var app = App();
        using var client = new HttpClient(new Handler(request =>
        {
            request.RequestUri!.Host.Should().Be("example.test");
            request.RequestUri.AbsolutePath.Should().Be("/" + Beta + ".zip");
            return Download();
        }));
        await GameDownloadInstallService.DownloadAndInstallAsync(app, client, _root, Release(Beta, true),
            _settings, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance, releaseMode: ReleaseInstallMode.ExplicitRelease);
        app.InstalledVersion.Should().Be(Beta);
    }

    [Fact]
    public async Task Automatic_install_honors_an_explicit_beta_pin()
    {
        var app = App();
        app.PreferredVersion = "Version1.0.5beta10";
        string? downloaded = null;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com") return Metadata(request);
            downloaded = request.RequestUri.AbsolutePath;
            return Download();
        }));
        await GameDownloadInstallService.DownloadAndInstallAsync(app, client, _root, Release(Stable),
            _settings, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance);
        downloaded.Should().Be("/Version1.0.5beta10.zip");
        app.InstalledVersion.Should().Be("Version1.0.5beta10");
    }

    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    public async Task Failed_revalidation_never_downloads_cached_release(int status)
    {
        var app = App();
        using var client = new HttpClient(new Handler(request =>
        {
            request.RequestUri!.Host.Should().Be("api.github.com");
            return new((HttpStatusCode)status);
        }));
        await GameDownloadInstallService.DownloadAndInstallAsync(app, client, _root, Release(Beta, true),
            _settings, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance);
        app.Status.Should().Be(GameStatus.NotInstalled);
        File.Exists(Path.Combine(_root, "game", "version.txt")).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Update_controller_installs_the_pin_it_just_revalidated()
    {
        var store = new FileSettingsStore(Path.Combine(_root, "settings.json"));
        store.Current.AppsPath = Path.Combine(_root, "Apps");
        store.Current.Platform = TargetOS.Windows;
        string? downloaded = null;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com") return Metadata(request);
            downloaded = request.RequestUri.AbsolutePath;
            return Download();
        }));
        using var manager = new GameManager(store, client);
        await using var session = new LauncherSession();
        var app = App();
        app.PreferredVersion = "Version1.0.5beta10";
        app.InstalledVersion = Stable;
        app.Status = GameStatus.UpdateAvailable;
        var controller = new QuiverLauncher.Views.LibraryLaunchController(manager, new(store), session,
            new LibraryPersistenceService(manager), (_, anchor) => anchor,
            (_, _) => throw new Exception("Unexpected picker"), (_, _) => throw new Exception("Unexpected prompt"),
            _ => { }, () => { }, _ => { }, () => 900);
        await controller.HandleUpdateNowAsync(new Button(), app);
        downloaded.Should().Be("/Version1.0.5beta10.zip");
        app.InstalledVersion.Should().Be("Version1.0.5beta10");
    }
}
