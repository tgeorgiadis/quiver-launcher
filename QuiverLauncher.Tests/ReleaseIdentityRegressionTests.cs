using System.Net;
using System.Text.Json;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ReleaseIdentityRegressionTests
{
    internal const string Stable = "Version1.0.4";
    internal const string Beta = "Version1.0.5beta9";
    internal static GitHubRelease Release(string tag, bool prerelease = false) => new()
    {
        tag_name = tag, prerelease = prerelease,
        assets = [new() { name = $"DKR-R-{tag}-Windows-x64.zip", browser_download_url = $"https://example.test/{tag}.zip" }]
    };
    internal static GitHubRelease[] Releases => [Release(Beta, true), Release("Version1.0.5beta10", true), Release(Stable)];

    [Theory]
    [InlineData(Stable, Beta, false)]
    [InlineData(Beta, "Version1.0.5beta10", false)]
    [InlineData("1.0.5beta9", "1.0.5beta10", false)]
    [InlineData("1.0.5beta9", "1.0.5", false)]
    [InlineData("nightly", "stable", false)]
    [InlineData("version", "ersion", false)]
    [InlineData("1..2", "1.2", false)]
    [InlineData("1.2.3.4.5", "1.2.3.4", false)]
    [InlineData("99999999999999999", "0.0.0", false)]
    [InlineData("v1.0.5beta9", "1.0.5beta9", true)]
    [InlineData("v2", "2.0.0", true)]
    [InlineData("V1.2", "1.2.0", true)]
    [InlineData("1.2.3+build1", "1.2.3+build2", true)]
    public void Identity_never_uses_lossy_numeric_normalization(string first, string second, bool equivalent) =>
        ReleaseVersionIdentity.AreVersionsEquivalent(first, second).Should().Be(equivalent);

    [Fact]
    public void Github_latest_matches_stable_despite_prereleases_at_front() =>
        CatalogReleaseSelection.SelectLatestRelease(Releases, githubLatestTag: Stable)!.tag_name.Should().Be(Stable);

    [Fact]
    public void Exact_tag_precedes_equivalent_alias()
    {
        var exact = Release("1.2.0");
        CatalogReleaseSelection.SelectLatestRelease([Release("v1.2"), exact], githubLatestTag: exact.tag_name).Should().BeSameAs(exact);
        CatalogReleaseSelection.SelectLatestRelease([Release("v1.2"), exact], preferredVersion: exact.tag_name).Should().BeSameAs(exact);
    }

    [Theory]
    [InlineData(Beta)]
    [InlineData("Version1.0.5beta10")]
    public void Exact_prerelease_pin_wins(string pin) =>
        CatalogReleaseSelection.SelectLatestRelease(Releases, preferredVersion: pin, githubLatestTag: Stable)!.tag_name.Should().Be(pin);

    internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
    internal static HttpResponseMessage Metadata(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/latest")
            ? JsonSerializer.Serialize(Release(Stable)) : JsonSerializer.Serialize(Releases))
    };

    [Fact]
    public void Legacy_selected_cache_is_only_a_pending_hint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quiver-old-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var repository = "legacy/" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(Path.Combine(directory, "version_cache.json"), JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["github:" + repository] = new { Version = Beta, LastChecked = DateTime.UtcNow, LastUpdateCheck = DateTime.UtcNow, CachedRelease = Release(Beta, true) }
            }));
            GitHubApiCache.Initialize(directory);
            GitHubApiCache.TryGetCachedVersion("github", repository, out _).Should().BeFalse();
            GitHubApiCache.NeedsUpdateCheck("github", repository).Should().BeTrue();
            var app = new GameInfo { Repository = repository, FolderName = "game" };
            StartupVersionResolver.Apply(app).Should().BeFalse();
            app.LatestVersionLabel.Should().Be($"Latest: {Beta} (pending check)");
            app.GetLatestRelease().Should().BeNull();
            GitHubApiCache.SetCache("github", repository, Stable, "", Release(Stable));
            GitHubApiCache.TryGetCachedVersion("github", repository, out var current).Should().BeTrue();
            current!.SelectionRevision.Should().Be(GameVersionCache.CurrentSelectionRevision);
            var saved = JsonSerializer.Deserialize<Dictionary<string, GameVersionCache>>(File.ReadAllText(Path.Combine(directory, "version_cache.json")))!;
            saved["github:" + repository].SelectionRevision.Should().Be(GameVersionCache.CurrentSelectionRevision);
        }
        finally { Directory.Delete(directory, true); }
    }
}
