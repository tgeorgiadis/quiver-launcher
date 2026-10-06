using System.Net;
using System.Web;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Tests;

public class QuiverCatalogClientTests : IDisposable
{
    // A live /apps item, trimmed. aiLevel, basedOn, developer, makers, modSources, verified.pinned
    // and friends are fields this client does not model and must ignore.
    private const string G1RDeluxe = """
        {"addedAt":1788046368000,"aiLevel":"assisted","artwork":"https://raw.githubusercontent.com/bryanthaboi/pokemon-gen1-recomp-project/refs/heads/main/assets/logo/gen1recomp_cover.png",
         "basedOn":{"console":"gb"},"consoles":["gb","gbc","gba"],"description":"G1R Deluxe brings Pokemon RBY / GSC / FRLG / RSE to PC.",
         "developer":{"key":"bryanthaboi","name":"bryanthaboi"},"gameId":"k97eep4wv41b1gwj2dne37gjkn8f748v",
         "games":[{"id":"k97eep4wv41b1gwj2dne37gjkn8f748v","slug":"pokemon-red","title":"Pokemon Red"},{"id":"k9790j3hwh7s9ff8wcqnr6g4vh8f6j6r","slug":"pokemon-gold","title":"Pokemon Gold"}],
         "id":"k17env62k9jdbccmgg4hvabjk58f4dhm","lastReleaseAt":1791309882000,"lastReleaseVersion":"v0.3.56",
         "launcher":{"filesToAdd":["portable.txt"],"folderName":"PokemonRedBlueYellowGoldSilverCrystal-Gen1RecompProject",
           "modSources":[{"name":"gamebanana","url":"https://gamebanana.com/games/25428"}],
           "mods":{"layout":"folderPerMod","path":"mods","sources":[{"provider":"gamebanana","sourceUrl":"https://gamebanana.com/games/25428"}]}},
         "libraryArt":{"capsule":"https://cdn2.steamgriddb.com/grid/177a3b68f66bc7dc139103594b1112a0.png","hero":"https://cdn2.steamgriddb.com/hero_thumb/0aef228ea234e8f32f0618737df16401.jpg"},
         "makers":["Nintendo"],"name":"Pokemon Red / Blue / Yellow / Gold / Silver / Crystal","projectId":"kh7efz9x3rezyj6swwebf28xz18f4078",
         "projectName":"G1R Deluxe","projectType":"port","recommended":1,"reportBroken":0,"reportIssues":0,"reviewCount":1,
         "slug":"pokemonredblueyellowgoldsilvercrystal-gen1recompproject","supportedOS":["android","linux","macos","windows"],
         "tags":["recreation","gb","pokemon","nintendo","mod support"],
         "verified":{"pinned":true,"prerelease":false,"releasedAt":1791027079000,"version":"v0.3.51"}}
        """;

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "QuiverLauncher.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<Uri> _requests = [];
    private HttpClient? _http;

    public void Dispose()
    {
        _http?.Dispose();
        TestFixtures.CleanupDirectory(_dataDirectory);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }

    /// <summary>A client whose requests get the given answers in order.</summary>
    private QuiverCatalogClient Client(params (HttpStatusCode Status, string Body)[] answers)
    {
        var queue = new Queue<(HttpStatusCode Status, string Body)>(answers);
        _http = new HttpClient(new Handler(request =>
        {
            _requests.Add(request.RequestUri!);
            var (status, body) = queue.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }));
        return new QuiverCatalogClient(_http, QuiverCatalogClient.DefaultBaseUrl);
    }

    private AppCatalogService Parser() => new(dataDirectory: _dataDirectory);

    private static Dictionary<string, string?> Query(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query.AllKeys.ToDictionary(key => key!, key => query[key]);
    }

