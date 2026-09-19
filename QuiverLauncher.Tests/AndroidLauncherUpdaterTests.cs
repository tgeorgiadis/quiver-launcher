using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class AndroidLauncherUpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-updater-tests", Guid.NewGuid().ToString("N"));
    private readonly Installer _installer = new();
    private readonly AppSettings _settings = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly List<AndroidLauncherUpdater> _updaters = [];
    private static object Release(string version = "v3.4.0", bool preview = false, bool draft = false, bool apk = true, string? url = null) => new
    {
        tag_name = version, prerelease = preview, draft,
        assets = apk ? new[] { new { name = "QuiverLauncher-android.apk", size = 3,
            browser_download_url = url ?? $"https://github.com/tgeorgiadis/quiver-launcher/releases/download/{version}/QuiverLauncher-android.apk" } } : []
    };
    private static HttpResponseMessage Releases(params object[] releases)
    {
        var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases)) };
        result.Headers.ETag = new EntityTagHeaderValue("\"release-1\""); return result;
    }
    private AndroidLauncherUpdater Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var updater = new AndroidLauncherUpdater(new HttpClient(new Handler(handler)), _root, _installer, () => _settings,
            now: () => _now, backupUserData: () => LauncherUpdateBackup.Create(_root));
        _updaters.Add(updater); return updater;
    }
    private AndroidLauncherUpdater Create(Func<HttpRequestMessage, HttpResponseMessage> handler) => Create((request, _) => Task.FromResult(handler(request)));
    private static HttpResponseMessage Apk(HttpRequestMessage request) => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]), RequestMessage = request };

    [Theory]
    [InlineData("v3.3.3", false)] [InlineData("v3.3.4", false)] [InlineData("v3.4.0", true)]
    public void Only_newer_versions_are_offered(string version, bool expected) =>
        (AndroidLauncherUpdater.SelectRelease(JsonSerializer.Serialize(new[] { Release(version) }), "3.3.4", false) != null).Should().Be(expected);

    [Fact]
    public void Selection_skips_drafts_previews_and_releases_without_apks()
    {
        var json = JsonSerializer.Serialize(new[] { Release("v4.0.0", draft: true), Release("v3.6.0", apk: false), Release("v3.5.0-beta.1", preview: true), Release() });
        AndroidLauncherUpdater.SelectRelease(json, "3.3.4", false)!.Version.Should().Be("v3.4.0");
        AndroidLauncherUpdater.SelectRelease(json, "3.3.4", true)!.Version.Should().Be("v3.5.0-beta.1");
    }
    [Theory]
    [InlineData("http://github.com/tgeorgiadis/quiver-launcher/releases/download/v3.4.0/QuiverLauncher-android.apk")]
    [InlineData("https://example.com/QuiverLauncher-android.apk")]
    [InlineData("https://github.com/another/repo/releases/download/v3.4.0/QuiverLauncher-android.apk")]
    public void Unsupported_download_sources_are_not_offered(string url) =>
        AndroidLauncherUpdater.SelectRelease(JsonSerializer.Serialize(new[] { Release(url: url) }), "3.3.4", false).Should().BeNull();

    [Fact]
    public async Task Checks_are_throttled_and_conditional_but_manual_checks_are_immediate()
    {
        var calls = 0;
        var updater = Create(request => { calls++; if (calls == 1) return Releases(Release()); request.Headers.IfNoneMatch.Should().ContainSingle(); return new(HttpStatusCode.NotModified); });
        await updater.CheckAsync(); await updater.CheckAsync(); calls.Should().Be(1);
        await updater.CheckAsync(true); calls.Should().Be(2); updater.HasUpdate.Should().BeTrue();
        _now += TimeSpan.FromHours(6); await updater.CheckAsync(); calls.Should().Be(3);
    }
    [Fact]
    public async Task Offline_failure_retains_update_and_dismissal_across_restarts()
    {
        var updater = Create(_ => Releases(Release())); await updater.CheckAsync(); updater.Dismiss();
        var offline = Create(_ => throw new HttpRequestException("Offline"));
        offline.ShowBanner.Should().BeFalse(); offline.HasUpdate.Should().BeTrue();
        await offline.CheckAsync(true); offline.Error.Should().Be("Offline"); offline.HasUpdate.Should().BeTrue();
        var next = Create(_ => Releases(Release("v3.5.0"))); await next.CheckAsync(true); next.ShowBanner.Should().BeTrue();
    }
    [Fact]
    public async Task Failure_does_not_claim_up_to_date_after_restart()
    {
        var updater = Create(_ => throw new HttpRequestException("Offline")); await updater.CheckAsync();
        Create(_ => Releases()).AvailableText.Should().Be("Check for Quiver updates");
    }
    [Fact]
    public async Task Rate_limit_survives_restart_and_manual_checks_respect_it()
    {
        var calls = 0;
        var updater = Create(_ => { calls++; var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(15)); return response; });
        await updater.CheckAsync(); await updater.CheckAsync(true); calls.Should().Be(1);
        var next = Create(_ => { calls++; return Releases(Release()); });
        await next.CheckAsync(true); calls.Should().Be(1);
        _now += TimeSpan.FromMinutes(16); await next.CheckAsync(true); calls.Should().Be(2); next.HasError.Should().BeFalse();
    }
    [Fact]
    public async Task Changing_channel_invalidates_conditional_cache_and_ignores_cadence()
    {
        var calls = 0;
        var updater = Create(request => { calls++; request.Headers.IfNoneMatch.Should().BeEmpty(); return Releases(Release("v3.5.0-beta.1", preview: true)); });
        await updater.CheckAsync(); updater.HasUpdate.Should().BeFalse();
        _settings.AllowPrereleaseLauncherUpdates = true;
        await updater.CheckAsync(); calls.Should().Be(2); updater.HasUpdate.Should().BeTrue();
    }
    [Fact]
    public async Task Concurrent_checks_make_one_request()
    {
        var entered = new TaskCompletionSource(); var finish = new TaskCompletionSource(); var calls = 0;
        var updater = Create(async (_, _) => { calls++; entered.SetResult(); await finish.Task; return Releases(Release()); });
        var check = updater.CheckAsync(); await entered.Task; await updater.CheckAsync(true); finish.SetResult(); await check; calls.Should().Be(1);
    }
    [Fact]
    public async Task Cancelled_install_keeps_valid_apk_and_retry_does_not_download_again()
    {
        var downloads = 0;
        var updater = Create(request => { if (request.RequestUri!.Host == "api.github.com") return Releases(Release()); downloads++; return Apk(request); });
        await updater.CheckAsync(); await updater.UpdateAsync();
        updater.ActionText.Should().Be("Install update"); updater.State.Should().Be(AndroidLauncherUpdateState.Ready);
        _installer.Installs.Should().Be(1); downloads.Should().Be(1);
        var next = Create(_ => throw new Exception("No request expected"));
        await next.UpdateAsync(); _installer.Installs.Should().Be(2); next.HasError.Should().BeFalse();
    }
    [Fact]
    public async Task Each_installer_attempt_has_a_complete_snapshot_and_retry_preserves_the_previous_copy()
    {
        var downloads = 0;
        var updater = Create(request =>
        {
            if (request.RequestUri!.Host == "api.github.com") return Releases(Release());
            downloads++;
            return Apk(request);
        });
        var appsPath = Path.Combine(_root, "apps.json");
        var settingsPath = Path.Combine(_root, "settings.json");
        File.WriteAllText(appsPath, "{\"apps\":[{\"name\":\"Saved app\"}]}");
        File.WriteAllText(settingsPath, "{\"FirstStartup\":false}");
        var apps = File.ReadAllBytes(appsPath);
        var settings = File.ReadAllBytes(settingsPath);
        _installer.Install = () =>
        {
            var snapshot = Directory.GetDirectories(Path.Combine(_root, "Backups", "updates")).Single();
            File.ReadAllBytes(Path.Combine(snapshot, "apps.json")).Should().Equal(apps);
            File.ReadAllBytes(Path.Combine(snapshot, "settings.json")).Should().Equal(settings);
            File.Exists(Path.Combine(snapshot, "manifest.json")).Should().BeTrue();
            throw new InvalidOperationException("Permission denied");
        };
        await updater.CheckAsync();
        await updater.UpdateAsync();
        updater.Error.Should().Be("Permission denied");
        File.WriteAllText(settingsPath, "{\"FirstStartup\":false,\"MouseWheelScrollSpeed\":3}");
        _installer.Install = () =>
        {
            var snapshots = Directory.GetDirectories(Path.Combine(_root, "Backups", "updates"));
            snapshots.Should().HaveCount(2);
            snapshots.Should().Contain(p => File.ReadAllBytes(Path.Combine(p, "settings.json")).SequenceEqual(settings));
            snapshots.Should().Contain(p => File.ReadAllBytes(Path.Combine(p, "settings.json")).SequenceEqual(File.ReadAllBytes(settingsPath)));
            snapshots.Should().OnlyContain(p => File.ReadAllBytes(Path.Combine(p, "apps.json")).SequenceEqual(apps));
            return Task.CompletedTask;
        };
        await updater.UpdateAsync();
        updater.HasError.Should().BeFalse();
        _installer.Installs.Should().Be(2);
        downloads.Should().Be(1);
        File.ReadAllBytes(appsPath).Should().Equal(apps);
    }

    [Fact]
    public async Task Backup_failure_blocks_installer_handoff()
    {
        var updater = Create(request => request.RequestUri!.AbsolutePath.EndsWith(".apk") ? Apk(request) : Releases(Release()));
        File.WriteAllText(Path.Combine(_root, "Backups"), "not a directory");
        await updater.CheckAsync();
        await updater.UpdateAsync();
        updater.Error.Should().Contain("update stopped");
        _installer.Installs.Should().Be(0);
        updater.ActionText.Should().Be("Install update");
    }

    [Fact]
    public async Task Installer_handoff_is_persisted_before_launch_and_resume_does_not_start_a_check()
    {
        var entered = new TaskCompletionSource(); var finish = new TaskCompletionSource();
        _installer.Install = async () => { var state = JsonSerializer.Deserialize<AndroidLauncherUpdateCache>(File.ReadAllText(Path.Combine(_root, "update.json")))!; state.InstallationHandedOff.Should().BeTrue(); entered.SetResult(); await finish.Task; };
        var calls = 0; var updater = Create(request => { calls++; return calls == 1 ? Releases(Release()) : Apk(request); });
        await updater.CheckAsync(); var update = updater.UpdateAsync(); await entered.Task;
        updater.State.Should().Be(AndroidLauncherUpdateState.AwaitingInstallation); await updater.CheckAsync(true); calls.Should().Be(2);
        finish.SetResult(); await update;
    }
    [Fact]
    public async Task Successful_install_clears_download_and_badge_on_next_startup()
    {
        var updater = Create(request => request.RequestUri!.Host == "api.github.com" ? Releases(Release()) : Apk(request));
        await updater.CheckAsync(); await updater.UpdateAsync();
        _installer.Installed = new("com.quiverlauncher.app", 30400, "3.4.0");
        var next = Create(_ => Releases()); next.HasUpdate.Should().BeFalse(); next.ShowBanner.Should().BeFalse();
        File.Exists(Path.Combine(_root, "quiver-update.apk")).Should().BeFalse();
    }
    [Fact]
    public async Task Invalid_apk_never_reaches_installer_and_partial_is_deleted()
    {
        _installer.Invalid = true;
        var updater = Create(request => request.RequestUri!.Host == "api.github.com" ? Releases(Release()) : Apk(request));
        await updater.CheckAsync(); await updater.UpdateAsync();
        updater.HasError.Should().BeTrue(); _installer.Installs.Should().Be(0);
        Directory.GetFiles(_root, "*.apk*").Should().BeEmpty();
    }
    [Fact]
    public async Task Tampered_completed_apk_is_revalidated_before_reuse()
    {
        var updater = Create(request => request.RequestUri!.Host == "api.github.com" ? Releases(Release()) : Apk(request));
        await updater.CheckAsync(); await updater.UpdateAsync(); _installer.Invalid = true;
        await updater.UpdateAsync(); _installer.Installs.Should().Be(1); updater.ActionText.Should().Be("Update");
    }
    [Fact]
    public async Task Permission_failure_retains_apk_for_retry()
    {
        _installer.Install = () => throw new InvalidOperationException("Permission denied");
        var updater = Create(request => request.RequestUri!.Host == "api.github.com" ? Releases(Release()) : Apk(request));
        await updater.CheckAsync(); await updater.UpdateAsync(); updater.Error.Should().Be("Permission denied"); updater.ActionText.Should().Be("Install update");
    }
    [Fact]
    public async Task Download_cancellation_is_not_an_error_and_can_retry()
    {
        var downloading = new TaskCompletionSource(); var blocked = true;
        var updater = Create(async (request, token) =>
        {
            if (request.RequestUri!.Host == "api.github.com") return Releases(Release());
            if (blocked) { downloading.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return Apk(request);
        });
        await updater.CheckAsync(); var update = updater.UpdateAsync(); await downloading.Task; updater.CancelDownload(); await update;
        updater.HasError.Should().BeFalse(); updater.CanUpdate.Should().BeTrue(); _installer.Installs.Should().Be(0);
        blocked = false; await updater.UpdateAsync(); _installer.Installs.Should().Be(1);
    }
    [Fact]
    public async Task Truncated_download_cannot_be_installed()
    {
        var updater = Create(request => request.RequestUri!.Host == "api.github.com" ? Releases(Release()) : new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]), RequestMessage = request });
        await updater.CheckAsync(); await updater.UpdateAsync(); updater.HasError.Should().BeTrue(); _installer.Installs.Should().Be(0);
    }
    [AvaloniaTheory]
    [InlineData(320)] [InlineData(360)]
    public async Task Narrow_update_controls_wrap_and_do_not_take_focus(int width)
    {
        var updater = Create(_ => Releases(Release())); await updater.CheckAsync();
        var view = new AndroidLauncherUpdateView { DataContext = updater }; var input = new TextBox();
        var panel = new StackPanel(); panel.Children.Add(input); panel.Children.Add(view);
        var window = new Window { Width = width, Height = 650, Content = panel };
        try
        {
            window.Show(); window.UpdateLayout(); input.Focus();
            await updater.CheckAsync(true); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            input.IsFocused.Should().BeTrue();
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToArray();
            buttons.Should().HaveCount(3);
            foreach (var button in buttons) { button.Bounds.Height.Should().BeGreaterThanOrEqualTo(44); button.Bounds.Width.Should().BeLessThan(width); }
            for (var i = 0; i < buttons.Length; i++) for (var j = i + 1; j < buttons.Length; j++)
                new Avalonia.Rect(buttons[i].TranslatePoint(default, view)!.Value, buttons[i].Bounds.Size).Deflate(0.1)
                    .Intersects(new Avalonia.Rect(buttons[j].TranslatePoint(default, view)!.Value, buttons[j].Bounds.Size).Deflate(0.1)).Should().BeFalse();
            view.FindControl<Button>("UpdateButton")!.Focus(); view.FindControl<Button>("UpdateButton")!.IsFocused.Should().BeTrue();
        }
        finally { window.Close(); }
    }
    public void Dispose()
    {
        foreach (var updater in _updaters) updater.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken); }
    private sealed class Installer : IAndroidLauncherInstaller
    {
        public LauncherApkIdentity Installed { get; set; } = new("com.quiverlauncher.app", 30304, "3.3.4");
        public bool Invalid; public int Installs;
        public Func<Task>? Install;
        public Task<LauncherApkIdentity> ValidateAsync(string path, string expectedVersion) => Invalid ? throw new InvalidDataException("Invalid package/signature") : Task.FromResult(new LauncherApkIdentity(Installed.PackageName, 30400, "3.4.0"));
        public async Task InstallAsync(string path) { Installs++; if (Install != null) await Install(); }
    }
}
