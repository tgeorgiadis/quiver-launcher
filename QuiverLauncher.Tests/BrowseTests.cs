using System.Net;
using System.Text.Json;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class BrowseTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuiverLauncher.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>Answers /facets itself and hands every /apps request to the test.</summary>
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> apps) : HttpMessageHandler
    {
        public List<string> AppQueries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/facets", StringComparison.Ordinal))
                return Task.FromResult(Json("""{"total":0,"consoles":[]}"""));
            lock (AppQueries) AppQueries.Add(request.RequestUri.Query);
            return apps(request);
        }
    }

    public void Dispose() => TestFixtures.CleanupDirectory(_directory);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    private static string Page(string? nextCursor, bool isDone, params string[] folders) => JsonSerializer.Serialize(new
    {
        items = folders.Select(folder => new { id = folder, slug = folder.ToLowerInvariant(), name = folder, launcher = new { folderName = folder } }),
        nextCursor,
        isDone,
    });

    private static BrowseViewModel Browse(Handler handler, IReadOnlyList<GameInfo>? library = null, Func<string>? listLocation = null,
        Func<string, Task<(List<GameInfo> Apps, string? Error)>>? loadList = null) =>
        new(new QuiverCatalogClient(new HttpClient(handler), "https://api.quiverlauncher.test/api/v1"), () => library ?? [],
            listLocation ?? (() => ""), loadList ?? (_ => Task.FromResult<(List<GameInfo> Apps, string? Error)>((new List<GameInfo>(), null))));

    [Theory]
    [InlineData(true, "https://example.com/my-apps.json")]
    [InlineData(false, "")]
    public void Settings_from_3_4_keep_the_players_own_list_and_stop_writing_catalog_sources(bool hasOwnList, string expectedList)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        var ownList = hasOwnList
            ? """, { "Id": "mine", "Name": "My apps", "Location": "/home/player/.local/share/QuiverLauncher/Cache/CatalogSources/mine.json", "RemoteLocation": "https://example.com/my-apps.json", "IsCommunityManaged": false, "Enabled": true }"""
            : "";
        File.WriteAllText(path, $$"""
            {
              "FirstStartup": false,
              "GitHubApiToken": "test-token",
              "SortBy": "Name",
              "PromptCatalogUpdates": true,
              "ShowLibraryAppUpdateBadges": false,
              "CatalogReviewSortBy": "Name",
              "CatalogReviewUseGridView": false,
              "CatalogPlatformFilters": [ "Linux", "Windows" ],
              "CatalogPlatformFilterChosen": true,
              "AppCatalogSources": [
                { "Id": "n64", "Name": "N64 Recomps", "Location": "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/lists/N64-Recomps.json", "IsCommunityManaged": true, "Enabled": true, "AcknowledgedListVersion": "12" },
                { "Id": "default", "Name": "Quiver Community Apps", "Location": "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/quiver-community-apps-catalog.json", "IsCommunityManaged": false, "Enabled": true, "IgnoredChangesAtVersion": { "owner/app": "3" } },
                { "Id": "old", "Name": "Old list", "Location": "/home/player/old-apps.json", "IsCommunityManaged": false, "Enabled": false } {{ownList}}
              ]
            }
            """);

        var store = new FileSettingsStore(path);
        store.Current.GitHubApiToken.Should().Be("test-token");
        store.Current.SortBy.Should().Be("Name");
        store.Current.CustomAppListLocation.Should().Be(expectedList);

        store.Save(store.Current);
        File.ReadAllText(path).Should().NotContain("AppCatalogSources");
        new FileSettingsStore(path).Current.CustomAppListLocation.Should().Be(expectedList);
    }

    [Fact]
    public async Task Reload_shows_first_page_and_load_more_follows_the_cursor_until_done()
    {
        var handler = new Handler(request => Task.FromResult(Json(request.RequestUri!.Query.Contains("cursor=c2")
            ? Page("c3", isDone: true, "Gamma")
            : Page("c2", isDone: false, "Alpha", "Beta"))));
        var browse = Browse(handler, library: [new GameInfo { FolderName = "beta" }]);
        browse.HideLibraryApps = false;
        var token = TestContext.Current.CancellationToken;

        await browse.ReloadAsync(token);
        browse.Items.Select(i => i.FolderName).Should().Equal("Alpha", "Beta");
        browse.Items.Select(i => i.InLibrary).Should().Equal(false, true);
        browse.CanLoadMore.Should().BeTrue();

        (await browse.LoadMoreAsync(token)).Should().BeTrue();
        handler.AppQueries[1].Should().Contain("cursor=c2");
        browse.Items.Select(i => i.FolderName).Should().Equal("Alpha", "Beta", "Gamma");
        browse.CanLoadMore.Should().BeFalse();
        (await browse.LoadMoreAsync(token)).Should().BeFalse();
        handler.AppQueries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Apps_already_in_the_library_are_hidden_and_counted_and_the_next_page_is_read_so_new_ones_show()
    {
        // The first page is all library apps: rather than show nothing, the next page is read.
        var handler = new Handler(request => Task.FromResult(Json(request.RequestUri!.Query.Contains("cursor=c2")
            ? Page(null, isDone: true, "Gamma", "Delta")
            : Page("c2", isDone: false, "Alpha", "Beta"))));
        var browse = Browse(handler, library: [new GameInfo { FolderName = "alpha" }, new GameInfo { FolderName = "beta" }]);
        var token = TestContext.Current.CancellationToken;

        await browse.ReloadAsync(token);
        browse.Items.Select(i => i.FolderName).Should().Equal("Gamma", "Delta");
        browse.HiddenInLibrary.Should().Be(2);
        browse.Items.Should().OnlyContain(i => i.CanAdd);

        // A library app in a folder of its own is still known by its catalog link.
        var renamed = Browse(handler, library: [new GameInfo { FolderName = "My Gamma", CatalogSlug = "gamma" }]);
        await renamed.ReloadAsync(token);
        renamed.Items.Select(i => i.FolderName).Should().Equal("Alpha", "Beta", "Delta");
        renamed.HiddenInLibrary.Should().Be(1);

        // Turned off, they show again, marked as in the library and with nothing to add.
        browse.HideLibraryApps = false;
        await browse.ReloadAsync(token);
        browse.Items.Select(i => i.FolderName).Should().Equal("Alpha", "Beta");
        browse.HiddenInLibrary.Should().Be(0);
        browse.Items.Should().OnlyContain(i => i.InLibrary && !i.CanAdd);
    }

    [Fact]
    public async Task A_library_app_not_in_the_catalog_shows_its_repositorys_readme_and_releases_on_its_page()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("An app not in the catalog never asks quiverlauncher.com."));
        var app = new GameInfo { Name = "Ship of Harkinian", Repository = "HarbourMasters/Shipwright", FolderName = "Shipwright" };
        var details = new BrowseDetailsViewModel(new QuiverCatalogClient(new HttpClient(handler), "https://api.quiverlauncher.test/api/v1"),
            (_, _) => throw new InvalidOperationException(), (_, _) => Task.FromResult(new DocumentContent("# Shipwright")),
            (_, _) => Task.FromResult<IReadOnlyList<QuiverCatalogRelease>>([new() { Version = "9.3.0", Notes = "Fixes.", State = RepositoryReleaseNotes.RepositoryState }]));
        var token = TestContext.Current.CancellationToken;

        await details.OpenAsync(BrowseItem.FromList(app), token);
        details.HasReleases.Should().BeTrue();
        await details.LoadReleasesAsync(token);

        details.Releases.Should().ContainSingle().Which.Version.Should().Be("9.3.0");
        details.ReleasesStatus.Should().BeEmpty();
        handler.AppQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task Unreachable_site_shows_a_status_instead_of_throwing()
    {
        var browse = Browse(new Handler(_ => Task.FromResult(
            Json("""{"error":{"message":"Down for maintenance."}}""", HttpStatusCode.ServiceUnavailable))));

        await browse.ReloadAsync(TestContext.Current.CancellationToken);

        browse.Status.Should().StartWith("Couldn't reach quiverlauncher.com").And.Contain("Down for maintenance.");
        browse.Items.Should().BeEmpty();
        browse.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Slower_earlier_reload_does_not_replace_newer_results()
    {
        var older = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var requested = new SemaphoreSlim(0);
        var browse = Browse(new Handler(request =>
        {
            requested.Release();
            return request.RequestUri!.Query.Contains("search=old") ? older.Task : newer.Task;
        }));
        var token = TestContext.Current.CancellationToken;

        browse.Search = "old";
        var first = browse.ReloadAsync(token);
        await requested.WaitAsync(token);
        browse.Search = "new";
        var second = browse.ReloadAsync(token);
        await requested.WaitAsync(token);

        newer.SetResult(Json(Page(null, isDone: true, "New")));
        await second;
        older.SetResult(Json(Page("old-2", isDone: false, "Old")));
        await first;

        browse.Items.Select(i => i.FolderName).Should().Equal("New");
        browse.CanLoadMore.Should().BeFalse();
        browse.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Own_list_is_sorted_by_name_filtered_by_search_and_reports_read_errors()
    {
        const string myList = "https://example.com/my-apps.json";
        List<GameInfo> apps =
        [
            new() { Name = "Zelda 64", Repository = "zelda/zelda64recomp", FolderName = "Zelda64Recomp" },
            new() { Name = "mario 64", Repository = "mario/sm64", FolderName = "SM64" },
            new() { Name = "Banjo", Repository = "banjo/banjo", FolderName = "Banjo" },
        ];
        var location = myList;
        var reads = new List<string>();
        var handler = new Handler(_ => throw new InvalidOperationException("The player's list never asks quiverlauncher.com."));
        var browse = Browse(handler,
            library: [new GameInfo { Repository = "zelda/zelda64recomp", FolderName = "Zelda64Recomp" }],
            listLocation: () => location,
            loadList: l =>
            {
                reads.Add(l);
                return Task.FromResult<(List<GameInfo> Apps, string? Error)>(l == myList ? (apps, null) : (new List<GameInfo>(), "File not found."));
            });
        // This test is about the list itself; hiding library apps has its own test.
        browse.HideLibraryApps = false;
        browse.ShowingCustomList = true;
        var token = TestContext.Current.CancellationToken;

        await browse.ReloadAsync(token);
        browse.Items.Select(i => i.Title).Should().Equal("Banjo", "mario 64", "Zelda 64");
        browse.Items.Single(i => i.InLibrary).Title.Should().Be("Zelda 64");

        browse.Search = "64";
        await browse.ReloadAsync(token);
        browse.Items.Select(i => i.Title).Should().Equal("mario 64", "Zelda 64");

        location = "missing.json";
        await browse.ReloadAsync(token);
        browse.Items.Should().BeEmpty();
        browse.Status.Should().Contain("File not found.");
        reads.Should().Equal(myList, "missing.json");
        handler.AppQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_search_lists_the_games_it_matched_with_their_ways_to_play()
    {
        var token = TestContext.Current.CancellationToken;
        static object App(string slug, params (string Slug, string Title)[] games) =>
            new { id = slug, slug, name = slug, launcher = new { folderName = slug }, games = games.Select(g => new { slug = g.Slug, title = g.Title }) };
        static string Game(string slug, string title, params string[] apps) => JsonSerializer.Serialize(new
        {
            game = new { slug, title, libraryArt = new { capsule = $"https://art.test/{slug}.png" } },
            entries = apps.Select(app => new { id = app, slug = app, name = app, launcher = new { folderName = app }, recommended = app.Length }),
        });
        var handler = new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/apps" => Json(JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    App("banjo-recompiled", ("banjo-kazooie", "Banjo-Kazooie")),
                    App("lighthouse", ("banjo-kazooie", "Banjo-Kazooie")),
                    App("renut", ("banjo-kazooie-nuts-bolts", "Banjo-Kazooie: Nuts & Bolts")),
                    // Found by its description, not its game: its game doesn't match the search.
                    App("party-pack", ("mario-party", "Mario Party")),
                },
                nextCursor = (string?)null,
                isDone = true,
            })),
            "/api/v1/games/banjo-kazooie" => Json(Game("banjo-kazooie", "Banjo-Kazooie", "lighthouse", "banjo-recompiled", "banjo-android")),
            "/api/v1/games/banjo-kazooie-nuts-bolts" => Json(Game("banjo-kazooie-nuts-bolts", "Banjo-Kazooie: Nuts & Bolts", "renut")),
            _ => Json("", HttpStatusCode.NotFound),
        }));
        var browse = Browse(handler);
        browse.Search = "banjo";

        await browse.ReloadAsync(token);
        for (var i = 0; i < 100 && browse.Games.Count == 0; i++) await Task.Delay(10, token);

        browse.Games.Select(g => (g.Title, g.Ways, g.WaysText)).Should().Equal(
            ("Banjo-Kazooie", 3, "3 ways to play \u2192"), ("Banjo-Kazooie: Nuts & Bolts", 1, "1 way to play \u2192"));
        browse.Games[0].Art.Should().Be("https://art.test/banjo-kazooie.png");
        // The game page lists its ways to play best first, from the same read.
        var game = await browse.GetGameAsync("banjo-kazooie", token);
        game!.Entries.Select(e => e.Slug).Should().Equal("banjo-recompiled", "banjo-android", "lighthouse");
        handler.AppQueries.Count(q => q.Length == 0).Should().Be(2, "each matched game's page is read once");
        BrowseText.TitleMatches("Pokémon Red", "pokemon red").Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, 0, "Not rated yet")]
    [InlineData(5, 1, 0, "Mostly runs · 5 run well, 1 with issues")]
    [InlineData(1, 0, 3, "Mostly doesn't run · 1 runs well, 3 don't run")]
    [InlineData(2, 2, 1, "Mixed · 2 run well, 2 with issues, 1 doesn't run")]
    public void Score_sums_up_player_reports(int runs, int issues, int broken, string expected) =>
        BrowseText.Score(runs, issues, broken).Should().Be(expected);

    [Fact]
    public void Card_reads_like_the_website_card()
    {
        var app = new QuiverCatalogApp
        {
            ProjectName = "Banjo: Recompiled", Tags = ["recomp", "n64", "rare"], SupportedOS = ["linux", "windows", "macos"],
            Games = [new() { Title = "Banjo-Kazooie" }], Recommended = 2, AiLevel = "assisted",
        };

        var card = BrowseItem.FromCatalog(app, new Dictionary<string, string> { ["n64"] = "Nintendo 64" });

        card.CardKind.Should().Be("RECOMP · NINTENDO 64");
        card.BasedOn.Should().Equal("Banjo-Kazooie");
        (card.ScoreLabel, card.ScoreCounts, card.ScorePositive).Should().Be(("Mostly runs", " · 2 run well", true));
        card.AiChip.Should().Be("AI-ASSISTED");
        card.PlatformTip.Should().Be("Windows, macOS, Linux");
        BrowseText.TagLabel("harbour masters vs the world").Should().Be("Harbour Masters vs the World");
    }
}
