using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class TelemetryTests
{
    private sealed class PostHog
    {
        public List<JsonObject> Batches { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public List<string> Urls { get; } = [];

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Urls.Add(request.RequestUri!.ToString());
            Batches.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
            return new HttpResponseMessage(Status);
        }

        public IEnumerable<JsonObject> Events => Batches.SelectMany(b => b["batch"]!.AsArray()).Select(e => e!.AsObject());
    }

    private static (Telemetry Client, PostHog Server) Create(string? token = "phc_project", string host = "https://ph.example")
    {
        var server = new PostHog();
        return (new Telemetry(token, host, server.SendAsync), server);
    }

    [Fact]
    public async Task Sends_nothing_until_the_player_says_yes()
    {
        var (client, server) = Create();
        client.Available.Should().BeTrue();
        client.Track("app_launched");
        client.Configure(enabled: false, installId: "install-1");
        client.Track("app_launched");
        await client.FlushAsync(TestContext.Current.CancellationToken);
        server.Batches.Should().BeEmpty();
        client.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Events_go_to_the_batch_endpoint_as_this_install_and_say_which_launcher_sent_them()
    {
        var (client, server) = Create();
        client.Configure(enabled: true, installId: "install-1");
        client.Track("app_launched", new Dictionary<string, object?> { ["slug"] = "zelda64recomp", ["source"] = "catalog" });
        await client.FlushAsync(TestContext.Current.CancellationToken);

        server.Urls.Should().Equal("https://ph.example/batch/");
        server.Batches.Single()["api_key"]!.GetValue<string>().Should().Be("phc_project");
        var sent = server.Events.Single();
        sent["event"]!.GetValue<string>().Should().Be("app_launched");
        var properties = sent["properties"]!.AsObject();
        properties["distinct_id"]!.GetValue<string>().Should().Be("install-1");
        properties["app"]!.GetValue<string>().Should().Be(Telemetry.AppName);
        properties["$process_person_profile"]!.GetValue<bool>().Should().BeFalse();
        properties["slug"]!.GetValue<string>().Should().Be("zelda64recomp");
        properties.ContainsKey("launcher_version").Should().BeTrue();
        properties.ContainsKey("os").Should().BeTrue();
    }

    [Fact]
    public async Task Turning_it_off_drops_what_was_waiting()
    {
        var (client, server) = Create();
        client.Configure(enabled: true, installId: "install-1");
        client.Track("app_launched");
        client.Configure(enabled: false, installId: null);
        client.Configure(enabled: true, installId: "install-2");
        await client.FlushAsync(TestContext.Current.CancellationToken);
        server.Batches.Should().BeEmpty();
    }

    [Fact]
    public async Task Events_wait_for_the_next_try_when_PostHog_is_unreachable()
    {
        var (client, server) = Create();
        client.Configure(enabled: true, installId: "install-1");
        client.Track("app_launched");
        server.Status = HttpStatusCode.ServiceUnavailable;
        await client.FlushAsync(TestContext.Current.CancellationToken);
        server.Status = HttpStatusCode.OK;
        await client.FlushAsync(TestContext.Current.CancellationToken);
        server.Batches.Should().HaveCount(2);
        server.Batches[1]["batch"]!.AsArray().Should().HaveCount(1);
    }

    [Fact]
    public async Task The_start_is_sent_once_and_only_after_saying_yes()
    {
        var (client, server) = Create();
        client.TrackStartup(new Dictionary<string, object?> { ["first_run"] = true });
        client.Configure(enabled: true, installId: "install-1");
        client.Configure(enabled: true, installId: "install-1");
        await client.FlushAsync(TestContext.Current.CancellationToken);
        server.Events.Select(e => e["event"]!.GetValue<string>()).Should().Equal("launcher_started");
    }

    [Fact]
    public void A_test_token_never_reaches_PostHog_itself()
    {
        new Telemetry("phc_test_123", "https://us.i.posthog.com").Available.Should().BeFalse();
        new Telemetry("phc_test_123", "http://127.0.0.1:9").Available.Should().BeTrue();
        new Telemetry(null).Available.Should().BeFalse();
        new Telemetry("  ").Available.Should().BeFalse();
    }

    [Theory]
    [InlineData(@"Could not find C:\Users\Someone\AppData\Local\QuiverLauncher\Apps\Game\game.exe", "Could not find <path>")]
    [InlineData("Access to the path '/home/someone/Games/x.zip' is denied.", "Access to the path '<path>' is denied.")]
    [InlineData(@"Missing \\server\share\file.txt now", "Missing <path> now")]
    [InlineData("file:///C:/Users/x/y.json failed", "<path> failed")]
    [InlineData("Response status code does not indicate success: 404 (Not Found).", "Response status code does not indicate success: 404 (Not Found).")]
    [InlineData("See https://quiverlauncher.com/apps/x for help", "See https://quiverlauncher.com/apps/x for help")]
    public void Paths_never_leave(string text, string expected)
    {
        var (client, _) = Create();
        client.Scrub(text).Should().Be(expected);
    }

    [Fact]
    public void The_apps_folder_and_the_computers_user_name_never_leave()
    {
        var (client, _) = Create();
        client.SetFolders("QuiverAppsFolderXYZ", null);
        client.Scrub("Installed to QuiverAppsFolderXYZ").Should().Be("Installed to <path>");
        var user = Environment.UserName;
        if (user.Length >= 2)
            client.Scrub($"Signed in as {user}.").Should().Be("Signed in as <user>.");
    }

    [Fact]
    public async Task Errors_go_to_error_tracking_with_their_frames_and_without_folders()
    {
        var (client, server) = Create();
        client.Configure(enabled: true, installId: "install-1");
        Exception error;
        try { throw new InvalidOperationException("Couldn't open /home/someone/apps.json", new IOException("disk")); }
        catch (Exception ex) { error = ex; }
        client.CaptureException(error, handled: true, "app_install");
        client.CaptureException(error, handled: true, "app_install");
        await client.FlushAsync(TestContext.Current.CancellationToken);

        var sent = server.Events.Single();
        sent["event"]!.GetValue<string>().Should().Be("$exception");
        var properties = sent["properties"]!.AsObject();
        properties["source"]!.GetValue<string>().Should().Be("app_install");
        var list = properties["$exception_list"]!.AsArray();
        list.Should().HaveCount(2);
        list[0]!["type"]!.GetValue<string>().Should().Be("System.InvalidOperationException");
        list[0]!["value"]!.GetValue<string>().Should().Be("Couldn't open <path>");
        list[0]!["mechanism"]!["handled"]!.GetValue<bool>().Should().BeTrue();
        list[1]!["type"]!.GetValue<string>().Should().Be("System.IO.IOException");
        var frame = list[0]!["stacktrace"]!["frames"]!.AsArray().Last()!;
        frame["platform"]!.GetValue<string>().Should().Be("custom");
        frame["function"]!.GetValue<string>().Should().Contain(nameof(Errors_go_to_error_tracking_with_their_frames_and_without_folders));
        frame["in_app"]!.GetValue<bool>().Should().BeTrue();
        if (frame["filename"] is { } file)
            file.GetValue<string>().Should().Be("TelemetryTests.cs");
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; private set; } = new() { FirstStartup = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }

    [Fact]
    public void The_answer_is_saved_and_a_new_install_id_comes_with_each_yes()
    {
        var store = new Store();
        var model = new SettingsViewModel(store);
        model.SetUsageData(false, "prompt");
        store.Current.UsageDataAsked.Should().BeTrue();
        store.Current.UsageDataEnabled.Should().BeFalse();
        store.Current.UsageDataInstallId.Should().BeEmpty();

        model.UsageDataEnabled = true;
        var first = store.Current.UsageDataInstallId;
        first.Should().NotBeEmpty();
        model.UsageDataEnabled = false;
        store.Current.UsageDataInstallId.Should().BeEmpty();
        model.UsageDataEnabled = true;
        store.Current.UsageDataInstallId.Should().NotBeEmpty().And.NotBe(first);
    }
}
