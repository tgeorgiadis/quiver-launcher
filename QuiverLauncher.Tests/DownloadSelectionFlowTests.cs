using System.Net;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class DownloadSelectionFlowTests
{
    [AvaloniaTheory]
    [InlineData("initial")]
    [InlineData("update")]
    [InlineData("explicit-version")]
    public async Task One_windows_build_installs_without_picker_in_each_flow(string flow)
    {
        using var fixture = new Fixture();
        fixture.Handler.Single = true;
        await using var session = new LauncherSession();
        var game = fixture.Game();
        if (flow == "initial")
            await GameDownloadInstallService.DownloadAndInstallAsync(game, fixture.Manager.HttpClient, fixture.Manager.GamesFolder,
                fixture.Handler.Release, fixture.Settings.Current, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance);
        else if (flow == "update")
        {
            game.Status = GameStatus.UpdateAvailable;
            game.InstalledVersion = "v0";
            (await fixture.Controller(session).HandleUpdateNowAsync(new Button(), game)).Should().BeTrue();
        }
        else
        {
            game.Status.Should().Be(GameStatus.NotInstalled);
            game.CanChangeVersion.Should().BeTrue();
            game.CanVersionOptions.Should().BeTrue();
            game.LatestVersion = "v2";
            await fixture.Controller(session).ShowReleaseDownloadSelectionMenuAsync(new Button(), game, fixture.Handler.Release, "v1", "v2");
            game.PreferredVersion.Should().Be("v1");
        }
        fixture.Handler.Downloads.Should().Be(1);
        game.Status.Should().Be(GameStatus.Installed);
        File.ReadAllText(Path.Combine(game.GetInstallPath(fixture.Manager.GamesFolder), "version.txt")).Trim().Should().Be("v1");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Other_downloads_submenu_can_be_reached_and_activated(bool gamepad)
    {
        using var fixture = new Fixture();
        await using var session = new LauncherSession();
        var controller = fixture.Controller(session);
        var choices = DownloadAssetPolicy.Select(DownloadAssetPolicyTests.Release("game-windows.zip", "game-windows-portable.zip", "unknown", "game-macos.zip", "game-windows.sha256"), "Windows");
        string? selected = null;
        var menu = new ContextMenu();
        foreach (var item in controller.CreateDownloadChoices(choices, a => { selected = a.name; return Task.CompletedTask; })) menu.Items.Add(item);
        var button = new Button { Content = "Download" };
        var window = new Window { Width = 900, Height = 600, Content = button };
        try
        {
            window.Show();
            GamepadContextMenuNavigation.Attach(menu);
            menu.Open(button);
            Dispatcher.UIThread.RunJobs();
            var items = menu.Items.OfType<MenuItem>().ToList();
            items.Should().HaveCount(3);
            var other = items[2];
            other.Header.Should().Be("Show other downloads");
            if (gamepad)
            {
                GamepadContextMenuNavigation.Instance.TryHandleNavigation(Services.NavigationDirection.Down);
                GamepadContextMenuNavigation.Instance.TryHandleNavigation(Services.NavigationDirection.Down);
                GamepadContextMenuNavigation.Instance.TryHandleConfirm();
                Dispatcher.UIThread.RunJobs();
                other.IsSubMenuOpen.Should().BeTrue();
                GamepadContextMenuNavigation.Instance.TryHandleConfirm();
            }
            else
            {
                items[0].Focus();
                Press(Key.Down, PhysicalKey.ArrowDown);
                Press(Key.Down, PhysicalKey.ArrowDown);
                Press(Key.Right, PhysicalKey.ArrowRight);
                other.IsSubMenuOpen.Should().BeTrue();
                Press(Key.Enter, PhysicalKey.Enter);
            }
            Dispatcher.UIThread.RunJobs();
            selected.Should().Be("unknown");
        }
        finally
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
            menu.Close(); window.Close();
        }
        void Press(Key key, PhysicalKey physical)
        {
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task Unattended_ambiguous_update_stays_pending_without_downloading()
    {
        using var fixture = new Fixture();
        await using var session = new LauncherSession();
        var game = fixture.Game();
        game.Status = GameStatus.UpdateAvailable;
        game.InstalledVersion = "v0";
        var result = await fixture.Controller(session).HandleUpdateNowAsync(new Button(), game,
            preferAutoPlatform: true, allowAssetPicker: false);
        result.Should().BeFalse();
        game.Status.Should().Be(GameStatus.UpdateAvailable);
        fixture.Handler.Downloads.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_installs_the_build_already_installed_without_a_picker(bool interactive)
    {
        using var fixture = new Fixture();
        fixture.Handler.Assets = ["Game-1.0.0-windows-x64.zip", "Game-1.0.0-windows-x64-portable.zip", "Game-1.0.0-macos.zip"];
        await using var session = new LauncherSession();
        var game = fixture.Game();
        game.Status = GameStatus.UpdateAvailable;
        game.InstalledVersion = "0.9.0";
        var gamePath = game.GetInstallPath(fixture.Manager.GamesFolder);
        Directory.CreateDirectory(gamePath);
        game.InstalledBuild = new InstalledBuild("Game-0.9.0-windows-x64-portable.zip", new string('0', 64));
        await game.InstalledBuild.WriteAsync(gamePath);
        var result = await fixture.Controller(session).HandleUpdateNowAsync(new Button(), game,
            preferAutoPlatform: !interactive, allowAssetPicker: interactive, interactive: interactive);
        result.Should().BeTrue();
        fixture.Handler.Downloaded.Should().Equal("Game-1.0.0-windows-x64-portable.zip");
        game.Status.Should().Be(GameStatus.Installed);
        InstalledBuild.Read(gamePath)!.FileName.Should().Be("Game-1.0.0-windows-x64-portable.zip");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_installs_a_nightlys_new_build_under_the_same_tag(bool recorded)
    {
        using var fixture = new Fixture();
        // Tonight's build names its files after its commit, as last night's did.
        fixture.Handler.Assets = recorded
            ? ["ymir-windows-x86_64-AVX2-v0.4.2-dev_8cdf0a53.zip", "ymir-windows-x86_64-SSE2-v0.4.2-dev_8cdf0a53.zip", "ymir-windows-ARM64-v0.4.2-dev_8cdf0a53.zip"]
            : ["ymir-windows-x86_64-AVX2-v0.4.2-dev_8cdf0a53.zip"];
        fixture.Handler.Rolling = ("latest-nightly", new DateTimeOffset(2026, 10, 19, 9, 40, 0, TimeSpan.Zero));
        await using var session = new LauncherSession();
        var game = fixture.Game();
        var gamePath = game.GetInstallPath(fixture.Manager.GamesFolder);
        Directory.CreateDirectory(gamePath);
        File.WriteAllBytes(Path.Combine(gamePath, "game.exe"), [0x4D, 0x5A, 0x01, 0x01]);
        File.WriteAllText(Path.Combine(gamePath, "version.txt"), "latest-nightly");
        File.SetLastWriteTimeUtc(Path.Combine(gamePath, "version.txt"), new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
        if (recorded)
        {
            game.InstalledBuild = new InstalledBuild("ymir-windows-x86_64-AVX2-v0.4.2-dev_1a2b3c4d.zip", new string('0', 64),
                new DateTimeOffset(2026, 10, 18, 9, 40, 0, TimeSpan.Zero));
            await game.InstalledBuild.WriteAsync(gamePath);
        }
        await GameStatusService.CheckStatusAsync(game, fixture.Manager.HttpClient, fixture.Manager.GamesFolder, forceUpdateCheck: true);
        game.Status.Should().Be(GameStatus.UpdateAvailable);

        var result = await fixture.Controller(session).HandleUpdateNowAsync(new Button(), game);

        result.Should().BeTrue("the same tag's new build is downloaded, not taken for the installed one");
        fixture.Handler.Downloaded.Should().Equal("ymir-windows-x86_64-AVX2-v0.4.2-dev_8cdf0a53.zip");
        game.Status.Should().Be(GameStatus.Installed);
        InstalledBuild.Read(gamePath)!.FileName.Should().Be("ymir-windows-x86_64-AVX2-v0.4.2-dev_8cdf0a53.zip");
        await GameStatusService.CheckStatusAsync(game, fixture.Manager.HttpClient, fixture.Manager.GamesFolder, forceUpdateCheck: true);
        game.Status.Should().Be(GameStatus.Installed, "checking again finds the build just installed");
    }

    [AvaloniaFact]
    public async Task Picker_lists_the_installed_build_first()
    {
        using var fixture = new Fixture();
        await using var session = new LauncherSession();
        var choices = DownloadAssetPolicy.Select(DownloadAssetPolicyTests.Release("game-windows.zip", "game-windows-portable.zip"), "Windows");
        var items = fixture.Controller(session).CreateDownloadChoices(choices, _ => Task.CompletedTask, choices.Eligible[1]).ToList();
        items.Select(i => ((GitHubAsset)i.Tag!).name).Should().Equal("game-windows-portable.zip", "game-windows.zip");
        Text(items[0]).Should().Be("game-windows-portable.zip (Same build as installed)");
        items[0].Classes.Should().Contain("accent");
        items[1].Classes.Should().NotContain("accent");
        static string? Text(MenuItem item) => ((Panel)item.Header!).Children.OfType<TextBlock>().Single().Text;
    }

    [Fact]
    public async Task CLI_ambiguous_download_exits_without_waiting_for_a_picker()
    {
        using var fixture = new Fixture();
        var cli = new CLIHandler(fixture.Manager);
        var method = typeof(CLIHandler).GetMethod("UpdateOrDownloadGame", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var operation = (Task<int>)method.Invoke(cli, [fixture.Game(), false])!;
        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(1);
        fixture.Handler.Downloads.Should().Be(0);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? _previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        private readonly ISettingsStore _previousSettings = SettingsStoreProvider.Default;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-download-policy", Guid.NewGuid().ToString("N"));
        private readonly HttpClient _client;
        public readonly Handler Handler = new();
        public readonly GameManager Manager;
        public readonly SettingsViewModel Settings;
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            QuiverLauncherPaths.OverrideUserDataRoot = _root;
            var store = new FileSettingsStore(Path.Combine(_root, "settings.json"));
            store.Current.Platform = TargetOS.Windows;
            store.Current.AppsPath = Path.Combine(_root, "Apps");
            store.Save(store.Current);
            SettingsStoreProvider.Default = store;
            _client = new(Handler);
            Manager = new(store, _client, new AppCatalogService(dataDirectory: _root));
            Settings = new(store);
        }
        public GameInfo Game() => new() { Name = "Test", Repository = "test/" + Guid.NewGuid().ToString("N"), FolderName = "Test", GameManager = Manager };
        public LibraryLaunchController Controller(LauncherSession session) => new(Manager, Settings, session,
            new LibraryPersistenceService(Manager), (_, anchor) => anchor,
            (_, _) => throw new Exception("Unexpected picker"), (_, _) => throw new Exception("Unexpected prompt"),
            _ => { }, () => { }, _ => { }, () => 900);
        public void Dispose()
        {
            Manager.Dispose(); _client.Dispose();
            SettingsStoreProvider.Default = _previousSettings;
            QuiverLauncherPaths.OverrideUserDataRoot = _previousRoot;
            Directory.Delete(_root, true);
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Downloads;
        public readonly List<string> Downloaded = [];
        public bool Single;
        /// <summary>The release's files, each of which can be downloaded.</summary>
        public string[]? Assets;
        /// <summary>The release's tag, and when its files were uploaded, for a release other than v1.</summary>
        public (string Tag, DateTimeOffset UploadedAt)? Rolling;
        public GitHubRelease Release
        {
            get
            {
                var release = Assets != null ? DownloadAssetPolicyTests.Release(Assets) : Single
                    ? DownloadAssetPolicyTests.Release("game-windows.zip", "game-windows.zip.sha256", "game-macos.zip", "game-linux.AppImage", "Nautilus-Alfa-iOS.zip",
                        "game-windows.zip.provenance.json", "game-rg34xxsp-stockos64-mod.zip", "game-sbc-portmaster.zip", "game-xbox-uwp.zip")
                    : DownloadAssetPolicyTests.Release("game-windows.zip", "game-windows-portable.zip", "game-macos.zip");
                if (Rolling is { } rolling)
                {
                    release.tag_name = rolling.Tag;
                    foreach (var asset in release.assets) asset.updated_at = rolling.UploadedAt;
                }
                return release;
            }
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // These apps are not in the quiverlauncher.com catalog.
            if (request.RequestUri!.Host == "api.quiverlauncher.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (request.RequestUri!.Host != "api.github.com")
            {
                Downloads++;
                Downloaded.Add(Path.GetFileName(request.RequestUri.AbsolutePath));
                if (Assets == null && (!Single || !request.RequestUri.AbsolutePath.EndsWith("/game-windows.zip"))) throw new Exception("Unexpected asset download");
                using var stream = new MemoryStream();
                using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
                using (var file = zip.CreateEntry("game.exe").Open()) file.Write([0x4D, 0x5A, 0x90, 0x00]);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(stream.ToArray()) });
            }
            var release = Release;
            var json = request.RequestUri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(release) : JsonSerializer.Serialize(new[] { release });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
