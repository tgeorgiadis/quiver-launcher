using System.Net;
using System.Text.Json;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogPlatformIndexTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "quiver-platform-index-" + Guid.NewGuid());
    public CatalogPlatformIndexTests() { Directory.CreateDirectory(_directory); GitHubApiCache.Initialize(_directory); }
    public void Dispose() => TestFixtures.CleanupDirectory(_directory);
    private const string Repo = "Rainchus/Donkey-Kong-64-Recompiled";
    private static GitHubRelease Dk64() => new() { tag_name = "1.0.2", assets = new[] {
        "DK64Recompiled-Windows-Release-1-0-2.zip", "DK64Recompiled-Linux-X64-Release-1-0-2.zip",
        "DK64Recompiled-Linux-ARM64-Release-1-0-2.zip", "DK64Recompiled-macOS-ARM64-Release-1-0-2.zip",
        "DK64Recompiled-Flatpak-X64-Release-1-0-2.zip" }.Select(n => new GitHubAsset { name = n }).ToArray() };
    private static CatalogSyncRowItem Row(string repository = Repo, string? pin = null) => new() {
        Repository = repository, External = new GameInfo { Repository = repository, PreferredVersion = pin, Name = "DK64", FolderName = "DK64" } };

    [Fact]
    public async Task Dk64_missing_failed_retry_verified_and_reload()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ => ++calls == 1 ? new(HttpStatusCode.ServiceUnavailable) : Ok(Dk64())));
        var rows = new[] { Row() };
        CatalogPlatformSupport.AppMatches("github", Repo, null, ["Windows"]).Should().BeFalse();
        await CatalogReleaseIndexWarmup.WarmAsync(client, rows, null, CancellationToken.None);
        var failed = CatalogReleaseIndexWarmup.Snapshot(client, CatalogReleaseIndexWarmup.CollectTargets(rows));
        failed.Completed.Should().Be(0); failed.Unresolved.Should().Be(1); failed.Outcome.Should().Be(CatalogReleaseWarmupOutcome.Failed);
        await CatalogReleaseIndexWarmup.WarmAsync(client, rows, null, CancellationToken.None);
        CatalogPlatformSupport.AppMatches("github", Repo.ToLowerInvariant(), null, ["Windows"]).Should().BeTrue();
        CatalogPlatformSupport.AppMatches("github", Repo, null, ["Android"]).Should().BeFalse();
        CatalogPlatformIndex.Initialize(_directory);
        CatalogPlatformSupport.AppMatches("github", Repo, null, ["Windows"]).Should().BeTrue();
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assetless_latest_uses_same_fallback_as_install_selection(bool prerelease)
    {
        var empty = new GitHubRelease { tag_name = "0.2.0-beta.2", assets = [] };
        var fallback = new GitHubRelease { tag_name = "fixture-older", prerelease = prerelease,
            assets = [new GitHubAsset { name = "SyphonFilterPC-win64.zip" }] };
        var releases = new[] { empty, fallback };
        using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/latest")
            ? Ok(empty) : new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases)) }));
        var row = Row("Madxbio97/SF-pc-port");
        await CatalogReleaseIndexWarmup.WarmAsync(client, [row], null, CancellationToken.None);
        CatalogPlatformIndex.TryGet("github", row.Repository, null, null, out var entry).Should().BeTrue();
        entry!.ReleaseTag.Should().Be(ReleaseSelection.SelectLatestRelease(releases, githubLatestTag: empty.tag_name)!.tag_name);
        CatalogPlatformSupport.AppMatches("github", row.Repository, null, ["Windows"]).Should().BeTrue();
    }

    [Fact]
    public void Old_empty_index_is_rechecked_without_invalidating_successful_assets()
    {
        File.WriteAllText(Path.Combine(_directory, "catalog_platform_index_v1.json"), JsonSerializer.Serialize(
            new Dictionary<string, CatalogPlatformEntry> {
                [CatalogPlatformIndex.Key("github", "test/empty")] = new("empty", [], DateTimeOffset.UtcNow),
                [CatalogPlatformIndex.Key("github", Repo)] = new("1.0.2", ["app-Windows.zip"], DateTimeOffset.UtcNow) }));
        CatalogPlatformIndex.Initialize(_directory);
        CatalogPlatformIndex.IsFresh("github", "test/empty").Should().BeFalse();
        CatalogPlatformIndex.IsFresh("github", Repo).Should().BeTrue();
        CatalogPlatformIndex.Set("github", "test/empty", null, null, new() { assets = [] });
        CatalogPlatformIndex.IsFresh("github", "test/empty").Should().BeTrue();
    }

    [Fact]
    public void Contexts_and_library_updates_do_not_overwrite_platform_index()
    {
        CatalogPlatformIndex.Set("github", Repo, "1", "token-a", Dk64());
        CatalogPlatformIndex.Set("github", Repo, "2", "token-a", new() { tag_name = "2", assets = [] });
        CatalogPlatformIndex.TryGet("github", Repo.ToLowerInvariant(), "1", "token-a", out var one).Should().BeTrue();
        CatalogPlatformIndex.TryGet("github", Repo, "2", "token-a", out var two).Should().BeTrue();
        one!.AssetNames.Should().NotBeEmpty(); two!.AssetNames.Should().BeEmpty();
        CatalogPlatformIndex.TryGet("github", Repo, "1", "token-b", out _).Should().BeFalse();
        CatalogPlatformIndex.TryGet("github", Repo, null, "token-a", out _).Should().BeFalse();
        CatalogPlatformIndex.Set("github", Repo, null, null, Dk64());
        GitHubApiCache.SetCache("github", Repo, "different", "", new() { assets = [] });
        CatalogPlatformSupport.AppMatches("github", Repo, null, ["Windows"]).Should().BeTrue();
        CatalogPlatformIndex.Set("gitlab", "Owner/App", null, null, Dk64());
        CatalogPlatformIndex.TryGet("gitlab", "owner/app", null, null, out _).Should().BeFalse();
    }

    [Fact]
    public void Legacy_metadata_is_a_frozen_stale_unpinned_fallback()
    {
        GitHubApiCache.SetCache("github", Repo, "1.0.2", "legacy", Dk64());
        CatalogPlatformIndex.Initialize(_directory);
        CatalogPlatformIndex.TryGet("github", Repo, null, null, out _).Should().BeTrue();
        CatalogPlatformIndex.IsFresh("github", Repo).Should().BeFalse();
        CatalogPlatformIndex.TryGet("github", Repo, "1.0.2", null, out _).Should().BeFalse();
        GitHubApiCache.SetCache("github", Repo, "other", "", new() { assets = [] });
        CatalogPlatformSupport.AppMatches("github", Repo, null, ["Windows"]).Should().BeTrue();
    }

    [Fact]
    public async Task Explicit_refresh_revalidates_fresh_metadata_with_304()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(request => {
            if (++calls == 1) { var response = Ok(Dk64()); response.Headers.ETag = new("\"dk64\""); return response; }
            request.Headers.IfNoneMatch.Should().ContainSingle().Which.Tag.Should().Be("\"dk64\"");
            return new(HttpStatusCode.NotModified);
        }));
        await CatalogReleaseIndexWarmup.WarmAsync(client, [Row()], null, CancellationToken.None);
        CatalogPlatformIndex.TryGet("github", Repo, null, null, out var before);
        await CatalogReleaseIndexWarmup.WarmAsync(client, [Row()], null, CancellationToken.None);
        calls.Should().Be(1);
        await CatalogReleaseIndexWarmup.WarmAsync(client, [Row()], null, CancellationToken.None, forceRefresh: true);
        calls.Should().Be(2);
        CatalogPlatformIndex.TryGet("github", Repo, null, null, out var after);
        after!.ValidatedAt.Should().BeOnOrAfter(before!.ValidatedAt);
        after.AssetNames.Should().Equal(before.AssetNames);
    }

    [Fact]
    public void Grouping_includes_release_and_credentials_but_normalizes_github_casing()
    {
        var a = Row(Repo, "1"); var b = Row(Repo.ToLowerInvariant(), "1"); var c = Row(Repo, "2");
        CatalogReleaseIndexWarmup.CollectTargets([a, b, c], _ => "one").Should().HaveCount(2);
        CatalogReleaseIndexWarmup.CollectTargets([a, b], game => ReferenceEquals(game, a.External) ? "one" : "two").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("game-aarch64.zip")]
    [InlineData("game-arm64.zip")]
    public void Architecture_alone_is_not_android(string asset) => PlatformAssetMatcher.MatchesPlatform(asset, "Android").Should().BeFalse();

    private static HttpResponseMessage Ok(GitHubRelease release) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release)) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
