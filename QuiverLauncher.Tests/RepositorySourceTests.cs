using FluentAssertions;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using System.Text.Json;

namespace QuiverLauncher.Tests;

public class RepositorySourceTests
{
    [Theory]
    [InlineData(null, "github", false)]
    [InlineData("", "github", false)]
    [InlineData("github", "github", false)]
    [InlineData("GitHub", "github", false)]
    [InlineData("gitlab", "gitlab", false)]
    [InlineData("GitLab", "gitlab", false)]
    [InlineData("bitbucket", "github", true)]
    public void Normalize_handles_known_and_unknown_sources(string? input, string expected, bool unsupported)
    {
        var result = RepositorySourceHelper.Normalize(input, out var wasUnsupported);
        result.Should().Be(expected);
        wasUnsupported.Should().Be(unsupported);
    }

    [Fact]
    public void IdentityKey_defaults_missing_source_to_github()
    {
        RepositorySourceHelper.GetIdentityKey(null, "owner/app")
            .Should().Be("github:owner/app");
        RepositorySourceHelper.GetIdentityKey("gitlab", "group/project")
            .Should().Be("gitlab:group/project");
        RepositorySourceHelper.GetIdentityKey(null, null, "MyFolder")
            .Should().Be("manual:MyFolder");
        RepositorySourceHelper.GetInstanceKey(null, "owner/app", "FolderA")
            .Should().Be("github:owner/app:FolderA");
        RepositorySourceHelper.GetInstanceKey(null, "owner/app", "FolderB")
            .Should().Be("github:owner/app:FolderB");
        RepositorySourceHelper.GetInstanceKey(null, null, "MyFolder")
            .Should().Be("manual:MyFolder");
        RepositorySourceHelper.IsManuallyManaged(null).Should().BeTrue();
        RepositorySourceHelper.IsManuallyManaged("owner/app").Should().BeFalse();
    }

    [Fact]
    public async Task Parse_and_serialize_omits_github_and_round_trips_gitlab()
    {
        var dir = Path.Combine(Path.GetTempPath(), "QuiverRepoSource_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = new AppCatalogService(dataDirectory: dir);
            var apps = new List<GameInfo>
            {
                new()
                {
                    Name = "GitHub App",
                    Repository = "owner/github-app",
                    FolderName = "GitHubApp",
                },
                new()
                {
                    Name = "GitLab App",
                    Repository = "bighead.0/ladxhd_updated",
                    RepositorySource = "gitlab",
                    FolderName = "LADXHD",
                },
                new()
                {
                    Name = "Unknown Source App",
                    Repository = "owner/unknown",
                    RepositorySource = "bitbucket",
                    FolderName = "UnknownApp",
                },
            };

            await catalog.SaveLocalAppsAsync(apps);
            var json = await File.ReadAllTextAsync(Path.Combine(dir, "apps.json"));
            using var document = JsonDocument.Parse(json);
            var array = document.RootElement.GetProperty("apps");

            array[0].TryGetProperty("repositorySource", out _).Should().BeFalse();
            array[1].GetProperty("repositorySource").GetString().Should().Be("gitlab");

            var loaded = await catalog.LoadLocalAppsAsync();
            loaded.Should().ContainSingle(a => a.Repository == "owner/github-app" && a.RepositorySource == null);
            loaded.Should().ContainSingle(a =>
                a.Repository == "bighead.0/ladxhd_updated" &&
                a.EffectiveRepositorySource == "gitlab");
            loaded.Should().ContainSingle(a =>
                a.Repository == "owner/unknown" &&
                a.EffectiveRepositorySource == "github" &&
                a.RepositorySource == null);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Dedupe_allows_same_repository_on_different_sources()
    {
        var github = new GameInfo { Name = "GH", Repository = "owner/app", FolderName = "A" };
        var gitlab = new GameInfo
        {
            Name = "GL",
            Repository = "owner/app",
            RepositorySource = "gitlab",
            FolderName = "B",
        };

        github.IdentityKey.Should().NotBe(gitlab.IdentityKey);

        var rows = CatalogCompareService.BuildCompareRows(
            [github],
            [gitlab]);

        rows.Should().ContainSingle(r =>
            r.Repository == "owner/app" &&
            r.Status == CatalogSyncStatus.InExternalOnly &&
            r.External!.EffectiveRepositorySource == "gitlab");
    }

    [Fact]
    public void GitLabReleaseSource_maps_gitlab_hosted_links_and_drops_external()
    {
        const string json = """
        [
          {
            "tag_name": "v1.2.3",
            "upcoming_release": false,
            "assets": {
              "links": [
                {
                  "name": "app-win.zip",
                  "url": "https://cdn.example.com/app-win.zip",
                  "direct_asset_url": "https://gitlab.com/group/project/-/releases/v1.2.3/downloads/app-win.zip"
                },
                {
                  "name": "external-doc",
                  "url": "https://example.com/docs"
                },
                {
                  "name": "app-linux.AppImage",
                  "url": "https://gitlab.com/group/project/uploads/abc/app-linux.AppImage"
                }
              ]
            }
          }
        ]
        """;

        var releases = GitLabReleaseSource.MapReleasesFromJson(json);
        releases.Should().ContainSingle();
        releases[0].tag_name.Should().Be("v1.2.3");
        releases[0].assets.Select(a => a.name).Should().BeEquivalentTo("app-win.zip", "app-linux.AppImage");
        releases[0].assets.Should().OnlyContain(a =>
            GitLabReleaseSource.IsGitLabHostedUrl(a.browser_download_url));
        releases[0].assets.First(a => a.name == "app-win.zip").browser_download_url
            .Should().Contain("gitlab.com");
    }

    [Fact]
    public void GetCacheKey_uses_composite_source_and_repository()
    {
        GitHubApiCache.GetCacheKey(null, "owner/app").Should().Be("github:owner/app");
        GitHubApiCache.GetCacheKey("gitlab", "group/project").Should().Be("gitlab:group/project");
    }

    [Fact]
    public void IsGitLabHostedUrl_accepts_gitlab_hosts_only()
    {
        GitLabReleaseSource.IsGitLabHostedUrl("https://gitlab.com/a/b/-/releases/v1/downloads/x.zip")
            .Should().BeTrue();
        GitLabReleaseSource.IsGitLabHostedUrl("https://cdn.gitlab.com/file.zip").Should().BeTrue();
        GitLabReleaseSource.IsGitLabHostedUrl("https://example.com/file.zip").Should().BeFalse();
        GitLabReleaseSource.IsGitLabHostedUrl("not-a-url").Should().BeFalse();
    }

    [Fact]
    public void ReleaseSourceRegistry_defaults_unknown_to_github()
    {
        var registry = ReleaseSourceRegistry.Default;
        registry.Get(null).Id.Should().Be(RepositorySourceIds.GitHub);
        registry.Get("gitlab").Id.Should().Be(RepositorySourceIds.GitLab);
        registry.Get("gitea").Id.Should().Be(RepositorySourceIds.GitHub);
    }
}