    [Fact]
    public async Task GetAppsAsync_sends_the_filters_and_reads_the_page()
    {
        var client = Client((HttpStatusCode.OK, $$"""{"items":[{{G1RDeluxe}}],"nextCursor":"c2","isDone":false}"""));

        var page = await client.GetAppsAsync(new QuiverCatalogQuery(Os: "windows", Console: "maker:Nintendo", ProjectType: "port"),
            null, TestContext.Current.CancellationToken);

        _requests.Should().ContainSingle().Which.AbsolutePath.Should().Be("/api/v1/apps");
        Query(_requests[0]).Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["limit"] = QuiverCatalogClient.PageSize.ToString(), ["os"] = "windows", ["maker"] = "Nintendo",
            ["projectType"] = "port", ["sort"] = "added",
        });
        page.NextCursor.Should().Be("c2");
        page.IsDone.Should().BeFalse();
        var app = page.Items.Should().ContainSingle().Subject;
        app.Slug.Should().Be("pokemonredblueyellowgoldsilvercrystal-gen1recompproject");
        app.ProjectName.Should().Be("G1R Deluxe");
        app.Games.Select(g => g.Title).Should().Equal("Pokemon Red", "Pokemon Gold");
        app.SupportedOS.Should().Equal("android", "linux", "macos", "windows");
        app.Launcher.FolderName.Should().Be("PokemonRedBlueYellowGoldSilverCrystal-Gen1RecompProject");
        app.Launcher.Mods!.Sources.Should().ContainSingle().Which.SourceUrl.Should().Be("https://gamebanana.com/games/25428");
        app.Verified!.Version.Should().Be("v0.3.51");
    }

    [Fact]
    public async Task GetAppsAsync_search_drops_sort_and_a_finished_page_has_no_cursor()
    {
        var client = Client((HttpStatusCode.OK, """{"items":[],"nextCursor":"c3","isDone":true}"""));

        var page = await client.GetAppsAsync(new QuiverCatalogQuery(Search: "zelda mask", Console: "n64", Sort: "rating"),
            "c2", TestContext.Current.CancellationToken);

        Query(_requests.Single()).Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["limit"] = QuiverCatalogClient.PageSize.ToString(), ["search"] = "zelda mask", ["console"] = "n64", ["cursor"] = "c2",
        });
        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
        page.IsDone.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_request_throws_with_the_status_and_the_site_message()
    {
        var token = TestContext.Current.CancellationToken;
        var client = Client(
            (HttpStatusCode.BadRequest, """{"error":{"code":"INVALID_FILTER","message":"Unknown console"}}"""),
            (HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>"));

        Func<Task> badFilter = () => client.GetAppsAsync(new QuiverCatalogQuery(Console: "psx"), null, token);
        var rejected = (await badFilter.Should().ThrowAsync<HttpRequestException>()).Which;
        rejected.Message.Should().Be("Unknown console");
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        Func<Task> proxyError = () => client.GetAppsAsync(new QuiverCatalogQuery(), null, token);
        (await proxyError.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Missing_readme_is_null_but_a_missing_app_is_an_error()
    {
        var token = TestContext.Current.CancellationToken;
        const string notFound = """{"error":{"code":"NOT_FOUND","message":"App not found"}}""";
        var client = Client((HttpStatusCode.NotFound, notFound), (HttpStatusCode.NotFound, notFound));

        (await client.GetReadmeAsync("mgba", token)).Should().BeNull();
        Func<Task> detail = () => client.GetDetailAsync("mgba", token);
        (await detail.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _requests.Select(r => r.AbsolutePath).Should().Equal("/api/v1/apps/mgba/readme", "/api/v1/apps/mgba");
    }

    [Fact]
    public async Task GitHub_detail_maps_to_a_library_app()
    {
        var client = Client((HttpStatusCode.OK, $$"""
            {"entry":{{G1RDeluxe}},
             "game":{"artwork":"https://cdn2.steamgriddb.com/icon/pokemon-red.png","id":"k97eep4wv41b1gwj2dne37gjkn8f748v"},"games":[],
             "project":{"aiUse":{"level":"assisted","source":"signals"},"author":"bryanthaboi","id":"kh7efz9x3rezyj6swwebf28xz18f4078",
               "name":"G1R Deluxe","projectType":"port","provider":"github","repository":"bryanthaboi/gen1recomp","slug":"github-bryanthaboi-gen1recomp"},
             "withdrawn":false}
            """));
        var detail = await client.GetDetailAsync("pokemonredblueyellowgoldsilvercrystal-gen1recompproject", TestContext.Current.CancellationToken);

        var app = QuiverCatalogMapping.ToGameInfo(Parser(), detail.Entry, detail.Project);

        app.Name.Should().Be("Pokemon Red / Blue / Yellow / Gold / Silver / Crystal");
        app.Project.Should().Be("G1R Deluxe");
        app.Repository.Should().Be("bryanthaboi/gen1recomp");
        app.RepositorySource.Should().BeNull();
        app.FolderName.Should().Be("PokemonRedBlueYellowGoldSilverCrystal-Gen1RecompProject");
        app.GameIconUrl.Should().Be(detail.Entry.Artwork);
        app.Tags.Should().Equal("recreation", "gb", "pokemon", "nintendo", "mod support");
        app.FilesToAdd.Should().Equal("portable.txt");
        app.ReleaseAssetFilter.Should().BeNull();
        app.ModsPath.Should().Be("mods");
        app.ModsLayout.Should().Be(GameModsConfig.LayoutFolderPerMod);
        app.ModsSources.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new GameModSource { Provider = ModProviderIds.GameBanana, SourceUrl = "https://gamebanana.com/games/25428" });
    }

    [Theory]
    [InlineData("gitlab", "sonicdcer/Starfox64Recomp", "sonicdcer/Starfox64Recomp", "gitlab", "EXIT1")]
    [InlineData("manual", null, "", null, null)]
    [InlineData("manual", "FluffyQuack/ReXGlue-EXIT", "", null, null)]
    public void Project_provider_decides_where_releases_come_from(
        string provider, string? repository, string expectedRepository, string? expectedSource, string? expectedFilter)
    {
        var entry = new QuiverCatalogApp
        {
            Slug = "exit-exitrecomp", Name = "Exit", ProjectName = "EXIT Recomp",
            Launcher = new() { FolderName = "Exit-EXITRecomp", ReleaseAssetFilter = "EXIT1" },
        };

        var app = QuiverCatalogMapping.ToGameInfo(Parser(), entry, new() { Provider = provider, Repository = repository });

        app.Repository.Should().Be(expectedRepository);
        app.RepositorySource.Should().Be(expectedSource);
        app.ReleaseAssetFilter.Should().Be(expectedFilter);
        app.IsManuallyManaged.Should().Be(provider == "manual");
        app.FolderName.Should().Be("Exit-EXITRecomp");
    }

    [Fact]
    public void Empty_folder_name_uses_the_slug_and_borrowed_game_art_is_not_the_app_icon()
    {
        var entry = new QuiverCatalogApp
        {
            Slug = "simcity2000-opensc2k", Name = "SimCity 2000", ProjectName = "OpenSC2K",
            Artwork = "https://cdn2.steamgriddb.com/icon/simcity-2000.png", ArtworkFromGame = true,
            Launcher = new() { FolderName = "" },
        };

        var app = QuiverCatalogMapping.ToGameInfo(Parser(), entry, new() { Provider = "github", Repository = "nicholas-ochoa/OpenSC2K" });

        app.FolderName.Should().Be("simcity2000-opensc2k");
        app.GameIconUrl.Should().BeNull();
    }
}
