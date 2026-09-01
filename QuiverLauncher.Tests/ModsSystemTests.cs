using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.Services.Mods.Providers.GameBanana;
using QuiverLauncher.Services.Mods.Providers.Thunderstore;

namespace QuiverLauncher.Tests;

public class ModsSystemTests
{
    [Theory]
    [InlineData("https://thunderstore.io/c/banjo-recompiled/", "banjo-recompiled")]
    [InlineData("https://thunderstore.io/c/banjo-recompiled", "banjo-recompiled")]
    [InlineData("banjo-recompiled", "banjo-recompiled")]
    public void ThunderstoreCommunityParser_parses_url_and_slug(string input, string expected)
    {
        ThunderstoreCommunityParser.TryParse(input, out var slug).Should().BeTrue();
        slug.Should().Be(expected);
    }

    [Fact]
    public void ThunderstoreCommunityParser_rejects_invalid()
    {
        ThunderstoreCommunityParser.TryParse("https://example.com/c/foo", out _).Should().BeFalse();
        ThunderstoreCommunityParser.TryParse("", out _).Should().BeFalse();
    }

    [Fact]
    public void GameModsConfig_normalizes_and_compares_sources_order_insensitively()
    {
        var a = new List<GameModSource>
        {
            new() { Provider = "thunderstore", SourceUrl = "https://thunderstore.io/c/banjo-recompiled/" },
            new() { Provider = "thunderstore", SourceUrl = "ship-of-harkinian" },
        };
        var b = new List<GameModSource>
        {
            new() { Provider = "Thunderstore", SourceUrl = "ship-of-harkinian" },
            new() { Provider = "thunderstore", SourceUrl = "https://thunderstore.io/c/banjo-recompiled/" },
        };

        GameModsConfig.AreEquivalent("mods", a, "mods", b).Should().BeTrue();
        GameModsConfig.AreEquivalent("mods", a, "Mods/Extra", b).Should().BeFalse();
        GameModsConfig.AreEquivalent(
            "mods", a, GameModsConfig.LayoutFolderPerMod,
            "mods", b, null).Should().BeFalse();
        GameModsConfig.AreEquivalent(
            "mods", a, "FolderPerMod",
            "mods", b, GameModsConfig.LayoutFolderPerMod).Should().BeTrue();
    }

    [Fact]
    public void GameModsConfig_resolves_wrap_folder_name_from_archive_then_package()
    {
        GameModsConfig.ResolveWrapFolderName("Music-FRLG.zip", "Other", "id")
            .Should().Be("Music-FRLG");
        GameModsConfig.ResolveWrapFolderName(null, "My Mod", "id")
            .Should().Be("My Mod");
        GameModsConfig.ResolveWrapFolderName(null, "bad/name", "safe-id")
            .Should().Be("bad_name");
        GameModsConfig.ResolveWrapFolderName(null, null, null)
            .Should().Be("mod");
    }

    [Fact]
    public void GameModsFormHelper_round_trips_sources()
    {
        var text = """
                   https://thunderstore.io/c/banjo-recompiled/
                   nexus|https://example.com/game
                   https://gamebanana.com/mods/games/24774
                   """;
        var parsed = GameModsFormHelper.ParseSourcesFromEditor(text);
        parsed.Should().HaveCount(3);
        parsed[0].Provider.Should().Be(ModProviderIds.Thunderstore);
        parsed[1].Provider.Should().Be("nexus");
        parsed[2].Provider.Should().Be(ModProviderIds.GameBanana);

        var formatted = GameModsFormHelper.FormatSourcesForEditor(parsed);
        GameModsFormHelper.ParseSourcesFromEditor(formatted).Should().BeEquivalentTo(parsed);
    }

    [Theory]
    [InlineData("https://gamebanana.com/mods/games/24774", "24774")]
    [InlineData("https://gamebanana.com/games/24774", "24774")]
    [InlineData("https://www.gamebanana.com/mods/games/24774/", "24774")]
    [InlineData("24774", "24774")]
    public void GameBananaSourceParser_parses_url_variants(string input, string expected)
    {
        GameBananaSourceParser.TryParse(input, out var gameId).Should().BeTrue();
        gameId.Should().Be(expected);
    }

    [Fact]
    public void GameBananaSourceParser_rejects_invalid()
    {
        GameBananaSourceParser.TryParse("https://example.com/mods/games/1", out _).Should().BeFalse();
        GameBananaSourceParser.TryParse("https://gamebanana.com/mods/697297", out _).Should().BeFalse();
        GameBananaSourceParser.TryParse("", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(ModProviderIds.Thunderstore, "Thunderstore")]
    [InlineData(ModProviderIds.GameBanana, "GameBanana")]
    [InlineData("THUNDERSTORE", "Thunderstore")]
    [InlineData("unknown", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ModListItem_ResolveProviderBadgeText_maps_known_providers(string? providerId, string expected)
    {
        ModListItem.ResolveProviderBadgeText(providerId).Should().Be(expected);
    }

    [Fact]
    public void ModListItem_AuthorLine_includes_provider_in_brackets()
    {
        var item = new ModListItem
        {
            Package = new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "1",
                Owner = "Owner",
                Name = "Mod",
                FullName = "Owner-Mod",
                SourceDisplayLabel = "Thunderstore · banjo-recompiled",
            },
        };

        item.AuthorLine.Should().Be("by Owner [Thunderstore]");
        item.ProviderBadgeText.Should().Be("Thunderstore");
        item.SourceLabel.Should().Be("Thunderstore · banjo-recompiled");
    }

    [Fact]
    public void ModListItem_AuthorLine_provider_only_when_owner_missing()
    {
        var item = new ModListItem
        {
            Package = new ModPackage
            {
                ProviderId = ModProviderIds.GameBanana,
                SourceKey = "24774",
                Id = "1",
                Owner = "",
                Name = "Mod",
                FullName = "Mod",
            },
        };

        item.AuthorLine.Should().Be("[GameBanana]");
    }

    [Fact]
    public void FormatModsLoadedStatus_exhausted_paging_hides_api_total_gap()
    {
        // API TotalCountHint can include paid/unmapped records never added to the catalog.
        MainWindow.FormatModsLoadedStatus(
                loaded: 94,
                isSearch: false,
                canLoadMore: false,
                totalCountHint: 96)
            .Should().Be("94 mods loaded");
    }

    [Fact]
    public void FormatModsLoadedStatus_with_pages_remaining_shows_n_of_m()
    {
        MainWindow.FormatModsLoadedStatus(
                loaded: 94,
                isSearch: false,
                canLoadMore: true,
                totalCountHint: 96)
            .Should().Be("94 of 96 mods loaded");
    }

    [Fact]
    public void FormatModsLoadedStatus_with_pages_remaining_without_hint_shows_more_available()
    {
        MainWindow.FormatModsLoadedStatus(
                loaded: 30,
                isSearch: false,
                canLoadMore: true,
                totalCountHint: null)
            .Should().Be("30 mods loaded (more available)");
    }

    [Fact]
    public void FormatModsLoadedStatus_search_exhausted_omits_more_available()
    {
        MainWindow.FormatModsLoadedStatus(
                loaded: 12,
                isSearch: true,
                canLoadMore: false,
                totalCountHint: 20)
            .Should().Be("12 search results");
    }

    [Fact]
    public void FormatModsLoadedStatus_search_with_pages_remaining()
    {
        MainWindow.FormatModsLoadedStatus(
                loaded: 12,
                isSearch: true,
                canLoadMore: true,
                totalCountHint: 20)
            .Should().Be("12 of 20 search results");
    }

    [Fact]
    public void ShouldShowModsListLoading_only_when_loading_and_empty()
    {
        MainWindow.ShouldShowModsListLoading(isLoading: true, rowCount: 0).Should().BeTrue();
        MainWindow.ShouldShowModsListLoading(isLoading: true, rowCount: 3).Should().BeFalse();
        MainWindow.ShouldShowModsListLoading(isLoading: false, rowCount: 0).Should().BeFalse();
        MainWindow.ShouldShowModsListLoading(isLoading: false, rowCount: 3).Should().BeFalse();
    }

    [Fact]
    public void GameBananaModProvider_maps_index_nsfw_and_skips_paid()
    {
        var source = new ModSourceRef
        {
            ProviderId = ModProviderIds.GameBanana,
            SourceKey = "24774",
            DisplayLabel = "GameBanana · 24774",
            SourceUrl = "https://gamebanana.com/mods/games/24774",
        };

        var freeNsfw = new GameBananaIndexRecord
        {
            IdRow = 1,
            Name = "NSFW Mod",
            PayType = "free",
            HasContentRatings = true,
            Version = "1.0",
            LikeCount = 42,
            DownloadCount = 1200,
            DateAddedUnix = 1_700_000_000,
            DateModifiedUnix = 1_700_100_000,
            Submitter = new GameBananaSubmitter { Name = "Author" },
            PreviewContent = new GameBananaPreviewContent
            {
                Screenshot = new GameBananaScreenshot
                {
                    BaseUrl = "https://images.gamebanana.com/img/ss/mods",
                    File220 = "thumb.webp",
                    File220Sfw = "thumb_sfw.webp",
                    File530 = "thumb_530.webp",
                    File530Sfw = "thumb_530_sfw.webp",
                },
            },
        };

        var paid = new GameBananaIndexRecord
        {
            IdRow = 2,
            Name = "Paid Mod",
            PayType = "paid",
            HasContentRatings = false,
        };

        var mapped = GameBananaModProvider.MapIndexRecord(freeNsfw, source);
        mapped.Should().NotBeNull();
        mapped!.HasContentRating.Should().BeTrue();
        mapped.IconUrl.Should().EndWith("thumb_530_sfw.webp");
        mapped.LatestVersion!.Version.Should().Be("1.0");
        mapped.RatingScore.Should().Be(42);
        mapped.DownloadCount.Should().Be(1200);
        mapped.UpdatedAtUnix.Should().Be(1_700_100_000);

        GameBananaModProvider.MapIndexRecord(paid, source).Should().BeNull();
    }

    [Fact]
    public void GameBananaModProvider_formats_updates_changelog()
    {
        var markdown = GameBananaModProvider.FormatUpdatesChangelog(
        [
            new GameBananaUpdateRecord
            {
                Version = "1.2",
                Name = "Hotfix",
                ChangeLog =
                [
                    new GameBananaChangeLogEntry { Text = "Fixed crash" },
                    new GameBananaChangeLogEntry { SText = "Improved performance" },
                ],
                Text = "<p>Extra notes</p>",
            },
        ]);

        markdown.Should().Contain("## 1.2 — Hotfix");
        markdown.Should().Contain("- Fixed crash");
        markdown.Should().Contain("- Improved performance");
        markdown.Should().Contain("Extra notes");
    }

    [Fact]
    public void GameBananaModProvider_multi_file_selection_sets_download_url()
    {
        var package = new ModPackage
        {
            ProviderId = ModProviderIds.GameBanana,
            SourceKey = "24774",
            Id = "123",
            Owner = "Author",
            Name = "Mod",
            FullName = "Author-Mod",
            DownloadFiles =
            [
                new ModDownloadFile
                {
                    Id = "10",
                    FileName = "a.zip",
                    DownloadUrl = "https://gamebanana.com/dl/10",
                    FileSize = 100,
                    Version = "1.0",
                },
                new ModDownloadFile
                {
                    Id = "20",
                    FileName = "b.zip",
                    DownloadUrl = "https://gamebanana.com/dl/20",
                    FileSize = 200,
                    Version = "2.0",
                    Description = "Optional pack",
                },
            ],
            LatestVersion = new ModPackageVersion
            {
                Version = "1.0",
                DownloadUrl = "https://gamebanana.com/dl/10",
                FileSize = 100,
            },
        };

        var selected = GameBananaModProvider.WithSelectedFile(package, package.DownloadFiles[1]);
        selected.LatestVersion!.DownloadUrl.Should().Be("https://gamebanana.com/dl/20");
        selected.LatestVersion.Version.Should().Be("2.0");
        selected.LatestVersion.FileSize.Should().Be(200);
    }

    [Fact]
    public async Task GameBananaModProvider_ListPackagesPageAsync_uses_page_tokens()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-gb-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            var page1 = """
                        {"_aMetadata":{"_nRecordCount":3,"_bIsComplete":false,"_nPerpage":2},"_aRecords":[
                          {"_idRow":1,"_sName":"One","_sPayType":"free","_sVersion":"1","_bHasContentRatings":false,"_aSubmitter":{"_sName":"A"}},
                          {"_idRow":2,"_sName":"Two","_sPayType":"free","_sVersion":"1","_bHasContentRatings":false,"_aSubmitter":{"_sName":"B"}}
                        ]}
                        """;
            var page2 = """
                        {"_aMetadata":{"_nRecordCount":3,"_bIsComplete":true,"_nPerpage":2},"_aRecords":[
                          {"_idRow":3,"_sName":"Three","_sPayType":"free","_sVersion":"1","_bHasContentRatings":true,"_aSubmitter":{"_sName":"C"}}
                        ]}
                        """;

            var handler = new StubHttpHandler();
            handler.Responses[
                "https://gamebanana.com/apiv13/Mod/Index?_nPerpage=2&_aFilters[Generic_Game]=24774&_nPage=1&_sSort=Generic_NewAndUpdated"] =
                (HttpStatusCode.OK, page1);
            handler.Responses[
                "https://gamebanana.com/apiv13/Mod/Index?_nPerpage=2&_aFilters[Generic_Game]=24774&_nPage=2&_sSort=Generic_NewAndUpdated"] =
                (HttpStatusCode.OK, page2);

            using var http = new HttpClient(handler);
            var provider = new GameBananaModProvider(http, cache);
            provider.TryParseSource("24774", out var source).Should().BeTrue();

            var first = await provider.ListPackagesPageAsync(source, null, 2);
            first.Packages.Should().HaveCount(2);
            first.NextPageToken.Should().Be("2");
            first.TotalCount.Should().Be(3);

            var second = await provider.ListPackagesPageAsync(source, first.NextPageToken, 2);
            second.Packages.Should().HaveCount(1);
            second.Packages[0].HasContentRating.Should().BeTrue();
            second.NextPageToken.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void ThunderstoreModProvider_maps_nsfw_flag()
    {
        var source = new ModSourceRef
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo-recompiled",
            DisplayLabel = "Thunderstore · banjo-recompiled",
            SourceUrl = "https://thunderstore.io/c/banjo-recompiled/",
        };

        var dto = new ThunderstorePackageDto
        {
            Uuid4 = "uuid",
            Name = "Mod",
            Owner = "Owner",
            FullName = "Owner-Mod",
            HasNsfwContent = true,
            Versions =
            [
                new ThunderstorePackageVersionDto
                {
                    VersionNumber = "1.0.0",
                    DownloadUrl = "https://example.com/mod.zip",
                    IsActive = true,
                },
            ],
        };

        var package = ThunderstoreModProvider.MapPackage(dto, source);
        package.Should().NotBeNull();
        package!.HasContentRating.Should().BeTrue();
    }

    [Fact]
    public void ThunderstoreModProvider_maps_downloads_rating_and_updated()
    {
        var source = new ModSourceRef
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo-recompiled",
            DisplayLabel = "Thunderstore · banjo-recompiled",
            SourceUrl = "https://thunderstore.io/c/banjo-recompiled/",
        };

        var updated = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        var dto = new ThunderstorePackageDto
        {
            Uuid4 = "uuid",
            Name = "Mumbo_Token_Tracker",
            Owner = "Cloudy",
            FullName = "Cloudy-Mumbo_Token_Tracker",
            RatingScore = 7,
            DateUpdated = updated,
            Versions =
            [
                new ThunderstorePackageVersionDto
                {
                    VersionNumber = "1.0.0",
                    Description = "Tracks Mumbo tokens.",
                    DownloadUrl = "https://example.com/v1.zip",
                    Downloads = 300,
                    IsActive = false,
                },
                new ThunderstorePackageVersionDto
                {
                    VersionNumber = "1.1.1",
                    Description = "Tracks Mumbo tokens.",
                    DownloadUrl = "https://example.com/v2.zip",
                    Downloads = 500,
                    IsActive = true,
                },
            ],
        };

        var package = ThunderstoreModProvider.MapPackage(dto, source);
        package.Should().NotBeNull();
        package!.DownloadCount.Should().Be(800);
        package.RatingScore.Should().Be(7);
        package.UpdatedAtUnix.Should().Be(updated.ToUnixTimeSeconds());
        package.Description.Should().Be("Tracks Mumbo tokens.");
        package.LatestVersion!.Version.Should().Be("1.1.1");

        var item = new ModListItem { Package = package };
        item.AuthorLine.Should().Be("by Cloudy [Thunderstore]");
        item.DownloadCountText.Should().Be("800");
        item.RatingText.Should().Be("7");
        item.HasStatsRow.Should().BeTrue();
        item.HasDescriptionPreview.Should().BeTrue();
    }

