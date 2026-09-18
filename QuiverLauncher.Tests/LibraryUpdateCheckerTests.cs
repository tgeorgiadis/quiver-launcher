using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LibraryUpdateCheckerTests(ITestOutputHelper output)
{
    private static string Release(string version = "2.0", bool assets = true, bool preview = false) =>
        $$"""{"tag_name":"{{version}}","prerelease":{{preview.ToString().ToLowerInvariant()}},"assets":{{(assets ? "[{\"name\":\"app.zip\",\"browser_download_url\":\"https://example.com/app.zip\"}]" : "[]")}}} """;
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body), Headers = { ETag = new EntityTagHeaderValue("\"fixture\"") } };
    private static GameInfo App(string repository = "fixture/app", string? pin = null) => new()
    { Name = repository, FolderName = Guid.NewGuid().ToString("N"), Repository = repository, PreferredVersion = pin,
        InstalledVersion = "1.0", Status = GameStatus.Installed };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return send(request, token); }
    }

    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Latest_first_halves_requests_with_controlled_network_latency(int count)
    {
        var handler = new Handler(async (r, ct) =>
        {
            await Task.Delay(15, ct);
            return Ok(r.RequestUri!.AbsolutePath.EndsWith("/latest") ? Release() : "[" + Release() + "]");
        });
        using var client = new HttpClient(handler);
        var apps = Enumerable.Range(0, count).Select(i => App("benchmark/app" + i)).ToArray();
        var timer = Stopwatch.StartNew();
        await Task.WhenAll(apps.Select(a => GitHubReleaseService.FetchReleasesAsync(client, a.Repository!, cancellationToken: TestContext.Current.CancellationToken)));
        var before = timer.ElapsedMilliseconds;
        handler.Calls.Should().Be(count * 2);
        handler.Calls = 0; timer.Restart();
        var result = await new LibraryUpdateChecker(client, new()).CheckAsync(apps, true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        var after = timer.ElapsedMilliseconds;
        result.Successful.Should().Be(count); handler.Calls.Should().Be(count);
        apps.Should().OnlyContain(a => a.Status == GameStatus.UpdateAvailable && a.LatestVersion == "2.0");
        output.WriteLine($"{count} apps, 15ms/request: previous={before}ms/{count * 2} requests; latest-first={after}ms/{count} requests");
    }

    [Fact]
    public async Task Pins_and_duplicate_entries_share_raw_responses_and_keep_their_own_selection()
    {
        var handler = new Handler((r, _) => Task.FromResult(Ok(r.RequestUri!.AbsolutePath.EndsWith("/latest")
            ? Release("2.0") : "[" + Release("3.0") + "," + Release("1.5") + "]")));
        using var client = new HttpClient(handler);
        var apps = new[] { App(), App(pin: "1.5"), App(), App(pin: "1.5") };
        var result = await new LibraryUpdateChecker(client, new()).CheckAsync(apps, true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        result.Successful.Should().Be(4); handler.Calls.Should().Be(2);
        apps.Select(a => a.LatestVersion).Should().Equal("2.0", "1.5", "2.0", "1.5");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_latest_or_assets_falls_back_to_prerelease_list(bool missing)
    {
        var handler = new Handler((r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/latest")
            ? missing ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(Release(assets: false))
            : Ok("[" + Release("3.0-beta", preview: true) + "]")));
        using var client = new HttpClient(handler);
        var app = App();
        (await new LibraryUpdateChecker(client, new()).CheckAsync([app], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken)).Complete.Should().BeTrue();
        app.LatestVersion.Should().Be("3.0-beta"); handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Cached_background_pass_is_offline_and_a_new_credential_revalidates()
    {
        var handler = new Handler((_, _) => Task.FromResult(Ok(Release())));
        using var client = new HttpClient(handler);
        var settings = new AppSettings();
        var checker = new LibraryUpdateChecker(client, settings);
        await checker.CheckAsync([App()], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        await checker.CheckAsync([App()], false, TimeSpan.FromHours(24), null, TestContext.Current.CancellationToken);
        handler.Calls.Should().Be(1);
        settings.GitHubApiToken = "different-credential";
        await checker.CheckAsync([App()], false, TimeSpan.FromHours(24), null, TestContext.Current.CancellationToken);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Manual_checks_send_validators_and_reuse_304_payloads()
    {
        var count = 0;
        var handler = new Handler((r, _) =>
        {
            if (++count == 1) return Task.FromResult(Ok(Release()));
            r.Headers.IfNoneMatch.Should().ContainSingle();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });
        using var client = new HttpClient(handler);
        var checker = new LibraryUpdateChecker(client, new());
        await checker.CheckAsync([App()], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        var app = App();
        (await checker.CheckAsync([app], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken)).Complete.Should().BeTrue();
        app.LatestVersion.Should().Be("2.0"); count.Should().Be(2);
    }

    [Theory]
    [InlineData(503, AppCheckOutcome.Failed)]
    [InlineData(429, AppCheckOutcome.RateLimited)]
    [InlineData(401, AppCheckOutcome.Failed)]
    [InlineData(403, AppCheckOutcome.Failed)]
    [InlineData(404, AppCheckOutcome.Failed)]
    public async Task Failures_keep_known_updates_and_do_not_claim_success(int code, AppCheckOutcome expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)));
        using var client = new HttpClient(handler);
        var app = App(); app.LatestVersion = "2.0"; app.Status = GameStatus.UpdateAvailable;
        var result = await new LibraryUpdateChecker(client, new()).CheckAsync([app], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        result.Apps.Single().Outcome.Should().Be(expected);
        result.Apps.Single().AppName.Should().Be(app.DisplayName);
        result.Apps.Single().Reason.Should().NotBeNullOrWhiteSpace();
        result.Apps.Single().Reason.Should().Contain(code == 429 ? "rate limit" : $"HTTP {code}");
        app.HasRepositoryCheckError.Should().BeTrue();
        app.RepositoryCheckError.Should().Be(result.Apps.Single().Reason);
        app.LatestVersion.Should().Be("2.0"); app.Status.Should().Be(GameStatus.UpdateAvailable);
    }

    [Fact]
    public async Task Gitlab_uses_its_existing_asset_mapping_and_manual_apps_make_no_requests()
    {
        var handler = new Handler((r, _) =>
        {
            r.RequestUri!.Host.Should().Be("gitlab.com");
            return Task.FromResult(Ok("""[{"tag_name":"2.0","assets":{"links":[{"name":"app.zip","url":"https://gitlab.com/fixture/app/app.zip"}]}}]"""));
        });
        using var client = new HttpClient(handler);
        var app = App(); app.RepositorySource = "gitlab";
        var manual = App(); manual.Repository = "";
        var result = await new LibraryUpdateChecker(client, new()).CheckAsync([app, manual], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        result.Successful.Should().Be(1); app.LatestVersion.Should().Be("2.0"); handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Timeout_releases_provider_slot_and_cancel_stops_remaining_requests()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (r, ct) =>
        {
            if (r.RequestUri!.AbsolutePath.Contains("stall")) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            return Ok(Release());
        });
        using var client = new HttpClient(handler);
        ReleaseRequestCoordinator.For(client).MetadataTimeout = TimeSpan.FromMilliseconds(80);
        var checker = new LibraryUpdateChecker(client, new());
        var results = await checker.CheckAsync([App("fixture/stall"), App("fixture/ok")], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        results.Failed.Should().Be(1); results.Successful.Should().Be(1);
        results.Apps[0].Reason.Should().Contain("too long");
        using var cancel = new CancellationTokenSource();
        var running = checker.CheckAsync([App("fixture/stall"), App("fixture/never")], true, TimeSpan.Zero, null, cancel.Token);
        cancel.Cancel();
        (await running).Cancelled.Should().Be(2);
    }

    [Fact]
    public async Task Empty_releases_explain_failure_and_snapshot_target_names()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Ok("[]"))));
        var app = App();
        app.RepositorySource = "gitlab";
        app.CustomDisplayName = "My renamed app";
        AppCheckProgress? initial = null;
        var result = await new LibraryUpdateChecker(client, new()).CheckAsync([app], true, TimeSpan.Zero,
            new CaptureProgress(value => initial ??= value), TestContext.Current.CancellationToken);
        result.Apps.Single().Reason.Should().Be("No eligible release was found.");
        result.Apps.Single().AppName.Should().Be("My renamed app");
        initial!.Targets!.Single().AppName.Should().Be("My renamed app");
    }

    private sealed class CaptureProgress(Action<AppCheckProgress> report) : IProgress<AppCheckProgress>
    {
        public void Report(AppCheckProgress value) => report(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uninstalled_app_warning_clears_after_successful_check(bool directCheck)
    {
        var fail = true;
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(fail
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Ok("""[{"tag_name":"2.0","assets":{"links":[{"name":"app.zip","url":"https://gitlab.com/fixture/app/app.zip"}]}}]"""))));
        var app = App("fixture/" + Guid.NewGuid().ToString("N"));
        app.RepositorySource = "gitlab";
        app.Status = GameStatus.NotInstalled;
        app.InstalledVersion = null;
        async Task Check()
        {
            if (directCheck) await app.CheckLatestVersionAsync(client, forceCheck: true);
            else await new LibraryUpdateChecker(client, new()).CheckAsync([app], true, TimeSpan.Zero, null,
                TestContext.Current.CancellationToken);
        }
        await Check();
        app.HasRepositoryCheckError.Should().BeTrue();
        app.RepositoryCheckError.Should().Contain("404");
        app.Status.Should().Be(GameStatus.NotInstalled);
        // Restoring old cached metadata is not evidence that the failed check recovered.
        app.ApplyCachedRelease("1.0", null);
        app.HasRepositoryCheckError.Should().BeTrue();
        fail = false;
        await Check();
        app.HasRepositoryCheckError.Should().BeFalse();
        app.LatestVersion.Should().Be("2.0");
        app.Status.Should().Be(GameStatus.NotInstalled);
    }

    [Fact]
    public async Task Cancelled_check_does_not_flag_an_unchecked_app()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No request expected")));
        var app = App();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await new LibraryUpdateChecker(client, new()).CheckAsync([app], true, TimeSpan.Zero, null, cancellation.Token);
        app.HasRepositoryCheckError.Should().BeFalse();
    }
}
