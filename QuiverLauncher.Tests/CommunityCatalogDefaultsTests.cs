using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CommunityCatalogDefaultsTests
{
    private const string N64RemoteUrl =
        "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/community-app-catalog/N64-Recomps.json";

    [Fact]
    public void FirstRunWelcome_copy_is_present()
    {
        CommunityCatalogDefaults.FirstRunWelcomeTitle.Should().Be("Welcome to Quiver Launcher");
        CommunityCatalogDefaults.FirstRunWelcomeMessage.Should().NotBeNullOrWhiteSpace();
        CommunityCatalogDefaults.FirstRunWelcomeMessage.Should().Contain("community app catalog lists");
        CommunityCatalogDefaults.FirstRunWelcomeMessage.Should().Contain("internet connection");
    }

    [Fact]
    public void IsLegacyDefaultSource_matches_default_source_id()
    {
        var source = new AppCatalogSource
        {
            Id = CommunityCatalogDefaults.DefaultSourceId,
            Name = CommunityCatalogDefaults.DefaultSourceName,
            Location = CommunityCatalogDefaults.DefaultCatalogUrl,
        };
        CommunityCatalogDefaults.IsLegacyDefaultSource(source).Should().BeTrue();
        CommunityCatalogDefaults.IsLegacyDefaultSource(new AppCatalogSource { Id = Guid.NewGuid().ToString() }).Should().BeFalse();
    }

    [Fact]
    public void GetFirstRunReviewSource_prefers_community_source_with_pending_review()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Clear();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Other",
            Enabled = true,
            PendingReviewCount = 5,
        });
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
            Name = "N64 Recomp Games",
            Enabled = true,
            IsCommunityManaged = true,
            PendingReviewCount = 2,
        });

        var source = CommunityCatalogDefaults.GetFirstRunReviewSource(settings);

        source.Should().NotBeNull();
        source!.Name.Should().Be("N64 Recomp Games");
    }

    [Fact]
    public void GetFirstRunReviewSource_falls_back_to_highest_pending_source()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Clear();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Low pending",
            Enabled = true,
            PendingReviewCount = 1,
        });
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = Guid.NewGuid().ToString(),
            Name = "High pending",
            Enabled = true,
            PendingReviewCount = 9,
        });

        CommunityCatalogDefaults.GetFirstRunReviewSource(settings)!.Name.Should().Be("High pending");
    }

    [Fact]
    public async Task EnsureCommunitySourcesCachedAsync_populates_cache_from_remote_lists()
    {
        var listJson = await File.ReadAllTextAsync(TestFixtures.N64RecompListPath);
        var reader = new FakeCatalogLocationReader(new Dictionary<string, string>
        {
            [CommunityCatalogDefaults.RemoteIndexUrl] =
                """
                {
                  "version": 2,
                  "lists": [
                    {
                      "id": "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
                      "remoteLocation": "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/community-app-catalog/N64-Recomps.json"
                    }
                  ]
                }
                """,
            [N64RemoteUrl] = listJson,
        });
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService(locationReader: reader);
        var settings = new AppSettings();
        settings.EnsureInitialized();

        try
        {
            await service.SaveLocalAppsAsync([]);
            await service.EnsureCommunitySourcesCachedAsync(new HttpClient(), settings);

            var source = settings.AppCatalogSources.Single();
            service.HasSourceCache(source.Id).Should().BeTrue();
            source.CachedListVersion.Should().Be("1.0.5");
            source.Name.Should().Be("N64 Recomps");
            source.Description.Should().Contain("N64 recompilation ports");
            await service.RefreshUpdateAvailableAsync(source);
            source.PendingReviewCount.Should().BeGreaterThan(0);
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

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
