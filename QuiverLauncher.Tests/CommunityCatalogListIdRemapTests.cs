using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CommunityCatalogListIdRemapTests
{
    [Fact]
    public void RemapLegacySourceIds_rewrites_old_n64_lists_onto_platform_id()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
            Name = "N64 Recomps",
            IsCommunityManaged = true,
            Enabled = true,
        });
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "e7b1f5d4-6c8a-4e2b-9f0d-3a4b5c6d7e8f",
            Name = "N64 Decomps",
            IsCommunityManaged = true,
            Enabled = false,
            HiddenFromReviewRepositories = ["owner/hidden"],
        });

        var index = CommunityCatalogIndex.TryParse(
            """
            {
              "version": 3,
              "lists": [
                {
                  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                  "remoteLocation": "https://example.com/Nintendo-64.json"
                }
              ]
            }
            """);

        CommunityCatalogListIdRemap.RemapLegacySourceIds(settings, index).Should().BeTrue();

        settings.AppCatalogSources.Should().HaveCount(1);
        var source = settings.AppCatalogSources.Single();
        source.Id.Should().Be(CommunityCatalogListIdRemap.Nintendo64ListId);
        source.Enabled.Should().BeTrue();
        source.HiddenFromReviewRepositories.Should().Contain("owner/hidden");
    }

    [Fact]
    public void RemapLegacySourceIds_noops_when_new_id_missing_from_index()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Id = "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
            IsCommunityManaged = true,
        });

        var index = CommunityCatalogIndex.TryParse(
            """
            {
              "version": 2,
              "lists": [
                {
                  "id": "b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c",
                  "remoteLocation": "https://example.com/N64-Recomps.json"
                }
              ]
            }
            """);

        CommunityCatalogListIdRemap.RemapLegacySourceIds(settings, index).Should().BeFalse();
        settings.AppCatalogSources.Single().Id.Should().Be("b4e8c2a1-3f5d-4e9b-8c7a-1d2e3f4a5b6c");
    }
}