    [Theory]
    [InlineData(45, "just now")]
    [InlineData(90, "1 minute ago")]
    [InlineData(3_600, "1 hour ago")]
    [InlineData(7_200, "2 hours ago")]
    [InlineData(86_400, "yesterday")]
    [InlineData(3 * 86_400, "3 days ago")]
    [InlineData(45 * 86_400, "last month")]
    public void ModRelativeTime_formats_relative_spans(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var unix = now.ToUnixTimeSeconds() - secondsAgo;
        ModRelativeTime.Format(unix, now).Should().Be(expected);
    }

    [Theory]
    [InlineData(49, "49")]
    [InlineData(1200, "1.2k")]
    [InlineData(15_000, "15k")]
    [InlineData(1_500_000, "1.5M")]
    public void ModRelativeTime_formats_compact_counts(long value, string expected)
    {
        ModRelativeTime.FormatCompactCount(value).Should().Be(expected);
    }

    [Fact]
    public void Catalog_parse_serialize_round_trips_mods_object()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "quiver-mods-ser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var (service, _) = TestFixtures.CreateIsolatedCatalogService(dataDirectory: tempDir);
            var apps = new List<GameInfo>
            {
                new()
                {
                    Name = "Banjo",
                    Repository = "owner/banjo",
                    FolderName = "Banjo",
                    ModsPath = "mods",
                    ModsLayout = GameModsConfig.LayoutFolderPerMod,
                    ModsSources =
                    [
                        new GameModSource
                        {
                            Provider = ModProviderIds.Thunderstore,
                            SourceUrl = "https://thunderstore.io/c/banjo-recompiled/",
                        },
                    ],
                },
            };

            service.SaveLocalApps(apps);
            var json = File.ReadAllText(Path.Combine(tempDir, "apps.json"));
            json.Should().Contain("\"mods\"");
            json.Should().Contain("banjo-recompiled");
            json.Should().Contain("folderPerMod");

            var loaded = service.ParseAppsFromJson(json);
            loaded.Single().ModsPath.Should().Be("mods");
            loaded.Single().ModsLayout.Should().Be(GameModsConfig.LayoutFolderPerMod);
            loaded.Single().ModsSources.Should().ContainSingle()
                .Which.SourceUrl.Should().Contain("banjo-recompiled");
            loaded.Single().CanOpenMods.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_compare_detects_mods_changes()
    {
        var local = new GameInfo
        {
            Name = "App",
            Repository = "owner/repo",
            FolderName = "App",
        };
        var external = new GameInfo
        {
            Name = "App",
            Repository = "owner/repo",
            FolderName = "App",
            ModsPath = "mods",
            ModsLayout = GameModsConfig.LayoutFolderPerMod,
            ModsSources =
            [
                new GameModSource { Provider = "thunderstore", SourceUrl = "banjo-recompiled" },
            ],
        };

        AppCatalogService.AreCatalogFieldsEquivalent(local, external).Should().BeFalse();
        CatalogCompareService.GetChangedFields(local, external).Should().Contain("mods");

        var replaced = CatalogCompareService.ReplaceFromExternal(local, external);
        replaced.ModsPath.Should().Be("mods");
        replaced.ModsLayout.Should().Be(GameModsConfig.LayoutFolderPerMod);
        replaced.ModsSources.Should().ContainSingle();
    }

