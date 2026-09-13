using System.Net;
using System.Text.Json;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class PublishedPlatformTests : IDisposable
{
    private const string Url = "https://example.test/platform-index.json";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-public-platform-" + Guid.NewGuid());
    private readonly string? _previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
    public PublishedPlatformTests()
    {
        Directory.CreateDirectory(_root);
        QuiverLauncherPaths.OverrideUserDataRoot = _root;
        CatalogPlatformIndex.Initialize(_root);
    }
    public void Dispose() { QuiverLauncherPaths.OverrideUserDataRoot = _previousRoot; TestFixtures.CleanupDirectory(_root); }
    private static PublishedPlatformRecord Entry(string repo = "owner/app", string[]? names = null, string? pin = null, int ageHours = 0) =>
        new("github", repo, pin, "v1", names ?? ["game-windows.zip"], DateTimeOffset.UtcNow.AddHours(-ageHours));
    private static string Document(params PublishedPlatformRecord[] entries) => JsonSerializer.Serialize(
        new PublishedPlatformDocument { GeneratedAt = DateTimeOffset.UtcNow, Entries = entries.ToList() }, PublishedPlatformDocument.JsonOptions);
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class ReviewService(List<GameInfo> apps, HttpClient client, bool cachedInitially = false) : ICatalogReviewService
    {
        private bool _fetched = cachedInitially;
        public Task<List<GameInfo>> LoadLocalAsync() => Task.FromResult(new List<GameInfo>());
        public Task<List<GameInfo>> LoadCachedAsync(string id) => Task.FromResult(_fetched ? apps : []);
        public Task FetchAsync(AppCatalogSource source) { _fetched = true; return Task.CompletedTask; }
        public Task RefreshPlatformMetadataAsync(AppCatalogSource source, bool force, CancellationToken token) => PublishedPlatformCache.RefreshAsync(client, source.PlatformMetadataUrl, force, token);
        public Task SaveLocalAsync(List<GameInfo> apps, AppCatalogSource source, CancellationToken token) => Task.CompletedTask;
        public void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external) { }
        public void Acknowledge(AppCatalogSource source) { }
    }
    private FileSettingsStore Store()
    {
        var store = new FileSettingsStore(Path.Combine(_root, "settings.json"));
        store.Current.AppsPath = Path.Combine(_root, "Apps");
        store.Current.CatalogPlatformFilters = ["Windows"];
        store.Save(store.Current);
        return store;
    }

    [Fact]
    public async Task Four_unindexed_apps_appear_before_shared_refresh_and_checks_update_in_place()
    {
        var indexStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var indexReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checksStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checksReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsoluteUri == Url)
            {
                indexStarted.TrySetResult();
                await indexReady.Task.WaitAsync(token);
                return Ok(Document());
            }
            requests.Enqueue(request.RequestUri.AbsolutePath);
            checksStarted.TrySetResult();
            await checksReady.Task.WaitAsync(token);
            var asset = request.RequestUri.AbsolutePath.Contains("new3") ? "app-Linux.AppImage" : "app-Windows.zip";
            return Ok(JsonSerializer.Serialize(new GitHubRelease { tag_name = "v1", assets = [new() { name = asset }] }));
        }));
        var store = Store();
        using var manager = new GameManager(store, client);
        var settings = new SettingsViewModel(store);
        var model = new CatalogSyncViewModel { SettingsModel = settings, PlatformFilters = ["Windows"] };
        var apps = Enumerable.Range(0, 4).Select(i => new GameInfo { Name = "New " + i, Repository = "new-tests/new" + i, FolderName = "New" + i }).ToList();
        using var workspace = new CatalogReviewWorkspace(model, settings, new ReviewService(apps, client),
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        using var queue = new CatalogReleasePrefetch(session, manager, settings, model, workspace, () => true,
            a => { a(); return Task.CompletedTask; }, model.DeferPlatformDiscoveries);
        var rowsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.RowsChanged += () => { model.Rows.UpdateWith(model.GetFilteredRows().ToList()); rowsReady.TrySetResult(); queue.Start(); };
        var opening = workspace.OpenAsync(new() { IsCommunityManaged = true, PlatformMetadataUrl = Url, CachedListVersion = "1" }, CatalogReviewFilter.NeedsReview, session.Token);
        try
        {
            await indexStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await rowsReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(4, model.Rows.Count);
            Assert.Empty(requests);
            Assert.False(model.ShowHiddenPendingReviews);
            Assert.Empty(model.GetFilteredBulkAddRows());
            Assert.All(model.Rows, row => { Assert.True(row.CanAdd); Assert.Equal("Checking compatibility…", row.CompatibilityText); });
            var original = model.Rows.ToArray();
            original[0].IsGamepadFocused = true;
            var mutations = 0;
            model.Rows.CollectionChanged += (_, _) => mutations++;
            indexReady.TrySetResult();
            await opening.WaitAsync(TimeSpan.FromSeconds(3));
            await checksStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            checksReady.TrySetResult();
            await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, requests.Count);
            Assert.Equal(original, model.Rows);
            Assert.Equal(0, mutations);
            Assert.True(original[0].IsGamepadFocused);
            Assert.Equal("Not available for Windows", original[3].CompatibilityText);
            Assert.Equal(3, model.GetFilteredBulkAddRows().Count);
            Assert.False(model.ShowPlatformCheck);
            Assert.False(model.ShowHiddenPendingReviews);
            model.AcceptPlatformDiscoveries();
            Assert.Equal(3, model.GetFilteredRows().Count());
        }
        finally { indexReady.TrySetResult(); checksReady.TrySetResult(); await opening; }
    }

    [Theory]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(200)]
    public async Task Failed_automatic_checks_keep_apps_visible_and_unverified(int status)
    {
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri == Url
            ? Ok(Document()) : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("invalid release response") })));
        var store = Store();
        using var manager = new GameManager(store, client);
        var settings = new SettingsViewModel(store);
        var model = new CatalogSyncViewModel { SettingsModel = settings };
        using var workspace = new CatalogReviewWorkspace(model, settings,
            new ReviewService([new() { Name = "New", Repository = "failure/new", FolderName = "New" }], client),
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        using var queue = new CatalogReleasePrefetch(session, manager, settings, model, workspace, () => true,
            a => { a(); return Task.CompletedTask; }, model.DeferPlatformDiscoveries);
        var source = new AppCatalogSource { IsCommunityManaged = true, PlatformMetadataUrl = Url, CachedListVersion = "1" };
        await workspace.OpenAsync(source, CatalogReviewFilter.NeedsReview, session.Token);
        if (status == 429)
        {
            for (var i = 0; i < 100 && !model.ShowPlatformFailure; i++) await Task.Delay(10);
            Assert.True(model.ShowPlatformFailure);
        }
        else await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var row = Assert.Single(model.GetFilteredRows());
        Assert.Equal("Compatibility unverified", row.CompatibilityText);
        Assert.True(row.CanAdd);
        Assert.Empty(model.GetFilteredBulkAddRows());
        Assert.True(model.ShowPlatformCheck);
        Assert.False(model.ShowHiddenPendingReviews);
        Assert.Null(source.AcknowledgedListVersion);
    }

    [Fact]
    public async Task Community_missing_checks_reuse_cached_api_evidence_without_requests()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(Url, request.RequestUri!.AbsoluteUri);
            calls++;
            return Task.FromResult(Ok(Document()));
        }));
        var store = Store();
        using var manager = new GameManager(store, client);
        CatalogPlatformIndex.Set("github", "cached/app", null, null, new() { tag_name = "v1", assets = [new() { name = "app-Windows.zip" }] });
        var settings = new SettingsViewModel(store);
        var model = new CatalogSyncViewModel { SettingsModel = settings };
        using var workspace = new CatalogReviewWorkspace(model, settings,
            new ReviewService([new() { Name = "Cached", Repository = "cached/app", FolderName = "Cached" }], client),
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        using var queue = new CatalogReleasePrefetch(session, manager, settings, model, workspace, () => true,
            a => { a(); return Task.CompletedTask; }, model.DeferPlatformDiscoveries);
        await workspace.OpenAsync(new() { IsCommunityManaged = true, PlatformMetadataUrl = Url }, CatalogReviewFilter.All, session.Token);
        await queue.Completion;
        Assert.Equal(1, calls);
        Assert.Single(model.GetFilteredBulkAddRows());
        Assert.False(model.ShowPlatformCheck);
    }

    [Theory]
    [InlineData(62)]
    [InlineData(150)]
    public async Task Fresh_profile_populates_once_with_zero_repository_requests(int count)
    {
        var requests = new List<string>();
        var payload = Document(Enumerable.Range(0, count).Select(i => Entry("owner/app" + i)).ToArray());
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(Url, request.RequestUri.AbsoluteUri);
            return Task.FromResult(Ok(payload));
        }));
        var store = Store();
        using var manager = new GameManager(store, client);
        var model = new CatalogSyncViewModel();
        var settings = new SettingsViewModel(store);
        model.SettingsModel = settings;
        var apps = Enumerable.Range(0, count).Select(i => new GameInfo { Name = "Game " + i, Repository = "owner/app" + i, FolderName = "app" + i }).ToList();
        using var workspace = new CatalogReviewWorkspace(model, settings, new ReviewService(apps, client), (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        using var queue = new CatalogReleasePrefetch(session, manager, settings, model, workspace, () => true,
            a => { a(); return Task.CompletedTask; }, model.DeferPlatformDiscoveries);
        var populations = 0;
        // The real review view initializes its filter controls before cached rows arrive.
        workspace.SourceOpened += _ => model.AcceptPlatformDiscoveries();
        workspace.RowsChanged += () => { populations++; model.Rows.UpdateWith(model.GetFilteredRows().ToList()); queue.Start(); };
        var source = new AppCatalogSource { IsCommunityManaged = true, PlatformMetadataUrl = Url };
        await workspace.OpenAsync(source, CatalogReviewFilter.All, session.Token);
        await queue.Completion;
        Assert.Equal(count, model.Rows.Count);
        Assert.Equal(1, populations);
        Assert.Single(requests);
        Assert.Equal(0, model.UnresolvedPlatformTargets);
        Assert.False(model.ShowPlatformRetry);
        settings.SaveApiToken("github", "synthetic-context");
        await queue.Completion;
        Assert.Equal(count, model.GetFilteredRows().Count());
        Assert.Single(requests);
        queue.RefreshAll();
        await session.DisposeAsync();
        Assert.All(requests, request => Assert.Equal(Url, request));
    }

    [Fact]
    public async Task Conditional_cache_is_throttled_persistent_and_keeps_stale_payload_after_304_or_failure()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var response = Ok(Document(Entry(ageHours: 30)));
                response.Headers.ETag = new("\"snapshot-1\"");
                return Task.FromResult(response);
            }
            Assert.Equal("\"snapshot-1\"", request.Headers.IfNoneMatch.Single().ToString());
            return Task.FromResult(calls == 2 ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok("{invalid"));
        }));
        await PublishedPlatformCache.RefreshAsync(client, Url);
        await PublishedPlatformCache.RefreshAsync(client, Url);
        Assert.Equal(1, calls);
        CatalogPlatformIndex.Initialize(_root); // Reload a real isolated cache, including its validator and stale evidence.
        Assert.True(CatalogPlatformIndex.TryGet("github", "OWNER/APP", null, "new-context", out var entry));
        Assert.False(CatalogPlatformIndex.IsFresh("github", "owner/app"));
        await PublishedPlatformCache.RefreshAsync(client, Url);
        Assert.Equal(2, calls);
        Assert.True(CatalogPlatformIndex.TryGet("github", "owner/app", null, null, out var after304));
        Assert.Equal(entry, after304);
        await PublishedPlatformCache.RefreshAsync(client, Url, true);
        Assert.NotNull(PublishedPlatformCache.Error(Url));
        Assert.True(CatalogPlatformSupport.AppMatches("github", "owner/app", null, ["Windows"]));
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("{\"formatRevision\":999,\"generatedAt\":\"2026-01-01T00:00:00Z\",\"entries\":[]}")]
    [InlineData("{\"formatRevision\":1,\"generatedAt\":\"2026-01-01T00:00:00Z\",\"entries\":null}")]
    public async Task Malformed_or_unknown_revision_cannot_replace_success(string invalid)
    {
        var body = Document(Entry());
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(body))));
        await PublishedPlatformCache.RefreshAsync(client, Url);
        body = invalid;
        await PublishedPlatformCache.RefreshAsync(client, Url, true);
        Assert.True(CatalogPlatformSupport.AppMatches("github", "owner/app", null, ["Windows"]));
        Assert.NotNull(PublishedPlatformCache.Error(Url));
    }

    [Fact]
    public async Task Public_coverage_pins_filters_empty_releases_and_private_contexts_remain_distinct()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Document(
            Entry(), Entry(pin: "beta", names: ["android.apk"]), Entry("owner/empty", []),
            Entry("owner/ios", ["Nautilus-Alfa-iOS.zip", "game-windows.zip.sha256"]))))));
        await PublishedPlatformCache.RefreshAsync(client, Url);
        Assert.True(CatalogPlatformSupport.AppMatches("github", "OWNER/APP", null, ["Windows"], token: "context-a"));
        Assert.False(CatalogPlatformSupport.AppMatches("github", "owner/app", null, ["Windows"], "beta"));
        Assert.True(CatalogPlatformSupport.AppMatches("github", "owner/app", null, ["Android"], "beta"));
        Assert.False(CatalogPlatformSupport.AppMatches("github", "owner/app", "linux", ["Windows"]));
        Assert.True(CatalogPlatformIndex.IsFresh("github", "owner/empty"));
        Assert.False(CatalogPlatformSupport.AppMatches("github", "owner/empty", null, ["Windows"]));
        Assert.False(CatalogPlatformSupport.AppMatches("github", "owner/ios", null, ["Windows"]));
        CatalogPlatformIndex.Set("github", "private/app", null, "context-a", new() { tag_name = "private", assets = [new() { name = "private-windows.zip" }] });
        Assert.False(CatalogPlatformIndex.TryGet("github", "private/app", null, "context-b", out _));
        Assert.False(CatalogPlatformIndex.TryGet("github", "private/app", null, null, out _));
        CatalogPlatformIndex.Set("github", "public/live", null, null, new() { assets = [new() { name = "windows.zip" }] });
        Assert.True(CatalogPlatformIndex.TryGet("github", "public/live", null, "context-b", out _));
    }

    [Fact]
    public async Task Publication_keeps_failed_entry_replaces_successful_empty_pin_and_uses_shared_assetless_fallback()
    {
        var previous = PublishedPlatformDocument.Parse(Document(Entry("owner/fail"), Entry("owner/empty", pin: "v1")));
        var old = new GitHubRelease { tag_name = "v1", assets = [new() { name = "SF-win64.zip" }] };
        var latest = new GitHubRelease { tag_name = "0.2.0-beta.2", assets = [new() { name = "windows.zip.sha256" }] };
        using var client = new HttpClient(new Handler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("fail")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (path.Contains("empty")) return Task.FromResult(Ok(JsonSerializer.Serialize(path.EndsWith("latest") ? (object)new GitHubRelease { tag_name = "v1" } : new[] { new GitHubRelease { tag_name = "v1" } })));
            return Task.FromResult(Ok(JsonSerializer.Serialize(path.EndsWith("latest") ? (object)latest : new[] { latest, old })));
        }));
        var generated = await PlatformMetadataPublisher.GenerateAsync(client,
            [new("github", "owner/fail"), new("github", "owner/empty", "v1"), new("github", "owner/sf")], previous, _ => null);
        Assert.Equal(2, generated.Successful);
        Assert.Single(generated.Failures);
        Assert.Equal(previous.Entries[0], generated.Document.Entries.Single(e => e.Repository == "owner/fail"));
        Assert.Empty(generated.Document.Entries.Single(e => e.Repository == "owner/empty").AssetNames);
        Assert.Equal("v1", generated.Document.Entries.Single(e => e.Repository == "owner/sf").ReleaseTag);
        var file = Path.Combine(_root, "publication.json");
        generated.Document.WriteAtomically(file);
        Assert.Equal(3, PublishedPlatformDocument.Parse(File.ReadAllText(file)).Entries.Count);
        var good = File.ReadAllText(file);
        generated.Document.FormatRevision = 200;
        Assert.Throws<JsonException>(() => generated.Document.WriteAtomically(file));
        Assert.Equal(good, File.ReadAllText(file));
    }

    [Fact]
    public async Task Unknown_apps_are_visible_and_bulk_eligibility_updates_without_apply()
    {
        var model = new CatalogSyncViewModel { PlatformFilters = ["Windows"] };
        var source = new AppCatalogSource { CachedListVersion = "1" };
        model.Refresh(source, [], [new() { Name = "DK64", Repository = "owner/dk", FolderName = "dk" }]);
        model.AcceptPlatformDiscoveries();
        Assert.Single(model.GetFilteredRows());
        Assert.Empty(model.GetFilteredBulkAddRows());
        CatalogPlatformIndex.Set("github", "owner/dk", null, null, new() { tag_name = "1.0.2", assets = [new() { name = "DK64Recompiled-Windows-Release-1-0-2.zip" }] });
        model.DeferPlatformDiscoveries();
        Assert.Single(model.GetFilteredRows());
        Assert.Equal(0, model.MoreAppsAvailable);
        Assert.Single(model.GetFilteredBulkAddRows());
        Assert.True(model.HasPlatformDiscoveries);
        model.AcceptPlatformDiscoveries();
        Assert.Single(model.GetFilteredRows());
        Assert.Single(model.GetFilteredBulkAddRows());
        CatalogCompareService.HideFromReview(source, model.AllRows[0].ReviewKey);
        model.Refresh(source, [], [new() { Name = "DK64", Repository = "owner/dk", FolderName = "dk" }]);
        Assert.Empty(model.GetFilteredRows());
        await Task.CompletedTask;
    }

    private sealed class RegistryReader : ICatalogLocationReader
    {
        public Task<string> ReadAsync(HttpClient client, string location, CancellationToken cancellationToken = default) =>
            Task.FromResult(location == CommunityCatalogDefaults.RemoteIndexUrl
                ? JsonSerializer.Serialize(new { version = 2, platformMetadataUrl = Url, lists = new[] { new { id = "test", remoteLocation = "https://example.test/catalog.json" } } })
                : "{\"version\":\"1\",\"name\":\"Test\",\"apps\":[]}");
    }
    [Fact]
    public async Task Refresh_all_sources_bypasses_shared_throttle_but_normal_startup_reuses_it()
    {
        var requests = 0;
        using var client = new HttpClient(new Handler((_, _) => { requests++; return Task.FromResult(Ok(Document(Entry()))); }));
        var service = new AppCatalogService(locationReader: new RegistryReader(), dataDirectory: _root);
        var settings = new AppSettings();
        await service.EnsureCommunitySourcesCachedAsync(client, settings);
        await service.EnsureCommunitySourcesCachedAsync(client, settings);
        Assert.Equal(1, requests);
        Assert.Equal(Url, settings.AppCatalogSources.Single().PlatformMetadataUrl);
        await service.RefreshAllSourcesAsync(client, settings);
        Assert.Equal(2, requests);
        await service.RefreshAllSourcesAsync(client, settings);
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task Missing_community_checks_start_automatically_and_saving_token_resumes_while_review_is_closed()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri == Url) return Task.FromResult(Ok(Document()));
            calls++;
            return Task.FromResult(request.Headers.Authorization == null
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : Ok(JsonSerializer.Serialize(new GitHubRelease { tag_name = "v1", assets = [new() { name = "windows.zip" }] })));
        }));
        var store = Store();
        using var manager = new GameManager(store, client);
        var settings = new SettingsViewModel(store);
        var model = new CatalogSyncViewModel { SettingsModel = settings };
        using var workspace = new CatalogReviewWorkspace(model, settings,
            new ReviewService([new() { Name = "Missing", Repository = "owner/missing", FolderName = "missing" }], client),
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        using var queue = new CatalogReleasePrefetch(session, manager, settings, model, workspace, () => workspace.ActiveSource != null,
            a => { a(); return Task.CompletedTask; }, model.DeferPlatformDiscoveries);
        var source = new AppCatalogSource { IsCommunityManaged = true, PlatformMetadataUrl = Url };
        await workspace.OpenAsync(source, CatalogReviewFilter.All, session.Token);
        queue.Start();
        Assert.Equal("Retry", model.PlatformRetryText);
        Assert.Equal(1, model.UnresolvedPlatformTargets);
        for (var i = 0; i < 100 && !model.ShowPlatformFailure; i++) await Task.Delay(10);
        Assert.Equal(1, calls);
        Assert.True(model.ShowPlatformTokenShortcut);
        queue.Cancel(); workspace.Close();
        settings.SaveApiToken("github", " synthetic-context ");
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.True(CatalogPlatformIndex.TryGet("github", "owner/missing", null, "synthetic-context", out _));
        await workspace.OpenAsync(source, CatalogReviewFilter.All, session.Token);
        queue.Start();
        Assert.Single(model.GetFilteredRows());
        Assert.Equal("synthetic-context", new FileSettingsStore(Path.Combine(_root, "settings.json")).Current.GitHubApiToken);
    }

    [Fact]
    public async Task Session_queue_survives_navigation_and_prioritizes_newly_opened_catalog()
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paths = new List<string>();
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (paths.Count == 1) { sent.SetResult(); await finish.Task.WaitAsync(token); }
            return Ok(JsonSerializer.Serialize(new GitHubRelease { tag_name = "v1", assets = [new() { name = "windows.zip" }] }));
        }));
        var store = Store();
        using var manager = new GameManager(store, client);
        var settings = new SettingsViewModel(store);
        var model = new CatalogSyncViewModel();
        var service = new ReviewService([], client);
        using var workspace = new CatalogReviewWorkspace(model, settings, service, (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        using var queue = new CatalogReleasePrefetch(session, manager, settings, model, workspace, () => true,
            a => { a(); return Task.CompletedTask; }, model.DeferPlatformDiscoveries);
        var first = new AppCatalogSource { Id = "first" };
        await workspace.OpenAsync(first, CatalogReviewFilter.All, session.Token);
        model.Refresh(first, [], Enumerable.Range(0, 3).Select(i => new GameInfo { Name = "A" + i, Repository = "owner/a" + i, FolderName = "a" + i }).ToList());
        queue.Start();
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Cancel(); workspace.Close();
        var second = new AppCatalogSource { Id = "second" };
        await workspace.OpenAsync(second, CatalogReviewFilter.All, session.Token);
        model.Refresh(second, [], [new() { Name = "B", Repository = "owner/b", FolderName = "b" }]);
        queue.Start(); finish.SetResult();
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "/repos/owner/a0/releases/latest", "/repos/owner/b/releases/latest", "/repos/owner/a1/releases/latest", "/repos/owner/a2/releases/latest" }, paths);
    }
}
