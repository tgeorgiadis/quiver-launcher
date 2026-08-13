using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CommunityCatalogBootstrapTests
{
    private const string N64RemoteUrl =
        "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/community-app-catalog/N64-Recomps.json";

    [Fact]
    public void MigrateLegacyDefaultSource_removes_monolithic_default()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = CommunityCatalogDefaults.DefaultSourceId,
            Name = CommunityCatalogDefaults.DefaultSourceName,
            Location = CommunityCatalogDefaults.DefaultCatalogUrl,
        });

        CommunityCatalogBootstrap.MigrateLegacyDefaultSource(settings);

        settings.AppCatalogSources.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncCommunitySourcesFromIndexAsync_creates_sources_with_remote_location_as_location()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();

        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [CommunityCatalogDefaults.RemoteIndexUrl] = SampleRemoteIndex(),
        });
        var bootstrap = new CommunityCatalogBootstrap(reader);

        var result = await bootstrap.SyncCommunitySourcesFromIndexAsync(new HttpClient(), settings);

        result.IndexLoaded.Should().BeTrue();
        result.AddedSourceCount.Should().Be(2);
        settings.AppCatalogSources.Should().HaveCount(2);
        settings.AppCatalogSources.Should().OnlyContain(s => s.IsCommunityManaged);
        settings.AppCatalogSources.Single(s => s.Id == "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c")
            .Location.Should().Be(N64RemoteUrl);
        settings.AppCatalogSources.Single(s => s.Id == "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c")
            .RemoteLocation.Should().BeNull();
    }

    [Fact]
    public async Task SyncCommunitySourcesFromIndexAsync_adds_new_remote_list()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(CommunityCatalogBootstrap.CreateSourceFromEntry(new CommunityCatalogListEntry
        {
            Id = "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
            Name = "N64 Recomp Games",
            RemoteLocation = N64RemoteUrl,
        }, N64RemoteUrl));

        var remoteIndex =
            """
            {
              "version": 2,
              "lists": [
                {
                  "id": "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
                  "remoteLocation": "https://example.com/n64-recomp.json"
                },
                {
                  "id": "e7f1a5d4-6b8f-7a2e-0f0d-4a5b6c7d8e9f",
                  "remoteLocation": "https://example.com/general-game-recreations.json"
                }
              ]
            }
            """;

        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [CommunityCatalogDefaults.RemoteIndexUrl] = remoteIndex,
        });
        var bootstrap = new CommunityCatalogBootstrap(reader);

        var result = await bootstrap.SyncCommunitySourcesFromIndexAsync(new HttpClient(), settings);

        result.AddedSourceCount.Should().Be(1);
        result.AddedSourceNames.Should().Contain("general game recreations");
        settings.AppCatalogSources.Should().HaveCount(2);
        settings.AppCatalogSources.Single(s => s.Id == "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c")
            .Location.Should().Be("https://example.com/n64-recomp.json");
    }

    [Fact]
    public async Task SyncCommunitySourcesFromIndexAsync_returns_error_when_remote_index_unavailable()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();

        var bootstrap = new CommunityCatalogBootstrap(new FakeCatalogLocationReader([]));

        var result = await bootstrap.SyncCommunitySourcesFromIndexAsync(new HttpClient(), settings);

        result.IndexLoaded.Should().BeFalse();
        result.LastError.Should().NotBeNullOrWhiteSpace();
        settings.AppCatalogSources.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncCommunitySourcesFromIndexAsync_removes_community_source_missing_from_index()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
            Name = "N64 Recomps",
            Location = N64RemoteUrl,
            IsCommunityManaged = true,
        });
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "retired-list-id",
            Name = "N64 Decomps",
            Location = "https://example.com/n64-decomps.json",
            IsCommunityManaged = true,
            Enabled = true,
        });
        var userSource = new AppCatalogSource
        {
            Id = "user-list-id",
            Name = "My Custom List",
            Location = "https://example.com/custom.json",
            IsCommunityManaged = false,
        };
        settings.AppCatalogSources.Add(userSource);

        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [CommunityCatalogDefaults.RemoteIndexUrl] = SampleRemoteIndex(),
        });
        var bootstrap = new CommunityCatalogBootstrap(reader);

        await bootstrap.SyncCommunitySourcesFromIndexAsync(new HttpClient(), settings);

        settings.AppCatalogSources.Should().Contain(s => s.Id == "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c");
        settings.AppCatalogSources.Should().Contain(s => s.Id == "e7f1a5d4-6b8f-7a2e-0f0d-4a5b6c7d8e9f");
        settings.AppCatalogSources.Should().NotContain(s => s.Id == "retired-list-id");
        settings.AppCatalogSources.Should().Contain(userSource);
    }

    [Fact]
    public async Task SyncCommunitySourcesFromIndexAsync_does_not_remove_sources_when_index_fails()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "retired-list-id",
            Name = "N64 Decomps",
            Location = "https://example.com/n64-decomps.json",
            IsCommunityManaged = true,
        });

        var bootstrap = new CommunityCatalogBootstrap(new FakeCatalogLocationReader([]));

        var result = await bootstrap.SyncCommunitySourcesFromIndexAsync(new HttpClient(), settings);

        result.IndexLoaded.Should().BeFalse();
        settings.AppCatalogSources.Should().ContainSingle(s => s.Id == "retired-list-id");
    }

    [Fact]
    public async Task SyncCommunitySourcesFromIndexAsync_does_not_modify_user_sources()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();

        var userSource = new AppCatalogSource
        {
            Id = Guid.NewGuid().ToString(),
            Name = "My Custom List",
            Location = "https://example.com/custom.json",
            IsCommunityManaged = false,
        };
        settings.AppCatalogSources.Add(userSource);

        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [CommunityCatalogDefaults.RemoteIndexUrl] = SampleRemoteIndex(),
        });
        var bootstrap = new CommunityCatalogBootstrap(reader);

        await bootstrap.SyncCommunitySourcesFromIndexAsync(new HttpClient(), settings);

        settings.AppCatalogSources.Should().Contain(userSource);
        userSource.Name.Should().Be("My Custom List");
    }

    [Fact]
    public async Task SyncCommunitySourcesFromIndexAsync_does_not_clear_existing_name_when_index_has_no_name()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
            Name = "N64 Recomps",
            Description = "N64 recompilation ports",
            Location = N64RemoteUrl,
            IsCommunityManaged = true,
        });

        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [CommunityCatalogDefaults.RemoteIndexUrl] = SampleRemoteIndex(),
        });
        var bootstrap = new CommunityCatalogBootstrap(reader);

        await bootstrap.SyncCommunitySourcesFromIndexAsync(new HttpClient(), settings);

        var source = settings.AppCatalogSources.Single(s => s.Id == "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c");
        source.Name.Should().Be("N64 Recomps");
        source.Description.Should().Be("N64 recompilation ports");
    }

    [Fact]
    public void DeriveListDisplayNameFromRemoteLocation_formats_filename_without_extension()
    {
        CommunityCatalogBootstrap.DeriveListDisplayNameFromRemoteLocation(N64RemoteUrl)
            .Should().Be("N64 Recomps");
        CommunityCatalogBootstrap.DeriveListDisplayNameFromRemoteLocation(
                "https://example.com/general-game-recreations.json")
            .Should().Be("general game recreations");
    }

    [Fact]
    public void MigrateBundledCommunitySourceLocation_moves_remote_url_to_location()
    {
        var source = new AppCatalogSource
        {
            Id = "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
            Name = "N64 Recomp Games",
            Location = "community-app-catalog/N64-Recomps.json",
            RemoteLocation = N64RemoteUrl,
            IsCommunityManaged = true,
        };

        CommunityCatalogBootstrap.MigrateBundledCommunitySourceLocation(source);

        source.Location.Should().Be(N64RemoteUrl);
        source.RemoteLocation.Should().BeNull();
    }

    [Fact]
    public async Task EnsureCommunitySourcesCachedAsync_fetches_lists_from_remote_urls()
    {
        var listJson = await File.ReadAllTextAsync(TestFixtures.N64RecompListPath);
        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [CommunityCatalogDefaults.RemoteIndexUrl] = SampleRemoteIndex(),
            [N64RemoteUrl] = listJson,
            ["https://example.com/general-game-recreations.json"] = listJson,
        });
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService(locationReader: reader);
        var settings = new AppSettings();
        settings.EnsureInitialized();

        try
        {
            await service.EnsureCommunitySourcesCachedAsync(new HttpClient(), settings);

            settings.AppCatalogSources.Should().HaveCount(2);
            foreach (var source in settings.AppCatalogSources)
            {
                service.HasSourceCache(source.Id).Should().BeTrue();
                source.CachedListVersion.Should().Be("1.0.5");
            }

            settings.AppCatalogSources.Single(s => s.Id == "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c")
                .Name.Should().Be("N64 Recomps");
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public void GetFetchLocationCandidates_uses_location_when_remote_location_absent()
    {
        var source = new AppCatalogSource
        {
            Location = "https://example.com/n64-recomp.json",
        };

        AppCatalogService.GetFetchLocationCandidates(source)
            .Should().Equal(["https://example.com/n64-recomp.json"]);
    }

    private static string SampleRemoteIndex() =>
        """
        {
          "version": 2,
          "lists": [
            {
              "id": "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
              "remoteLocation": "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/community-app-catalog/N64-Recomps.json"
            },
            {
              "id": "e7f1a5d4-6b8f-7a2e-0f0d-4a5b6c7d8e9f",
              "remoteLocation": "https://example.com/general-game-recreations.json"
            }
          ]
        }
        """;

    private sealed class FakeCatalogLocationReader(Dictionary<string, string> responses) : ICatalogLocationReader
    {
        public Task<string> ReadAsync(HttpClient httpClient, string location, CancellationToken cancellationToken = default)
        {
            if (responses.TryGetValue(location, out var json))
                return Task.FromResult(json);

            throw new InvalidOperationException($"No fake response for {location}");
        }
    }
}