    [Fact]
    public void ModInstallService_extracts_payload_and_skips_root_metadata()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, "manifest.json", "{}");
            AddZipEntry(zip, "icon.png", "png");
            AddZipEntry(zip, "README.md", "# hi");
            AddZipEntry(zip, "CHANGELOG.md", "notes");
            AddZipEntry(zip, "Token_Tracker.nrm", "modbytes");
            AddZipEntry(zip, "nested/data.txt", "data");
        }

        ms.Position = 0;
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "manifest.json", "icon.png", "README.md", "CHANGELOG.md",
            };
            var files = ModInstallService.ExtractPayloadFiles(ms, root, metadata);
            files.Should().BeEquivalentTo(["Token_Tracker.nrm", "nested/data.txt"]);
            File.Exists(Path.Combine(root, "Token_Tracker.nrm")).Should().BeTrue();
            File.Exists(Path.Combine(root, "manifest.json")).Should().BeFalse();
            File.Exists(Path.Combine(root, "nested", "data.txt")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModInstallService_extracts_7z_payload_and_skips_root_metadata()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-mod.7z");
        File.Exists(fixture).Should().BeTrue("sample-mod.7z fixture should be copied to test output");

        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-extract-7z-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var stream = File.OpenRead(fixture);
            var metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "manifest.json", "icon.png", "README.md", "CHANGELOG.md",
            };
            var files = ModInstallService.ExtractPayloadFiles(stream, root, metadata);
            files.Should().BeEquivalentTo(["payload.nrm", "nested/data.txt"]);
            File.Exists(Path.Combine(root, "payload.nrm")).Should().BeTrue();
            File.Exists(Path.Combine(root, "manifest.json")).Should().BeFalse();
            File.Exists(Path.Combine(root, "README.md")).Should().BeFalse();
            File.Exists(Path.Combine(root, "nested", "data.txt")).Should().BeTrue();
            File.ReadAllText(Path.Combine(root, "payload.nrm")).Should().Be("modbytes");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModInstallService_extracts_rar_payload_and_skips_root_metadata()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-mod.rar");
        File.Exists(fixture).Should().BeTrue("sample-mod.rar fixture should be copied to test output");

        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-extract-rar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var stream = File.OpenRead(fixture);
            var metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "manifest.json", "icon.png", "README.md", "CHANGELOG.md",
            };
            var files = ModInstallService.ExtractPayloadFiles(stream, root, metadata);
            files.Should().BeEquivalentTo(["payload.nrm", "nested/data.txt"]);
            File.Exists(Path.Combine(root, "payload.nrm")).Should().BeTrue();
            File.Exists(Path.Combine(root, "manifest.json")).Should().BeFalse();
            File.Exists(Path.Combine(root, "README.md")).Should().BeFalse();
            File.Exists(Path.Combine(root, "nested", "data.txt")).Should().BeTrue();
            File.ReadAllText(Path.Combine(root, "payload.nrm")).Should().Be("modbytes");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModInstallService_folderPerMod_wraps_flat_zip_into_named_folder()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, "track.ogg", "audio");
            AddZipEntry(zip, "config.json", "{}");
            AddZipEntry(zip, "nested/extra.txt", "x");
        }

        ms.Position = 0;
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-wrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var files = ModInstallService.ExtractPayloadFiles(
                ms,
                root,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                GameModsConfig.LayoutFolderPerMod,
                "Music-FRLG");

            files.Should().BeEquivalentTo([
                "Music-FRLG/track.ogg",
                "Music-FRLG/config.json",
                "Music-FRLG/nested/extra.txt",
            ]);
            File.Exists(Path.Combine(root, "Music-FRLG", "track.ogg")).Should().BeTrue();
            File.Exists(Path.Combine(root, "track.ogg")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModInstallService_folderPerMod_leaves_single_top_level_folder_unchanged()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, "Music-FRLG/track.ogg", "audio");
            AddZipEntry(zip, "Music-FRLG/config.json", "{}");
        }

        ms.Position = 0;
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var files = ModInstallService.ExtractPayloadFiles(
                ms,
                root,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                GameModsConfig.LayoutFolderPerMod,
                "Music-FRLG");

            files.Should().BeEquivalentTo([
                "Music-FRLG/track.ogg",
                "Music-FRLG/config.json",
            ]);
            File.Exists(Path.Combine(root, "Music-FRLG", "track.ogg")).Should().BeTrue();
            // Must not double-wrap into Music-FRLG/Music-FRLG/...
            File.Exists(Path.Combine(root, "Music-FRLG", "Music-FRLG", "track.ogg")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModInstallService_folderPerMod_skips_metadata_then_wraps_payload()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, "manifest.json", "{}");
            AddZipEntry(zip, "icon.png", "png");
            AddZipEntry(zip, "README.md", "# hi");
            AddZipEntry(zip, "Token_Tracker.nrm", "modbytes");
            AddZipEntry(zip, "nested/data.txt", "data");
        }

        ms.Position = 0;
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-wrap-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "manifest.json", "icon.png", "README.md", "CHANGELOG.md",
            };
            var files = ModInstallService.ExtractPayloadFiles(
                ms,
                root,
                metadata,
                GameModsConfig.LayoutFolderPerMod,
                "Mumbo_Token_Tracker");

            files.Should().BeEquivalentTo([
                "Mumbo_Token_Tracker/Token_Tracker.nrm",
                "Mumbo_Token_Tracker/nested/data.txt",
            ]);
            File.Exists(Path.Combine(root, "Mumbo_Token_Tracker", "Token_Tracker.nrm")).Should().BeTrue();
            File.Exists(Path.Combine(root, "manifest.json")).Should().BeFalse();
            File.Exists(Path.Combine(root, "Mumbo_Token_Tracker", "manifest.json")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(new byte[] { (byte)'P', (byte)'K', 0x03, 0x04 }, true)]
    [InlineData(new byte[] { 0x37, 0x7A, 0xBC, 0xAF }, true)]
    [InlineData(new byte[] { 0x52, 0x61, 0x72, 0x21 }, true)]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 }, false)]
    public void GameBananaModProvider_accepts_zip_7z_and_rar_magic_headers(byte[] header, bool expected)
    {
        GameBananaModProvider.IsSupportedArchiveHeader(header).Should().Be(expected);
    }

    [Fact]
    public void InstalledModsStore_round_trips_sidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new InstalledModsStore();
            var doc = new InstalledModsDocument
            {
                Mods =
                [
                    new InstalledModRecord
                    {
                        Provider = "thunderstore",
                        SourceKey = "banjo-recompiled",
                        Id = "uuid",
                        FullName = "Cloudy-Mumbo_Token_Tracker",
                        Owner = "Cloudy",
                        Name = "Mumbo_Token_Tracker",
                        Version = "1.1.1",
                        Files = ["Mumbo_Token_Tracker.nrm"],
                    },
                ],
            };
            store.Save(root, doc);

            var loaded = store.Load(root);
            loaded.Mods.Should().ContainSingle();
            loaded.Mods[0].FullName.Should().Be("Cloudy-Mumbo_Token_Tracker");
            File.Exists(Path.Combine(root, InstalledModsStore.SidecarFileName)).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModCatalogListBuilder_RecordMatchesPackageFile_distinguishes_download_files()
    {
        var package = MakeMultiFileGbPackage();
        var fileA = new InstalledModRecord
        {
            Provider = package.ProviderId,
            Id = package.Id,
            DownloadFileId = "10",
        };
        var fileB = new InstalledModRecord
        {
            Provider = package.ProviderId,
            Id = package.Id,
            DownloadFileId = "20",
        };
        var legacy = new InstalledModRecord
        {
            Provider = package.ProviderId,
            Id = package.Id,
        };

        ModCatalogListBuilder.RecordMatchesPackage(fileA, package).Should().BeTrue();
        ModCatalogListBuilder.RecordMatchesPackageFile(fileA, package, "10").Should().BeTrue();
        ModCatalogListBuilder.RecordMatchesPackageFile(fileA, package, "20").Should().BeFalse();
        ModCatalogListBuilder.RecordMatchesPackageFile(fileB, package, "20").Should().BeTrue();
        ModCatalogListBuilder.RecordMatchesPackageFile(legacy, package, null).Should().BeTrue();
        ModCatalogListBuilder.RecordMatchesPackageFile(legacy, package, "10").Should().BeFalse();
        ModCatalogListBuilder.RecordMatchesPackageFile(fileA, package, null).Should().BeFalse();
    }

    [Fact]
    public void ModDownloadFileSelection_preselects_installed_and_preferred_file_ids()
    {
        var records = new[]
        {
            new InstalledModRecord { DownloadFileId = "10" },
            new InstalledModRecord { DownloadFileId = "20" },
        };

        var ids = ModDownloadFileSelection.GetPreselectedFileIds(records, "30");
        ids.Should().BeEquivalentTo(["10", "20", "30"]);
    }

    [Fact]
    public async Task ModInstallService_second_file_does_not_remove_first()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-multi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var package = MakeMultiFileGbPackage();
            var provider = new FakeZipModProvider(ModProviderIds.GameBanana, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [package.DownloadFiles[0].DownloadUrl] = CreateMinimalModZip("scene.pak", "scene"),
                [package.DownloadFiles[1].DownloadUrl] = CreateMinimalModZip("art.pak", "art"),
            });
            var installer = new ModInstallService(new ModProviderRegistry([provider]));

            await installer.InstallSelectedFilesAsync(
                root, "mods", package, [package], provider, [package.DownloadFiles[0]]);
            await installer.InstallSelectedFilesAsync(
                root, "mods", package, [package], provider, [package.DownloadFiles[1]]);

            var installed = installer.LoadInstalled(root);
            installed.Mods.Should().HaveCount(2);
            installed.Mods.Select(m => m.DownloadFileId).Should().BeEquivalentTo(["10", "20"]);

            var modsDir = installer.GetModsDirectory(root, "mods");
            File.Exists(Path.Combine(modsDir, "scene.pak")).Should().BeTrue();
            File.Exists(Path.Combine(modsDir, "art.pak")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModInstallService_uninstall_one_file_leaves_the_other()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-uninst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var package = MakeMultiFileGbPackage();
            var provider = new FakeZipModProvider(ModProviderIds.GameBanana, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [package.DownloadFiles[0].DownloadUrl] = CreateMinimalModZip("scene.pak", "scene"),
                [package.DownloadFiles[1].DownloadUrl] = CreateMinimalModZip("art.pak", "art"),
            });
            var installer = new ModInstallService(new ModProviderRegistry([provider]));

            await installer.InstallSelectedFilesAsync(
                root, "mods", package, [package], provider, package.DownloadFiles);

            installer.UninstallMatchingFile(root, "mods", package, "10").Should().BeTrue();

            var installed = installer.LoadInstalled(root);
            installed.Mods.Should().ContainSingle();
            installed.Mods[0].DownloadFileId.Should().Be("20");

            var modsDir = installer.GetModsDirectory(root, "mods");
            File.Exists(Path.Combine(modsDir, "scene.pak")).Should().BeFalse();
            File.Exists(Path.Combine(modsDir, "art.pak")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModInstallService_InstallSelectedFilesAsync_two_files_produce_two_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-mod-two-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var package = MakeMultiFileGbPackage();
            var provider = new FakeZipModProvider(ModProviderIds.GameBanana, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [package.DownloadFiles[0].DownloadUrl] = CreateMinimalModZip("scene.pak", "scene"),
                [package.DownloadFiles[1].DownloadUrl] = CreateMinimalModZip("art.pak", "art"),
            });
            var installer = new ModInstallService(new ModProviderRegistry([provider]));

            await installer.InstallSelectedFilesAsync(
                root, "mods", package, [package], provider, package.DownloadFiles);

            var installed = installer.LoadInstalled(root);
            installed.Mods.Should().HaveCount(2);
            installed.Mods.Select(m => m.DownloadFileId).Should().BeEquivalentTo(["10", "20"]);
            installed.Mods.Select(m => m.Id).Distinct().Should().ContainSingle().Which.Should().Be(package.Id);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModCatalogListBuilder_BuildItems_keeps_one_card_for_multiple_file_records()
    {
        var package = MakeMultiFileGbPackage();
        var installed = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = package.ProviderId,
                    SourceKey = package.SourceKey,
                    Id = package.Id,
                    FullName = package.FullName,
                    Owner = package.Owner,
                    Name = package.Name,
                    Version = "1.0",
                    DownloadFileId = "10",
                    DownloadFileName = "scene.zip",
                },
                new InstalledModRecord
                {
                    Provider = package.ProviderId,
                    SourceKey = package.SourceKey,
                    Id = package.Id,
                    FullName = package.FullName,
                    Owner = package.Owner,
                    Name = package.Name,
                    Version = "1.0",
                    DownloadFileId = "20",
                    DownloadFileName = "art.zip",
                },
            ],
        };

        var items = ModCatalogListBuilder.BuildItems([package], installed, out var migrated);
        items.Should().ContainSingle();
        items[0].Status.Should().Be(ModInstallStatus.Installed);
        items[0].VersionLine.Should().Be("2 files");
        migrated.Should().BeFalse();
    }

    [Fact]
    public void ApplyInstalledState_updates_dependency_card_from_sidecar()
    {
        var root = MakeThunderstorePackage("Author", "RootMod", "1.0.0");
        var dep = MakeThunderstorePackage("LibOwner", "SharedLib", "2.0.0");
        var rootItem = new ModListItem { Package = root };
        var depItem = new ModListItem { Package = dep };
        var sidecar = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = dep.ProviderId,
                    SourceKey = dep.SourceKey,
                    Id = dep.Id,
                    FullName = dep.FullName,
                    Owner = dep.Owner,
                    Name = dep.Name,
                    Version = "2.0.0",
                },
            ],
        };

        ModCatalogListBuilder.ApplyInstalledState([rootItem, depItem], sidecar);

        rootItem.Status.Should().Be(ModInstallStatus.NotInstalled);
        depItem.Status.Should().Be(ModInstallStatus.Installed);
        depItem.InstalledVersion.Should().Be("2.0.0");
    }

    [Fact]
    public void ApplyInstalledState_updates_root_and_dependency_cards()
    {
        var root = MakeThunderstorePackage("Author", "RootMod", "1.0.0");
        var dep = MakeThunderstorePackage("LibOwner", "SharedLib", "2.0.0");
        var rootItem = new ModListItem { Package = root };
        var depItem = new ModListItem { Package = dep };
        var sidecar = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = root.ProviderId,
                    SourceKey = root.SourceKey,
                    Id = root.Id,
                    FullName = root.FullName,
                    Owner = root.Owner,
                    Name = root.Name,
                    Version = "1.0.0",
                },
                new InstalledModRecord
                {
                    Provider = dep.ProviderId,
                    SourceKey = dep.SourceKey,
                    Id = dep.Id,
                    FullName = dep.FullName,
                    Owner = dep.Owner,
                    Name = dep.Name,
                    Version = "2.0.0",
                },
            ],
        };

        ModCatalogListBuilder.ApplyInstalledState([rootItem, depItem], sidecar);

        rootItem.Status.Should().Be(ModInstallStatus.Installed);
        depItem.Status.Should().Be(ModInstallStatus.Installed);
    }

    [Theory]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.1.1", "1.1.1", false)]
    [InlineData("v1.2.0", "1.2.0", false)]
    [InlineData("1.0.0", "1.0.10", true)]
    public void ModVersionComparer_detects_updates(string installed, string latest, bool expected)
    {
        ModVersionComparer.IsUpdateAvailable(installed, latest).Should().Be(expected);
    }

    [Fact]
    public void ThunderstoreModProvider_parses_dependency_strings()
    {
        ThunderstoreModProvider.TryParseDependencyString(
            "MythicManiac-TestMod-1.1.0", out var fullName, out var version).Should().BeTrue();
        fullName.Should().Be("MythicManiac-TestMod");
        version.Should().Be("1.1.0");

        ThunderstoreModProvider.TryParseDependencyString(
            "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled-2.0.0",
            out var depFull, out var depVer).Should().BeTrue();
        depFull.Should().Be("LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled");
        depVer.Should().Be("2.0.0");

        ThunderstoreModProvider.TrySplitPackageFullName(depFull, out var owner, out var name).Should().BeTrue();
        owner.Should().Be("LT_Schmiddy");
        name.Should().Be("RecompExternalPython_for_BanjoRecompiled");
    }

    [Fact]
    public void ModInstallService_creates_thunderstore_dependency_stub()
    {
        var parent = new ModPackage
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo-recompiled",
            SourceDisplayLabel = "Thunderstore · banjo-recompiled",
            Id = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
            Owner = "Vertigo",
            Name = "Stop_N_Swop_Transfer_Tool_BK",
            FullName = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
            LatestVersion = new ModPackageVersion { Version = "1.1.4", DownloadUrl = "https://example.com/a.zip" },
        };

        var stub = ModInstallService.CreateThunderstoreDependencyStub(
            parent, "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled");
        stub.Should().NotBeNull();
        stub!.Owner.Should().Be("LT_Schmiddy");
        stub.Name.Should().Be("RecompExternalPython_for_BanjoRecompiled");
        stub.FullName.Should().Be("LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled");
        stub.SourceKey.Should().Be("banjo-recompiled");
        stub.LatestVersion!.DownloadUrl.Should().BeEmpty();
        stub.PackagePageUrl.Should()
            .Be("https://thunderstore.io/c/banjo-recompiled/p/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/");
    }

    [Fact]
    public async Task ModInstallService_enriches_dependency_when_catalog_listing_has_no_download_url()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-ts-dep-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(Path.GetTempPath(), "quiver-ts-dep-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(root);

        try
        {
            var zipBytes = CreateMinimalModZip("payload.nrm", "bytes");
            var experimentalDep = """
                                  {"namespace":"LT_Schmiddy","name":"RecompExternalPython_for_BanjoRecompiled",
                                   "full_name":"LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled",
                                   "owner":"LT_Schmiddy",
                                   "package_url":"https://thunderstore.io/package/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/",
                                   "is_deprecated":false,
                                   "latest":{"version_number":"2.0.0","description":"Python",
                                     "icon":"https://example.com/icon.png","dependencies":[],
                                     "download_url":"https://thunderstore.io/package/download/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/2.0.0/",
                                     "downloads":1,"is_active":true}}
                                  """;

            var handler = new StubHttpHandler();
            handler.Responses[
                "https://thunderstore.io/api/experimental/package/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/"] =
                (HttpStatusCode.OK, experimentalDep);
            handler.BinaryResponses[
                "https://thunderstore.io/package/download/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/2.0.0/"] =
                zipBytes;
            handler.BinaryResponses[
                "https://thunderstore.io/package/download/Vertigo/Stop_N_Swop_Transfer_Tool_BK/1.1.4/"] =
                zipBytes;

            using var http = new HttpClient(handler);
            var provider = new ThunderstoreModProvider(http, cache);
            var installer = new ModInstallService(new ModProviderRegistry([provider]));

            var rootPackage = new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
                Owner = "Vertigo",
                Name = "Stop_N_Swop_Transfer_Tool_BK",
                FullName = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
                LatestVersion = new ModPackageVersion
                {
                    Version = "1.1.4",
                    DownloadUrl = "https://thunderstore.io/package/download/Vertigo/Stop_N_Swop_Transfer_Tool_BK/1.1.4/",
                    Dependencies = ["LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled-2.0.0"],
                },
            };

            // Listing stub in catalog: present but not downloadable until enrich.
            var catalog = new[]
            {
                rootPackage,
                new ModPackage
                {
                    ProviderId = ModProviderIds.Thunderstore,
                    SourceKey = "banjo-recompiled",
                    Id = "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled",
                    Owner = "LT_Schmiddy",
                    Name = "RecompExternalPython_for_BanjoRecompiled",
                    FullName = "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled",
                    LatestVersion = new ModPackageVersion
                    {
                        Version = string.Empty,
                        DownloadUrl = string.Empty,
                    },
                },
            };

            await installer.InstallWithDependenciesAsync(root, "mods", rootPackage, catalog, provider);

            var installed = installer.LoadInstalled(root);
            installed.Mods.Select(m => m.FullName).Should().BeEquivalentTo(
            [
                "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled",
                "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
            ]);
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModInstallService_fetches_missing_dependency_not_in_catalog()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-ts-dep-miss-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(Path.GetTempPath(), "quiver-ts-dep-miss-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(root);

        try
        {
            var zipBytes = CreateMinimalModZip("dep.nrm", "dep");
            var experimentalDep = """
                                  {"namespace":"LT_Schmiddy","name":"RecompExternalPython_for_BanjoRecompiled",
                                   "full_name":"LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled",
                                   "owner":"LT_Schmiddy",
                                   "package_url":"https://thunderstore.io/package/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/",
                                   "is_deprecated":false,
                                   "latest":{"version_number":"2.0.0","description":"Python",
                                     "icon":"https://example.com/icon.png","dependencies":[],
                                     "download_url":"https://thunderstore.io/package/download/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/2.0.0/",
                                     "downloads":1,"is_active":true}}
                                  """;

            var handler = new StubHttpHandler();
            handler.Responses[
                "https://thunderstore.io/api/experimental/package/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/"] =
                (HttpStatusCode.OK, experimentalDep);
            handler.BinaryResponses[
                "https://thunderstore.io/package/download/LT_Schmiddy/RecompExternalPython_for_BanjoRecompiled/2.0.0/"] =
                zipBytes;
            handler.BinaryResponses[
                "https://thunderstore.io/package/download/Vertigo/Stop_N_Swop_Transfer_Tool_BK/1.1.4/"] =
                zipBytes;

            using var http = new HttpClient(handler);
            var provider = new ThunderstoreModProvider(http, cache);
            var installer = new ModInstallService(new ModProviderRegistry([provider]));

            var rootPackage = new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
                Owner = "Vertigo",
                Name = "Stop_N_Swop_Transfer_Tool_BK",
                FullName = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
                LatestVersion = new ModPackageVersion
                {
                    Version = "1.1.4",
                    DownloadUrl = "https://thunderstore.io/package/download/Vertigo/Stop_N_Swop_Transfer_Tool_BK/1.1.4/",
                    Dependencies = ["LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled-2.0.0"],
                },
            };

            // Catalog has only the root — dep must be stubbed + enriched.
            await installer.InstallWithDependenciesAsync(root, "mods", rootPackage, [rootPackage], provider);

            var installed = installer.LoadInstalled(root);
            installed.Mods.Should().HaveCount(2);
            installed.Mods.Should().Contain(m =>
                m.FullName == "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled" &&
                m.Version == "2.0.0");
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModInstallService_lists_missing_direct_dependencies()
    {
        var installer = new ModInstallService(new ModProviderRegistry([]));
        var parent = new ModPackage
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo-recompiled",
            Id = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
            Owner = "Vertigo",
            Name = "Stop_N_Swop_Transfer_Tool_BK",
            FullName = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
            LatestVersion = new ModPackageVersion
            {
                Version = "1.1.4",
                DownloadUrl = "https://example.com/a.zip",
                Dependencies =
                [
                    "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled-2.0.0",
                    "Already-InstalledLib-1.0.0",
                    "Other-MissingLib-3.0.0",
                ],
            },
        };

        var catalog = new[]
        {
            parent,
            new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled",
                Owner = "LT_Schmiddy",
                Name = "Python helper",
                FullName = "LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled",
                LatestVersion = new ModPackageVersion { Version = string.Empty, DownloadUrl = string.Empty },
            },
        };

        var document = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = ModProviderIds.Thunderstore,
                    SourceKey = "banjo-recompiled",
                    Id = "Already-InstalledLib",
                    FullName = "Already-InstalledLib",
                    Name = "InstalledLib",
                },
            ],
        };

        installer.ListMissingDirectDependencies(document, parent, catalog)
            .Should().Equal("Python helper", "MissingLib");
    }

    [Fact]
    public async Task ModInstallService_skips_dependencies_when_installDependencies_is_false()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-ts-dep-skip-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(Path.GetTempPath(), "quiver-ts-dep-skip-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(root);

        try
        {
            var zipBytes = CreateMinimalModZip("payload.nrm", "bytes");
            var handler = new StubHttpHandler();
            handler.BinaryResponses[
                "https://thunderstore.io/package/download/Vertigo/Stop_N_Swop_Transfer_Tool_BK/1.1.4/"] =
                zipBytes;

            using var http = new HttpClient(handler);
            var provider = new ThunderstoreModProvider(http, cache);
            var installer = new ModInstallService(new ModProviderRegistry([provider]));

            var rootPackage = new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
                Owner = "Vertigo",
                Name = "Stop_N_Swop_Transfer_Tool_BK",
                FullName = "Vertigo-Stop_N_Swop_Transfer_Tool_BK",
                LatestVersion = new ModPackageVersion
                {
                    Version = "1.1.4",
                    DownloadUrl = "https://thunderstore.io/package/download/Vertigo/Stop_N_Swop_Transfer_Tool_BK/1.1.4/",
                    Dependencies = ["LT_Schmiddy-RecompExternalPython_for_BanjoRecompiled-2.0.0"],
                },
            };

            await installer.InstallWithDependenciesAsync(
                root, "mods", rootPackage, [rootPackage], provider, installDependencies: false);

            var installed = installer.LoadInstalled(root);
            installed.Mods.Should().ContainSingle(m =>
                m.FullName == "Vertigo-Stop_N_Swop_Transfer_Tool_BK");
            handler.RequestedUrls.Should().NotContain(u =>
                u.Contains("LT_Schmiddy", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModCatalogListBuilder_dedupes_uuid_install_against_owner_name_catalog()
    {
        var catalog = new[]
        {
            new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                SourceDisplayLabel = "Thunderstore · banjo-recompiled",
                Id = "ProxyBK-AbilityAnywhere",
                Owner = "ProxyBK",
                Name = "AbilityAnywhere",
                FullName = "ProxyBK-AbilityAnywhere",
                Description = "Fly or Shock Jump anywhere",
                IconUrl = "https://example.com/icon.png",
                DownloadCount = 1600,
                RatingScore = 3,
                LatestVersion = new ModPackageVersion
                {
                    Version = "0.0.1",
                    DownloadUrl = string.Empty,
                },
            },
            new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "Other-Mod",
                Owner = "Other",
                Name = "Mod",
                FullName = "Other-Mod",
                Description = "Not installed",
                LatestVersion = new ModPackageVersion { Version = "1.0.0", DownloadUrl = string.Empty },
            },
        };

        var installed = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = ModProviderIds.Thunderstore,
                    SourceKey = "banjo-recompiled",
                    Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    FullName = "ProxyBK-AbilityAnywhere",
                    Owner = "ProxyBK",
                    Name = "AbilityAnywhere",
                    Version = "0.0.1",
                },
            ],
        };

        var items = ModCatalogListBuilder.BuildItems(catalog, installed, out var migrated);
        items.Should().HaveCount(2);
        items.Count(i => i.Status == ModInstallStatus.Installed).Should().Be(1);
        items.Single(i => i.Status == ModInstallStatus.Installed).Package.Id
            .Should().Be("ProxyBK-AbilityAnywhere");
        items.Single(i => i.Status == ModInstallStatus.Installed).Package.Description
            .Should().Be("Fly or Shock Jump anywhere");
        migrated.Should().BeTrue();
        installed.Mods.Should().ContainSingle();
        installed.Mods[0].Id.Should().Be("ProxyBK-AbilityAnywhere");
    }

    [Fact]
    public void ModCatalogListBuilder_keeps_true_orphan_install_stub()
    {
        var catalog = new[]
        {
            new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "Other-Mod",
                Owner = "Other",
                Name = "Mod",
                FullName = "Other-Mod",
                LatestVersion = new ModPackageVersion { Version = "1.0.0", DownloadUrl = string.Empty },
            },
        };

        var installed = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = ModProviderIds.Thunderstore,
                    SourceKey = "banjo-recompiled",
                    Id = "Legacy-Only",
                    FullName = "Legacy-Only",
                    Owner = "Legacy",
                    Name = "Only",
                    Version = "1.0.0",
                },
            ],
        };

        var items = ModCatalogListBuilder.BuildItems(catalog, installed, out var migrated);
        items.Should().HaveCount(2);
        items.Should().Contain(i => i.Package.Id == "Other-Mod" && i.Status == ModInstallStatus.NotInstalled);
        items.Should().Contain(i => i.Package.Id == "Legacy-Only" && i.Status == ModInstallStatus.Installed);
        migrated.Should().BeFalse();
    }

    [Fact]
    public void ModCatalogListBuilder_search_mode_excludes_unrelated_installed_orphans()
    {
        var catalog = new[]
        {
            new ModPackage
            {
                ProviderId = ModProviderIds.GameBanana,
                SourceKey = "20371",
                Id = "99",
                Owner = "turpinator",
                Name = "MM ReMasked HD HUD Mod",
                FullName = "turpinator-MM ReMasked HD HUD Mod",
                Description = "True to the original style!",
                IconUrl = "https://example.com/hud.png",
                LatestVersion = new ModPackageVersion { Version = "1.0.1", DownloadUrl = string.Empty },
            },
        };

        var installed = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = ModProviderIds.GameBanana,
                    SourceKey = "20371",
                    Id = "1",
                    FullName = "Fado-(MM) Fadó's Equipment",
                    Owner = "Fado",
                    Name = "(MM) Fadó's Equipment",
                    Version = "2.2.1",
                },
                new InstalledModRecord
                {
                    Provider = ModProviderIds.GameBanana,
                    SourceKey = "20371",
                    Id = "99",
                    FullName = "turpinator-MM ReMasked HD HUD Mod",
                    Owner = "turpinator",
                    Name = "MM ReMasked HD HUD Mod",
                    Version = "1.0.0",
                },
            ],
        };

        var items = ModCatalogListBuilder.BuildItems(
            catalog,
            installed,
            out _,
            ModOrphanInstallMode.Exclude);

        items.Should().ContainSingle();
        items[0].Package.Name.Should().Be("MM ReMasked HD HUD Mod");
        items[0].Status.Should().Be(ModInstallStatus.UpdateAvailable);
    }

    [Fact]
    public void ModCatalogListBuilder_enriches_orphan_from_known_packages()
    {
        var catalog = Array.Empty<ModPackage>();
        var knownPackage = new ModPackage
        {
            ProviderId = ModProviderIds.GameBanana,
            SourceKey = "20371",
            Id = "42",
            Owner = "JamminLZ",
            Name = "Hylian Shield Port",
            FullName = "JamminLZ-Hylian Shield Port",
            Description = "A port of the Hylian Shield",
            IconUrl = "https://example.com/shield.png",
            DownloadCount = 500,
            RatingScore = 8,
            UpdatedAtUnix = 1_700_000_000,
            LatestVersion = new ModPackageVersion { Version = "1.2.0", DownloadUrl = string.Empty },
        };

        var known = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        ModCatalogListBuilder.RememberPackages(known, [knownPackage]);

        var installed = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = ModProviderIds.GameBanana,
                    SourceKey = "20371",
                    Id = "42",
                    FullName = "JamminLZ-Hylian Shield Port",
                    Owner = "JamminLZ",
                    Name = "Hylian Shield Port",
                    Version = "1.2.0",
                },
            ],
        };

        var items = ModCatalogListBuilder.BuildItems(
            catalog,
            installed,
            out var migrated,
            ModOrphanInstallMode.Include,
            known);

        items.Should().ContainSingle();
        migrated.Should().BeFalse();
        var package = items[0].Package;
        package.Description.Should().Be("A port of the Hylian Shield");
        package.IconUrl.Should().Be("https://example.com/shield.png");
        package.DownloadCount.Should().Be(500);
        package.RatingScore.Should().Be(8);
        items[0].Status.Should().Be(ModInstallStatus.Installed);
    }

    [Fact]
    public void ModCatalogListBuilder_enriches_orphan_via_owner_name_when_id_differs()
    {
        var knownPackage = new ModPackage
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo-recompiled",
            Id = "ProxyBK-AbilityAnywhere",
            Owner = "ProxyBK",
            Name = "AbilityAnywhere",
            FullName = "ProxyBK-AbilityAnywhere",
            Description = "Fly or Shock Jump anywhere",
            IconUrl = "https://example.com/ability.png",
            LatestVersion = new ModPackageVersion { Version = "0.0.1", DownloadUrl = string.Empty },
        };

        var known = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        ModCatalogListBuilder.RememberPackages(known, [knownPackage]);

        // Known cache is keyed by Owner-Name Id; sidecar still has legacy UUID + matching Owner/Name
        // but a mismatched FullName so Id/FullName lookups miss.
        var installed = new InstalledModsDocument
        {
            Mods =
            [
                new InstalledModRecord
                {
                    Provider = ModProviderIds.Thunderstore,
                    SourceKey = "banjo-recompiled",
                    Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    FullName = "legacy-mismatch",
                    Owner = "ProxyBK",
                    Name = "AbilityAnywhere",
                    Version = "0.0.1",
                },
            ],
        };

        var items = ModCatalogListBuilder.BuildItems(
            Array.Empty<ModPackage>(),
            installed,
            out _,
            ModOrphanInstallMode.Include,
            known);

        items.Should().ContainSingle();
        items[0].Package.Description.Should().Be("Fly or Shock Jump anywhere");
        items[0].Package.IconUrl.Should().Be("https://example.com/ability.png");
        items[0].Package.Id.Should().Be("ProxyBK-AbilityAnywhere");
    }

    [Fact]
    public void ModCatalogListBuilder_AppendUniquePackages_skips_overlapping_ids()
    {
        var packages = new List<ModPackage>
        {
            MakePkg(ModProviderIds.Thunderstore, "banjo", "AlwaysHighPolyBanjo", "TSRStormed", "AlwaysHighPolyBanjo"),
            MakePkg(ModProviderIds.Thunderstore, "banjo", "Asset_Expansion_Pak", "Dario", "Asset_Expansion_Pak"),
        };

        ModCatalogListBuilder.AppendUniquePackages(packages,
        [
            MakePkg(ModProviderIds.Thunderstore, "banjo", "AlwaysHighPolyBanjo", "TSRStormed", "AlwaysHighPolyBanjo"),
            MakePkg(ModProviderIds.Thunderstore, "banjo", "Banjo_Dreamie", "Loggo", "Banjo_Dreamie"),
            MakePkg(ModProviderIds.Thunderstore, "banjo", "Asset_Expansion_Pak", "Dario", "Asset_Expansion_Pak"),
        ]);

        packages.Should().HaveCount(3);
        packages.Select(p => p.Id).Should().Equal(
            "AlwaysHighPolyBanjo", "Asset_Expansion_Pak", "Banjo_Dreamie");
    }

    [Fact]
    public void ModCatalogListBuilder_BuildItems_collapses_duplicate_catalog_rows()
    {
        var catalog = new[]
        {
            MakePkg(ModProviderIds.Thunderstore, "banjo", "AlwaysHighPolyBanjo", "TSRStormed", "AlwaysHighPolyBanjo"),
            MakePkg(ModProviderIds.Thunderstore, "banjo", "AlwaysHighPolyBanjo", "TSRStormed", "AlwaysHighPolyBanjo"),
            MakePkg(ModProviderIds.Thunderstore, "banjo", "Banjo_Dreamie", "Loggo", "Banjo_Dreamie"),
        };

        var items = ModCatalogListBuilder.BuildItems(
            catalog,
            new InstalledModsDocument(),
            out _);

        items.Should().HaveCount(2);
        items.Select(i => i.Package.Id).Should().Equal("AlwaysHighPolyBanjo", "Banjo_Dreamie");
    }

    [Fact]
    public void ModCatalogListBuilder_PackagesMatch_by_fullname_name_when_id_differs()
    {
        var uuid = MakePkg(
            ModProviderIds.Thunderstore, "banjo",
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "ProxyBK", "AbilityAnywhere");
        var named = MakePkg(
            ModProviderIds.Thunderstore, "banjo",
            "ProxyBK-AbilityAnywhere", "ProxyBK", "AbilityAnywhere");

        ModCatalogListBuilder.PackagesMatch(uuid, named).Should().BeTrue();
        ModCatalogListBuilder.PackagesMatch(named, MakePkg(
            ModProviderIds.Thunderstore, "banjo", "Other-Mod", "Other", "Mod")).Should().BeFalse();
    }

    [Fact]
    public void ModCatalogListBuilder_FindListIndexByPackage_prefers_package_identity()
    {
        var a = MakePkg(ModProviderIds.Thunderstore, "banjo", "A", "Own", "A");
        var b = MakePkg(ModProviderIds.Thunderstore, "banjo", "B", "Own", "B");
        var c = MakePkg(ModProviderIds.Thunderstore, "banjo", "C", "Own", "C");
        var rows = new List<ModListItem>
        {
            new() { Package = a },
            new() { Package = b },
            new() { Package = c },
        };

        // After Installed-first sort, B may move — identity still finds it.
        ModCatalogListBuilder.FindListIndexByPackage(rows, b, fallbackIndex: 0).Should().Be(1);
        ModCatalogListBuilder.FindListIndexByPackage(rows, c, fallbackIndex: 0).Should().Be(2);
    }

    [Fact]
    public void ModCatalogListBuilder_FindListIndexByPackage_falls_back_when_missing()
    {
        var rows = new List<ModListItem>
        {
            new() { Package = MakePkg(ModProviderIds.Thunderstore, "banjo", "A", "Own", "A") },
            new() { Package = MakePkg(ModProviderIds.Thunderstore, "banjo", "B", "Own", "B") },
        };
        var missing = MakePkg(ModProviderIds.Thunderstore, "banjo", "Z", "Own", "Z");

        ModCatalogListBuilder.FindListIndexByPackage(rows, missing, fallbackIndex: 1).Should().Be(1);
        ModCatalogListBuilder.FindListIndexByPackage(rows, missing, fallbackIndex: 99).Should().Be(1);
        ModCatalogListBuilder.FindListIndexByPackage(rows, null, fallbackIndex: -1).Should().Be(0);
        ModCatalogListBuilder.FindListIndexByPackage([], missing, fallbackIndex: 0).Should().Be(-1);
    }

    [Fact]
    public void ModListItem_ApplyInstalled_updates_status_in_place()
    {
        var package = new ModPackage
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo",
            SourceDisplayLabel = "Thunderstore · banjo",
            Id = "Cloudy-Mumbo",
            Owner = "Cloudy",
            Name = "Mumbo",
            FullName = "Cloudy-Mumbo",
            LatestVersion = new ModPackageVersion
            {
                Version = "2.0.0",
                DownloadUrl = "https://example.com/mod.zip",
            },
        };

        var item = new ModListItem { Package = package };
        item.CanInstall.Should().BeTrue();
        item.CanUninstall.Should().BeFalse();

        item.ApplyInstalled(new InstalledModRecord { Version = "2.0.0" });
        item.Status.Should().Be(ModInstallStatus.Installed);
        item.CanInstall.Should().BeFalse();
        item.CanUpdate.Should().BeFalse();
        item.CanUninstall.Should().BeTrue();
        item.InstalledVersion.Should().Be("2.0.0");

        item.ApplyInstalled(new InstalledModRecord { Version = "1.0.0" });
        item.Status.Should().Be(ModInstallStatus.UpdateAvailable);
        item.CanUpdate.Should().BeTrue();

        item.ApplyInstalled([]);
        item.Status.Should().Be(ModInstallStatus.NotInstalled);
        item.CanInstall.Should().BeTrue();
        item.InstalledVersion.Should().BeNull();
    }

    [Fact]
    public void ModListItem_ApplyInstalled_multiple_files_update_when_any_file_is_behind()
    {
        var package = MakeMultiFileGbPackage();
        var item = new ModListItem { Package = package };

        item.ApplyInstalled(
        [
            new InstalledModRecord
            {
                Provider = package.ProviderId,
                Id = package.Id,
                Version = "1.0",
                DownloadFileId = "10",
            },
            new InstalledModRecord
            {
                Provider = package.ProviderId,
                Id = package.Id,
                Version = "0.9",
                DownloadFileId = "20",
            },
        ]);

        item.Status.Should().Be(ModInstallStatus.UpdateAvailable);
        item.CanUpdate.Should().BeTrue();
        item.CanInstall.Should().BeFalse();
        item.AllowsAdditionalFiles.Should().BeFalse();
        item.VersionLine.Should().Be("2 files · update");
    }

    [Fact]
    public void ModListItem_CanInstall_add_files_only_when_uninstalled_files_remain()
    {
        var package = MakeMultiFileGbPackage();
        var item = new ModListItem { Package = package };

        item.ApplyInstalled(
        [
            new InstalledModRecord
            {
                Provider = package.ProviderId,
                Id = package.Id,
                Version = "1.0",
                DownloadFileId = "10",
            },
        ]);

        item.CanInstall.Should().BeTrue();
        item.InstallButtonLabel.Should().Be("Add files");
        item.AllowsAdditionalFiles.Should().BeTrue();

        item.ApplyInstalled(
        [
            new InstalledModRecord
            {
                Provider = package.ProviderId,
                Id = package.Id,
                Version = "1.0",
                DownloadFileId = "10",
            },
            new InstalledModRecord
            {
                Provider = package.ProviderId,
                Id = package.Id,
                Version = "1.0",
                DownloadFileId = "20",
            },
        ]);

        item.CanInstall.Should().BeFalse();
        item.AllowsAdditionalFiles.Should().BeFalse();
    }

    [Fact]
    public void ModDownloadFileSelection_GetUninstalledFiles_omits_installed_ids()
    {
        var package = MakeMultiFileGbPackage();
        var remaining = ModDownloadFileSelection.GetUninstalledFiles(
            package.DownloadFiles,
            [
                new InstalledModRecord { DownloadFileId = "10", DownloadFileName = "scene.zip" },
            ]);

        remaining.Should().ContainSingle();
        remaining[0].Id.Should().Be("20");
    }

    [Fact]
    public void ModDownloadFileSelection_per_file_current_file_not_update_when_page_version_higher()
    {
        // GameBanana 650008-style: page v2.0, fiercedeity still labeled v1.0.
        var package = MakeGbPageVsFileVersionPackage();
        var fierceDeity = new InstalledModRecord
        {
            Provider = package.ProviderId,
            Id = package.Id,
            Version = "1.0",
            DownloadFileId = "30",
            DownloadFileName = "fiercedeity.zip",
        };

        ModDownloadFileSelection.IsRecordUpdateAvailable(fierceDeity, package).Should().BeFalse();
        ModDownloadFileSelection.ResolveLatestVersion(fierceDeity, package).Should().Be("1.0");
    }

    [Fact]
    public void ModDownloadFileSelection_per_file_no_page_fallback_when_download_files_empty()
    {
        var detailed = MakeGbPageVsFileVersionPackage();
        var package = new ModPackage
        {
            ProviderId = detailed.ProviderId,
            SourceKey = detailed.SourceKey,
            SourceDisplayLabel = detailed.SourceDisplayLabel,
            Id = detailed.Id,
            Owner = detailed.Owner,
            Name = detailed.Name,
            FullName = detailed.FullName,
            DownloadFiles = [],
            LatestVersion = detailed.LatestVersion,
        };
        var fierceDeity = new InstalledModRecord
        {
            Provider = package.ProviderId,
            Id = package.Id,
            Version = "1.0",
            DownloadFileId = "30",
            DownloadFileName = "fiercedeity.zip",
        };

        ModDownloadFileSelection.IsRecordUpdateAvailable(fierceDeity, package).Should().BeFalse();
        ModDownloadFileSelection.ResolveLatestVersion(fierceDeity, package).Should().Be("1.0");
    }

    [Fact]
    public void ModDownloadFileSelection_per_file_detects_true_update_for_behind_file()
    {
        var package = MakeGbPageVsFileVersionPackage();
        var behind = new InstalledModRecord
        {
            Provider = package.ProviderId,
            Id = package.Id,
            Version = "1.0",
            DownloadFileId = "10",
            DownloadFileName = "standard.zip",
        };

        ModDownloadFileSelection.IsRecordUpdateAvailable(behind, package).Should().BeTrue();
        ModDownloadFileSelection.ResolveLatestVersion(behind, package).Should().Be("2.0");
    }

    [Fact]
    public void ModDownloadFileSelection_ResolveFilesToUpdate_only_returns_behind_files()
    {
        var package = MakeGbPageVsFileVersionPackage();
        var records = new[]
        {
            new InstalledModRecord
            {
                Provider = package.ProviderId,
                Id = package.Id,
                Version = "1.0",
                DownloadFileId = "10",
            },
            new InstalledModRecord
            {
                Provider = package.ProviderId,
                Id = package.Id,
                Version = "1.0",
                DownloadFileId = "30",
            },
        };

        var toUpdate = ModDownloadFileSelection.ResolveFilesToUpdate(package, records);
        toUpdate.Should().ContainSingle();
        toUpdate[0].Id.Should().Be("10");
    }

    [Fact]
    public void ModListItem_SetKnownDownloadFiles_keeps_current_file_installed()
    {
        var detailed = MakeGbPageVsFileVersionPackage();
        var indexPackage = new ModPackage
        {
            ProviderId = detailed.ProviderId,
            SourceKey = detailed.SourceKey,
            SourceDisplayLabel = detailed.SourceDisplayLabel,
            Id = detailed.Id,
            Owner = detailed.Owner,
            Name = detailed.Name,
            FullName = detailed.FullName,
            DownloadFiles = [],
            LatestVersion = detailed.LatestVersion,
        };
        var item = new ModListItem { Package = indexPackage };
        item.ApplyInstalled(new InstalledModRecord
        {
            Provider = indexPackage.ProviderId,
            Id = indexPackage.Id,
            Version = "1.0",
            DownloadFileId = "30",
            DownloadFileName = "fiercedeity.zip",
        });

        // With empty files + file identity, no false Update from page v2.0.
        item.Status.Should().Be(ModInstallStatus.Installed);

        item.SetKnownDownloadFiles(detailed.DownloadFiles);
        item.Status.Should().Be(ModInstallStatus.Installed);
        item.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task ModCatalogLoader_load_more_dedupes_overlapping_page_packages()
    {
        var provider = new FakePagedBrowseProvider(
            ModProviderIds.Thunderstore,
            "banjo",
            new Dictionary<string, ModPackagePage>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = new()
                {
                    Packages =
                    [
                        MakePkg(ModProviderIds.Thunderstore, "banjo", "A", "Own", "A"),
                        MakePkg(ModProviderIds.Thunderstore, "banjo", "B", "Own", "B"),
                    ],
                    NextPageToken = "2",
                },
                ["2"] = new()
                {
                    Packages =
                    [
                        MakePkg(ModProviderIds.Thunderstore, "banjo", "B", "Own", "B"), // overlap
                        MakePkg(ModProviderIds.Thunderstore, "banjo", "C", "Own", "C"),
                    ],
                    NextPageToken = null,
                },
            });

        var loader = new ModCatalogLoader(new ModProviderRegistry([provider]));
        var sources = new[]
        {
            new GameModSource { Provider = ModProviderIds.Thunderstore, SourceUrl = "banjo" },
        };

        var session = await loader.LoadBrowseSessionAsync(sources, sourceFilterKey: null, pageSize: 2);
        session.Packages.Should().HaveCount(2);
        session.CanLoadMore.Should().BeTrue();

        var more = await loader.LoadMoreBrowseSessionAsync(session, pageSize: 2);
        more.Packages.Select(p => p.Id).Should().Equal("A", "B", "C");
        more.CanLoadMore.Should().BeFalse();
    }

    [Fact]
    public void ModProviderRegistry_resolves_thunderstore_and_gamebanana()
    {
        using var http = new HttpClient();
        var cache = Path.Combine(Path.GetTempPath(), "quiver-mod-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            var registry = new ModProviderRegistry(http, cache);
            registry.TryGet(ModProviderIds.Thunderstore, out var provider).Should().BeTrue();
            provider.DisplayName.Should().Be("Thunderstore");
            provider.SupportsPagedListing.Should().BeTrue();
            provider.SupportsRemoteSearch.Should().BeTrue();
            provider.TryParseSource("banjo-recompiled", out var source).Should().BeTrue();
            source.SourceKey.Should().Be("banjo-recompiled");

            registry.TryGet(ModProviderIds.GameBanana, out var gb).Should().BeTrue();
            gb.SupportsPagedListing.Should().BeTrue();
            gb.SupportsRemoteSearch.Should().BeTrue();
            gb.TryParseSource("https://gamebanana.com/mods/games/24774", out var gbSource).Should().BeTrue();
            gbSource.SourceKey.Should().Be("24774");

            registry.TryGet("unknown-host", out _).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void ModListSorter_installed_first_puts_installed_ahead_alphabetically()
    {
        static ModListItem Make(string name, ModInstallStatus status)
        {
            var item = new ModListItem
            {
                Package = new ModPackage
                {
                    ProviderId = "thunderstore",
                    SourceKey = "community",
                    SourceDisplayLabel = "Thunderstore",
                    Id = name,
                    Owner = "Owner",
                    Name = name,
                    FullName = $"Owner-{name}",
                },
            };
            item.Status = status;
            if (status != ModInstallStatus.NotInstalled)
                item.InstalledVersion = "1.0.0";
            return item;
        }

        var items = new[]
        {
            Make("Zebra", ModInstallStatus.NotInstalled),
            Make("Apple", ModInstallStatus.Installed),
            Make("Mango", ModInstallStatus.UpdateAvailable),
            Make("Banana", ModInstallStatus.NotInstalled),
            Make("Cherry", ModInstallStatus.Installed),
        };

        var sorted = ModListSorter.Sort(items, ModListSorter.InstalledFirst);
        sorted.Select(i => i.DisplayName).Should().Equal("Apple", "Cherry", "Mango", "Banana", "Zebra");
    }

    [Fact]
    public void ModListSorter_name_desc_and_updates_first()
    {
        static ModListItem Make(string name, ModInstallStatus status)
        {
            var item = new ModListItem
            {
                Package = new ModPackage
                {
                    ProviderId = "thunderstore",
                    SourceKey = "community",
                    SourceDisplayLabel = "Thunderstore",
                    Id = name,
                    Owner = "Owner",
                    Name = name,
                    FullName = $"Owner-{name}",
                },
            };
            item.Status = status;
            return item;
        }

        var items = new[]
        {
            Make("Beta", ModInstallStatus.Installed),
            Make("Alpha", ModInstallStatus.UpdateAvailable),
            Make("Gamma", ModInstallStatus.NotInstalled),
        };

        ModListSorter.Sort(items, ModListSorter.NameDesc)
            .Select(i => i.DisplayName)
            .Should().Equal("Gamma", "Beta", "Alpha");

        ModListSorter.Sort(items, ModListSorter.UpdatesFirst)
            .Select(i => i.DisplayName)
            .Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void ModListSorter_top_rated_merges_sources_by_rating_score()
    {
        static ModListItem Make(string provider, string name, int rating)
        {
            var item = new ModListItem
            {
                Package = new ModPackage
                {
                    ProviderId = provider,
                    SourceKey = "src",
                    Id = name,
                    Owner = "Owner",
                    Name = name,
                    FullName = $"Owner-{name}",
                    RatingScore = rating,
                },
            };
            return item;
        }

        // Provider order as catalog loader would concatenate (TS page then GB page).
        var items = new[]
        {
            Make(ModProviderIds.Thunderstore, "MM ReMasked HD HUD Mod", 2),
            Make(ModProviderIds.GameBanana, "MM Reloaded", 53),
            Make(ModProviderIds.GameBanana, "3DS Link", 40),
        };

        ModListSorter.Sort(items, ModListSorter.TopRated)
            .Select(i => i.DisplayName)
            .Should().Equal("MM Reloaded", "3DS Link", "MM ReMasked HD HUD Mod");
        ModListSorter.IsRemoteSort(ModListSorter.MostDownloaded).Should().BeTrue();
        ModListSorter.IsRemoteSort(ModListSorter.InstalledFirst).Should().BeFalse();
    }

    [Fact]
    public void ModListSorter_newest_prefers_created_at_over_updated_at()
    {
        static ModListItem Make(string name, long? created, long? updated)
        {
            var item = new ModListItem
            {
                Package = new ModPackage
                {
                    ProviderId = ModProviderIds.Thunderstore,
                    SourceKey = "src",
                    Id = name,
                    Owner = "Owner",
                    Name = name,
                    FullName = $"Owner-{name}",
                    CreatedAtUnix = created,
                    UpdatedAtUnix = updated,
                },
            };
            return item;
        }

        var items = new[]
        {
            Make("OldCreated_NewUpdate", created: 1_000, updated: 9_000),
            Make("NewCreated_OldUpdate", created: 8_000, updated: 2_000),
            Make("FallbackUpdatedOnly", created: null, updated: 7_000),
        };

        ModListSorter.Sort(items, ModListSorter.Newest)
            .Select(i => i.Package.Name)
            .Should().Equal("NewCreated_OldUpdate", "FallbackUpdatedOnly", "OldCreated_NewUpdate");
    }

    [Fact]
    public void ModListSorter_most_downloaded_and_last_updated_order_by_stats()
    {
        static ModListItem Make(string name, long downloads, long? updated)
        {
            var item = new ModListItem
            {
                Package = new ModPackage
                {
                    ProviderId = ModProviderIds.GameBanana,
                    SourceKey = "src",
                    Id = name,
                    Owner = "Owner",
                    Name = name,
                    FullName = $"Owner-{name}",
                    DownloadCount = downloads,
                    UpdatedAtUnix = updated,
                },
            };
            return item;
        }

        var items = new[]
        {
            Make("A", downloads: 10, updated: 100),
            Make("B", downloads: 50, updated: 50),
            Make("C", downloads: 20, updated: 200),
        };

        ModListSorter.Sort(items, ModListSorter.MostDownloaded)
            .Select(i => i.DisplayName)
            .Should().Equal("B", "C", "A");
        ModListSorter.Sort(items, ModListSorter.LastUpdated)
            .Select(i => i.DisplayName)
            .Should().Equal("C", "A", "B");
    }

    [Theory]
    [InlineData(ModListSorter.TopRated, "top-rated", "Generic_MostLiked", "popularity")]
    [InlineData(ModListSorter.Newest, "newest", "Generic_Newest", "date")]
    [InlineData(ModListSorter.LastUpdated, "last-updated", "Generic_NewAndUpdated", "udate")]
    [InlineData(ModListSorter.MostDownloaded, "most-downloaded", "Generic_MostDownloaded", "popularity")]
    [InlineData(ModListSorter.InstalledFirst, "last-updated", "Generic_NewAndUpdated", "udate")]
    public void ModListSorter_maps_remote_provider_sort_params(
        string sortMode,
        string tsOrdering,
        string gbIndexSort,
        string gbSearchOrder)
    {
        ModListSorter.ToThunderstoreOrdering(sortMode).Should().Be(tsOrdering);
        ModListSorter.ToGameBananaIndexSort(sortMode).Should().Be(gbIndexSort);
        ModListSorter.ToGameBananaSearchOrder(sortMode).Should().Be(gbSearchOrder);
    }

    [Fact]
    public void GameBananaApiClient_builds_index_and_search_urls_with_sort()
    {
        GameBananaApiClient.BuildIndexUrl("20371", 1, 30, "Generic_MostLiked")
            .Should().Be(
                "https://gamebanana.com/apiv13/Mod/Index?_nPerpage=30&_aFilters[Generic_Game]=20371&_nPage=1&_sSort=Generic_MostLiked");

        GameBananaApiClient.BuildSearchUrl("20371", "wolf", 1, 15, "popularity")
            .Should().Be(
                "https://gamebanana.com/apiv13/Util/Search/Results?_sModelName=Mod&_sOrder=popularity&_idGameRow=20371&_sSearchString=wolf&_csvFields=name,description,owner,credits&_nPerpage=15&_nPage=1");
    }

    [Fact]
    public void ModListItem_source_filter_key_matches_package()
    {
        var package = new ModPackage
        {
            ProviderId = "thunderstore",
            SourceKey = "banjo-recompiled",
            SourceDisplayLabel = "Thunderstore · banjo-recompiled",
            Id = "id",
            Owner = "Cloudy",
            Name = "Mumbo_Token_Tracker",
            FullName = "Cloudy-Mumbo_Token_Tracker",
            LatestVersion = new ModPackageVersion
            {
                Version = "1.1.1",
                DownloadUrl = "https://example.com/mod.zip",
            },
        };

        var item = new ModListItem { Package = package };
        item.ApplyInstalled(new InstalledModRecord { Version = "1.0.0" });
        item.Status.Should().Be(ModInstallStatus.UpdateAvailable);

        var filterKey = $"{item.ProviderId}|{item.SourceKey}";
        filterKey.Should().Be("thunderstore|banjo-recompiled");
    }

    [Fact]
    public void ThunderstoreApiClient_builds_doc_urls()
    {
        ThunderstoreApiClient.BuildPackageDocUrl("Dario", "Asset_Expansion_Pak", "0.0.3", "readme")
            .Should().Be("https://thunderstore.io/api/experimental/package/Dario/Asset_Expansion_Pak/0.0.3/readme/");

        ThunderstoreApiClient.BuildPackageDocUrl("Dario", "Asset_Expansion_Pak", "0.0.3", "CHANGELOG")
            .Should().Be("https://thunderstore.io/api/experimental/package/Dario/Asset_Expansion_Pak/0.0.3/changelog/");
    }

    [Fact]
    public async Task ThunderstoreApiClient_caches_readme_and_treats_404_as_null()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-mod-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            var handler = new StubHttpHandler();
            handler.Responses[
                "https://thunderstore.io/api/experimental/package/Dario/Asset_Expansion_Pak/0.0.3/readme/"] =
                (HttpStatusCode.OK, """{"markdown":"# Hello\n\nWorld"}""");
            handler.Responses[
                "https://thunderstore.io/api/experimental/package/Dario/Asset_Expansion_Pak/0.0.3/changelog/"] =
                (HttpStatusCode.NotFound, "");

            using var http = new HttpClient(handler);
            var client = new ThunderstoreApiClient(http, cache);

            var readme = await client.GetPackageDocMarkdownAsync("Dario", "Asset_Expansion_Pak", "0.0.3", "readme");
            readme.Should().Contain("Hello");
            handler.RequestCount.Should().Be(1);

            // Second call should hit disk cache.
            var readmeAgain = await client.GetPackageDocMarkdownAsync("Dario", "Asset_Expansion_Pak", "0.0.3", "readme");
            readmeAgain.Should().Contain("Hello");
            handler.RequestCount.Should().Be(1);

            var changelog = await client.GetPackageDocMarkdownAsync("Dario", "Asset_Expansion_Pak", "0.0.3", "changelog");
            changelog.Should().BeNull();
            handler.RequestCount.Should().Be(2);

            // Cached empty changelog stays null without another network call.
            var changelogAgain = await client.GetPackageDocMarkdownAsync("Dario", "Asset_Expansion_Pak", "0.0.3", "changelog");
            changelogAgain.Should().BeNull();
            handler.RequestCount.Should().Be(2);
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task ThunderstoreModProvider_GetReadmeAsync_returns_null_without_version()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-mod-docs2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            using var http = new HttpClient(new StubHttpHandler());
            var provider = new ThunderstoreModProvider(http, cache);
            var package = new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "banjo-recompiled",
                Id = "id",
                Owner = "Dario",
                Name = "Asset_Expansion_Pak",
                FullName = "Dario-Asset_Expansion_Pak",
                LatestVersion = null,
            };

            var markdown = await provider.GetReadmeAsync(package);
            markdown.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void ThunderstoreApiClient_builds_listing_url_with_query_nsfw_and_section()
    {
        var url = ThunderstoreApiClient.BuildListingUrl(
            "banjo-recompiled",
            page: 2,
            query: "token tracker",
            includeNsfw: true,
            sectionUuid: "019bfe52-aaaa-bbbb-cccc-ddddeeeeffff",
            ordering: "most-downloaded");

        url.Should().StartWith("https://thunderstore.io/api/cyberstorm/listing/banjo-recompiled/?page=2");
        url.Should().Contain("ordering=most-downloaded");
        url.Should().Contain("nsfw=true");
        url.Should().Contain("q=token%20tracker");
        url.Should().Contain("section=019bfe52-aaaa-bbbb-cccc-ddddeeeeffff");
    }

    [Fact]
    public void ThunderstoreApiClient_selects_mods_section_uuid()
    {
        var filters = new List<ThunderstoreCommunityFilterDto>
        {
            new() { Uuid = "modpacks-uuid", Name = "Modpacks", Slug = "modpacks" },
            new() { Uuid = "mods-uuid", Name = "Mods", Slug = "mods" },
        };

        ThunderstoreApiClient.SelectModsSectionUuid(filters).Should().Be("mods-uuid");
        ThunderstoreApiClient.SelectModsSectionUuid([]).Should().BeNull();
        ThunderstoreApiClient.SelectModsSectionUuid(null).Should().BeNull();
    }

    [Fact]
    public void ThunderstoreApiClient_parses_next_page_from_url()
    {
        ThunderstoreApiClient.TryParseNextPage(
                "https://thunderstore.io/api/cyberstorm/listing/banjo-recompiled/?page=3&ordering=last-updated")
            .Should().Be(3);
        ThunderstoreApiClient.TryParseNextPage(null).Should().BeNull();
        ThunderstoreApiClient.TryParseNextPage("").Should().BeNull();
    }

    [Fact]
    public void ThunderstoreModProvider_maps_cyberstorm_listing_row()
    {
        var source = new ModSourceRef
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo-recompiled",
            DisplayLabel = "Thunderstore · banjo-recompiled",
            SourceUrl = "https://thunderstore.io/c/banjo-recompiled/",
        };

        var dto = new ThunderstoreListingPackageDto
        {
            Name = "Mumbo_Token_Tracker",
            Namespace = "Cloudy",
            Description = "Tracks tokens",
            DownloadCount = 801,
            RatingCount = 1,
            IsNsfw = true,
            IsDeprecated = false,
            Size = 163588,
            IconUrl = "https://gcdn.thunderstore.io/live/repository/icons/Cloudy-Mumbo_Token_Tracker-1.1.1.png",
            LastUpdated = DateTimeOffset.Parse("2026-07-27T21:52:22Z"),
            DateTimeCreated = DateTimeOffset.Parse("2026-06-20T20:48:31Z"),
        };

        var package = ThunderstoreModProvider.MapListingPackage(dto, source);
        package.Should().NotBeNull();
        package!.Id.Should().Be("Cloudy-Mumbo_Token_Tracker");
        package.FullName.Should().Be("Cloudy-Mumbo_Token_Tracker");
        package.Owner.Should().Be("Cloudy");
        package.DownloadCount.Should().Be(801);
        package.RatingScore.Should().Be(1);
        package.HasContentRating.Should().BeTrue();
        package.LatestVersion!.Version.Should().Be("1.1.1");
        package.LatestVersion.DownloadUrl.Should().BeEmpty();
        package.UpdatedAtUnix.Should().Be(dto.LastUpdated!.Value.ToUnixTimeSeconds());
        package.CreatedAtUnix.Should().Be(dto.DateTimeCreated!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task ThunderstoreModProvider_search_and_enrich_use_cyberstorm_and_experimental()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-ts-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            var listing = """
                          {"count":1,"next":null,"previous":null,"results":[{
                            "name":"Mumbo_Token_Tracker","namespace":"Cloudy","description":"Tracks",
                            "download_count":10,"rating_count":2,"is_nsfw":false,"is_deprecated":false,"size":100,
                            "icon_url":"https://gcdn.thunderstore.io/live/repository/icons/Cloudy-Mumbo_Token_Tracker-1.0.0.png",
                            "last_updated":"2026-07-27T21:52:22Z"
                          }]}
                          """;
            var filters = """
                          {"package_categories":[],"sections":[
                            {"uuid":"mods-section-uuid","name":"Mods","slug":"mods","priority":0},
                            {"uuid":"packs-uuid","name":"Modpacks","slug":"modpacks","priority":-1}
                          ]}
                          """;
            var experimental = """
                               {"namespace":"Cloudy","name":"Mumbo_Token_Tracker","full_name":"Cloudy-Mumbo_Token_Tracker",
                                "owner":"Cloudy","package_url":"https://thunderstore.io/c/banjo-recompiled/p/Cloudy/Mumbo_Token_Tracker/",
                                "is_deprecated":false,
                                "latest":{"version_number":"1.1.1","description":"Tracks",
                                  "icon":"https://example.com/icon.png","dependencies":["Lib-A-1.0.0"],
                                  "download_url":"https://thunderstore.io/package/download/Cloudy/Mumbo_Token_Tracker/1.1.1/",
                                  "downloads":36,"is_active":true}}
                               """;

            var handler = new StubHttpHandler();
            handler.Responses[
                "https://thunderstore.io/api/cyberstorm/community/banjo-recompiled/filters/"] =
                (HttpStatusCode.OK, filters);
            handler.Responses[
                "https://thunderstore.io/api/cyberstorm/listing/banjo-recompiled/?page=1&ordering=last-updated&nsfw=false&deprecated=false&q=token&section=mods-section-uuid"] =
                (HttpStatusCode.OK, listing);
            handler.Responses[
                "https://thunderstore.io/api/experimental/package/Cloudy/Mumbo_Token_Tracker/"] =
                (HttpStatusCode.OK, experimental);

            using var http = new HttpClient(handler);
            var provider = new ThunderstoreModProvider(http, cache);
            provider.TryParseSource("banjo-recompiled", out var source).Should().BeTrue();

            var page = await provider.SearchPackagesPageAsync(source, "token", null, 20);
            page.Packages.Should().HaveCount(1);
            page.Packages[0].Name.Should().Be("Mumbo_Token_Tracker");
            page.Packages[0].LatestVersion!.DownloadUrl.Should().BeEmpty();

            var enriched = await provider.EnrichForInstallAsync(page.Packages[0]);
            enriched.LatestVersion!.Version.Should().Be("1.1.1");
            enriched.LatestVersion.DownloadUrl.Should()
                .Be("https://thunderstore.io/package/download/Cloudy/Mumbo_Token_Tracker/1.1.1/");
            enriched.LatestVersion.Dependencies.Should().ContainSingle("Lib-A-1.0.0");
            // Listing community URL must win over experimental /package/... (which redirects to riskofrain2).
            enriched.PackagePageUrl.Should()
                .Be("https://thunderstore.io/c/banjo-recompiled/p/Cloudy/Mumbo_Token_Tracker/");
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void ThunderstoreModProvider_ResolvePackagePageUrl_prefers_community_over_experimental()
    {
        var community = "https://thunderstore.io/c/bomberman-64-recompiled/p/Litronom/PlayAs/";
        var experimental = "https://thunderstore.io/package/Litronom/PlayAs/";

        ThunderstoreModProvider.ResolvePackagePageUrl(
                community,
                "bomberman-64-recompiled",
                "Litronom",
                "PlayAs",
                experimental)
            .Should().Be(community);

        ThunderstoreModProvider.ResolvePackagePageUrl(
                experimental,
                "bomberman-64-recompiled",
                "Litronom",
                "PlayAs",
                experimental)
            .Should().Be(community);

        ThunderstoreModProvider.ResolvePackagePageUrl(
                null,
                "bomberman-64-recompiled",
                "Litronom",
                "PlayAs",
                experimental)
            .Should().Be(community);

        ThunderstoreModProvider.ResolvePackagePageUrl(
                null,
                sourceKey: null,
                "Litronom",
                "PlayAs",
                experimental)
            .Should().Be(experimental);
    }

    [Fact]
    public async Task ThunderstoreModProvider_EnrichForInstallAsync_keeps_community_page_url()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-ts-pageurl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            var experimental = """
                               {"namespace":"Litronom","name":"PlayAs","full_name":"Litronom-PlayAs",
                                "owner":"Litronom","package_url":"https://thunderstore.io/package/Litronom/PlayAs/",
                                "is_deprecated":false,
                                "latest":{"version_number":"2.0.0","description":"Play as",
                                  "icon":"https://example.com/icon.png","dependencies":[],
                                  "download_url":"https://thunderstore.io/package/download/Litronom/PlayAs/2.0.0/",
                                  "downloads":183,"is_active":true}}
                               """;

            var handler = new StubHttpHandler();
            handler.Responses[
                "https://thunderstore.io/api/experimental/package/Litronom/PlayAs/"] =
                (HttpStatusCode.OK, experimental);

            using var http = new HttpClient(handler);
            var provider = new ThunderstoreModProvider(http, cache);

            var listingPackage = new ModPackage
            {
                ProviderId = ModProviderIds.Thunderstore,
                SourceKey = "bomberman-64-recompiled",
                SourceDisplayLabel = "Thunderstore · bomberman-64-recompiled",
                Id = "Litronom-PlayAs",
                Owner = "Litronom",
                Name = "PlayAs",
                FullName = "Litronom-PlayAs",
                PackagePageUrl = "https://thunderstore.io/c/bomberman-64-recompiled/p/Litronom/PlayAs/",
                LatestVersion = new ModPackageVersion
                {
                    Version = string.Empty,
                    DownloadUrl = string.Empty,
                },
            };

            var enriched = await provider.EnrichForInstallAsync(listingPackage);
            enriched.PackagePageUrl.Should()
                .Be("https://thunderstore.io/c/bomberman-64-recompiled/p/Litronom/PlayAs/");
            enriched.LatestVersion!.DownloadUrl.Should()
                .Be("https://thunderstore.io/package/download/Litronom/PlayAs/2.0.0/");
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task GameBananaModProvider_SearchPackagesPageAsync_keeps_mods_only()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-gb-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            var body = """
                       {"_aMetadata":{"_nRecordCount":3,"_bIsComplete":true,"_nPerpage":30},"_aRecords":[
                         {"_idRow":1,"_sModelName":"Mod","_sName":"Cool Mod","_sPayType":"free","_sVersion":"1","_bHasContentRatings":false,"_aSubmitter":{"_sName":"A"}},
                         {"_idRow":2,"_sModelName":"Question","_sName":"How?","_sPayType":"free","_sVersion":"1","_bHasContentRatings":false,"_aSubmitter":{"_sName":"B"}},
                         {"_idRow":3,"_sModelName":"Request","_sName":"Please","_sPayType":"free","_sVersion":"1","_bHasContentRatings":false,"_aSubmitter":{"_sName":"C"}}
                       ]}
                       """;

            var handler = new StubHttpHandler();
            handler.Responses[
                "https://gamebanana.com/apiv13/Util/Search/Results?_sModelName=Mod&_sOrder=udate&_idGameRow=24774&_sSearchString=cool&_csvFields=name,description,owner,credits&_nPerpage=30&_nPage=1"] =
                (HttpStatusCode.OK, body);

            using var http = new HttpClient(handler);
            var provider = new GameBananaModProvider(http, cache);
            provider.TryParseSource("24774", out var source).Should().BeTrue();

            var page = await provider.SearchPackagesPageAsync(source, "cool", null, 30);
            page.Packages.Should().ContainSingle();
            page.Packages[0].Name.Should().Be("Cool Mod");
            page.Packages[0].Id.Should().Be("1");
            page.NextPageToken.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task ModCatalogLoader_merges_remote_search_pages_from_multiple_sources()
    {
        var ts = new FakeRemoteSearchProvider(ModProviderIds.Thunderstore, "ts",
            new Dictionary<string, ModPackagePage>
            {
                ["q:token|"] = new()
                {
                    Packages =
                    [
                        MakePkg(ModProviderIds.Thunderstore, "ts", "Cloudy-Token", "Cloudy", "Token"),
                    ],
                    NextPageToken = "2",
                    TotalCount = 2,
                },
                ["q:token|2"] = new()
                {
                    Packages =
                    [
                        MakePkg(ModProviderIds.Thunderstore, "ts", "Cloudy-Token2", "Cloudy", "Token2"),
                    ],
                    NextPageToken = null,
                    TotalCount = 2,
                },
            });

        var gb = new FakeRemoteSearchProvider(ModProviderIds.GameBanana, "24774",
            new Dictionary<string, ModPackagePage>
            {
                ["q:token|"] = new()
                {
                    Packages =
                    [
                        MakePkg(ModProviderIds.GameBanana, "24774", "99", "Author", "Token Pack"),
                    ],
                    NextPageToken = null,
                },
            });

        var loader = new ModCatalogLoader(new ModProviderRegistry([ts, gb]));
        var sources = new[]
        {
            new GameModSource { Provider = ModProviderIds.Thunderstore, SourceUrl = "ts" },
            new GameModSource { Provider = ModProviderIds.GameBanana, SourceUrl = "24774" },
        };

        var session = await loader.LoadSearchSessionAsync(sources, sourceFilterKey: null, "token", pageSize: 10);
        session.IsSearch.Should().BeTrue();
        session.Packages.Should().HaveCount(2);
        session.Packages.Select(p => p.ProviderId).Should().BeEquivalentTo(
            [ModProviderIds.Thunderstore, ModProviderIds.GameBanana]);
        session.CanLoadMore.Should().BeTrue();

        var more = await loader.LoadMoreSearchSessionAsync(session, pageSize: 10);
        more.Packages.Should().HaveCount(3);
        more.CanLoadMore.Should().BeFalse();
        more.Packages.Select(p => p.Name).Should().Contain(["Token", "Token2", "Token Pack"]);
    }

    private static ModPackage MakePkg(string provider, string sourceKey, string id, string owner, string name) =>
        new()
        {
            ProviderId = provider,
            SourceKey = sourceKey,
            SourceDisplayLabel = $"{provider} · {sourceKey}",
            Id = id,
            Owner = owner,
            Name = name,
            FullName = $"{owner}-{name}",
            LatestVersion = new ModPackageVersion { Version = "1.0.0", DownloadUrl = string.Empty },
        };

    private static ModPackage MakeThunderstorePackage(string owner, string name, string version) =>
        new()
        {
            ProviderId = ModProviderIds.Thunderstore,
            SourceKey = "banjo-recompiled",
            SourceDisplayLabel = "Thunderstore · banjo-recompiled",
            Id = $"{owner}-{name}",
            Owner = owner,
            Name = name,
            FullName = $"{owner}-{name}",
            LatestVersion = new ModPackageVersion { Version = version, DownloadUrl = string.Empty },
        };

    private static ModPackage MakeMultiFileGbPackage() =>
        new()
        {
            ProviderId = ModProviderIds.GameBanana,
            SourceKey = "20371",
            SourceDisplayLabel = "GameBanana · 20371",
            Id = "12345",
            Owner = "Author",
            Name = "ScenePack",
            FullName = "Author-ScenePack",
            DownloadFiles =
            [
                new ModDownloadFile
                {
                    Id = "10",
                    FileName = "scene.zip",
                    DownloadUrl = "https://example.com/a.zip",
                    FileSize = 10,
                    Version = "1.0",
                    Description = "Scene pack",
                },
                new ModDownloadFile
                {
                    Id = "20",
                    FileName = "art.zip",
                    DownloadUrl = "https://example.com/b.zip",
                    FileSize = 20,
                    Version = "1.0",
                    Description = "Art pack",
                },
            ],
            LatestVersion = new ModPackageVersion
            {
                Version = "1.0",
                DownloadUrl = "https://example.com/a.zip",
                FileSize = 10,
            },
        };

    /// <summary>Page v2.0 with mixed per-file versions (GameBanana mod 650008 shape).</summary>
    private static ModPackage MakeGbPageVsFileVersionPackage() =>
        new()
        {
            ProviderId = ModProviderIds.GameBanana,
            SourceKey = "20371",
            SourceDisplayLabel = "GameBanana · 20371",
            Id = "650008",
            Owner = "Author",
            Name = "3DS Link",
            FullName = "Author-3DSLink",
            DownloadFiles =
            [
                new ModDownloadFile
                {
                    Id = "10",
                    FileName = "standard.zip",
                    DownloadUrl = "https://example.com/standard.zip",
                    FileSize = 10,
                    Version = "2.0",
                    Description = "Standard",
                },
                new ModDownloadFile
                {
                    Id = "20",
                    FileName = "tunic.zip",
                    DownloadUrl = "https://example.com/tunic.zip",
                    FileSize = 20,
                    Version = "2.0",
                    Description = "Tunic",
                },
                new ModDownloadFile
                {
                    Id = "30",
                    FileName = "fiercedeity.zip",
                    DownloadUrl = "https://example.com/fiercedeity.zip",
                    FileSize = 30,
                    Version = "1.0",
                    Description = "Fierce Deity",
                },
            ],
            LatestVersion = new ModPackageVersion
            {
                Version = "2.0",
                DownloadUrl = "https://example.com/standard.zip",
                FileSize = 10,
            },
        };

    private sealed class FakeZipModProvider : IModProvider
    {
        private readonly Dictionary<string, byte[]> _zipsByUrl;

        public FakeZipModProvider(string id, Dictionary<string, byte[]> zipsByUrl)
        {
            Id = id;
            _zipsByUrl = zipsByUrl;
        }

        public string Id { get; }
        public string DisplayName => Id;
        public bool SupportsPagedListing => false;
        public bool SupportsRemoteSearch => false;

        public void ForceRefreshOnNextList() { }

        public bool TryParseSource(string sourceUrl, out ModSourceRef source)
        {
            source = new ModSourceRef
            {
                ProviderId = Id,
                SourceKey = sourceUrl,
                DisplayLabel = DisplayName,
                SourceUrl = sourceUrl,
            };
            return true;
        }

        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(
            ModSourceRef source,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModPackage>>([]);

        public Task<ModPackagePage> ListPackagesPageAsync(
            ModSourceRef source,
            string? pageToken,
            int pageSize,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModPackagePage { Packages = [], NextPageToken = null });

        public Task<ModPackagePage> SearchPackagesPageAsync(
            ModSourceRef source,
            string query,
            string? pageToken,
            int pageSize,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> DownloadAsync(
            ModPackageVersion version,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_zipsByUrl.TryGetValue(version.DownloadUrl, out var bytes))
                throw new InvalidOperationException($"No zip stub for '{version.DownloadUrl}'.");
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();

        public Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakePagedBrowseProvider : IModProvider
    {
        private readonly Dictionary<string, ModPackagePage> _pages;

        public FakePagedBrowseProvider(string id, string sourceKey, Dictionary<string, ModPackagePage> pages)
        {
            Id = id;
            SourceKey = sourceKey;
            _pages = pages;
        }

        public string Id { get; }
        public string DisplayName => Id;
        public string SourceKey { get; }
        public bool SupportsPagedListing => true;
        public bool SupportsRemoteSearch => false;

        public void ForceRefreshOnNextList() { }

        public bool TryParseSource(string sourceUrl, out ModSourceRef source)
        {
            source = new ModSourceRef
            {
                ProviderId = Id,
                SourceKey = SourceKey,
                DisplayLabel = $"{DisplayName} · {SourceKey}",
                SourceUrl = sourceUrl,
            };
            return string.Equals(sourceUrl, SourceKey, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(
            ModSourceRef source,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModPackage>>([]);

        public Task<ModPackagePage> ListPackagesPageAsync(
            ModSourceRef source,
            string? pageToken,
            int pageSize,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var key = pageToken ?? "";
            return Task.FromResult(_pages.TryGetValue(key, out var page)
                ? page
                : new ModPackagePage { Packages = [], NextPageToken = null });
        }

        public Task<ModPackagePage> SearchPackagesPageAsync(
            ModSourceRef source,
            string query,
            string? pageToken,
            int pageSize,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModPackagePage { Packages = [], NextPageToken = null });

        public Task<Stream> DownloadAsync(
            ModPackageVersion version,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();

        public Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeRemoteSearchProvider : IModProvider
    {
        private readonly Dictionary<string, ModPackagePage> _pages;

        public FakeRemoteSearchProvider(string id, string sourceKey, Dictionary<string, ModPackagePage> pages)
        {
            Id = id;
            SourceKey = sourceKey;
            _pages = pages;
        }

        public string Id { get; }
        public string DisplayName => Id;
        public string SourceKey { get; }
        public bool SupportsPagedListing => true;
        public bool SupportsRemoteSearch => true;

        public void ForceRefreshOnNextList() { }

        public bool TryParseSource(string sourceUrl, out ModSourceRef source)
        {
            source = new ModSourceRef
            {
                ProviderId = Id,
                SourceKey = SourceKey,
                DisplayLabel = $"{DisplayName} · {SourceKey}",
                SourceUrl = sourceUrl,
            };
            return string.Equals(sourceUrl, SourceKey, StringComparison.OrdinalIgnoreCase) ||
                   sourceUrl.Contains(SourceKey, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(
            ModSourceRef source,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModPackage>>([]);

        public Task<ModPackagePage> ListPackagesPageAsync(
            ModSourceRef source,
            string? pageToken,
            int pageSize,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModPackagePage { Packages = [], NextPageToken = null });

        public Task<ModPackagePage> SearchPackagesPageAsync(
            ModSourceRef source,
            string query,
            string? pageToken,
            int pageSize,
            ModListOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var key = $"q:{query.Trim()}|{pageToken ?? ""}";
            return Task.FromResult(_pages.TryGetValue(key, out var page)
                ? page
                : new ModPackagePage { Packages = [], NextPageToken = null });
        }

        public Task<Stream> DownloadAsync(
            ModPackageVersion version,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();

        public Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private static void AddZipEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] CreateMinimalModZip(string entryName, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            AddZipEntry(zip, entryName, content);
        return ms.ToArray();
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, byte[]> BinaryResponses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RequestedUrls { get; } = [];
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var url = request.RequestUri?.ToString() ?? string.Empty;
            RequestedUrls.Add(url);
            if (BinaryResponses.TryGetValue(url, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                });
            }

            if (Responses.TryGetValue(url, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(response.Status)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
