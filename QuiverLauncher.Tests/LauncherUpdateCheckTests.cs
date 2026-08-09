using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherUpdateCheckTests
{
    private const string SampleReleaseJson =
        """
        {
          "tag_name": "v2.1.2",
          "assets": [
            {
              "name": "Quiver-Windows-x64.zip",
              "browser_download_url": "https://github.com/tgeorgiadis/quiver/releases/download/v2.1.2/Quiver-Windows-x64.zip"
            }
          ],
          "prerelease": false
        }
        """;

    [Fact]
    public void TryParseReleaseTagFromJson_extracts_tag_from_sample_github_json()
    {
        LauncherUpdateService.TryParseReleaseTagFromJson(SampleReleaseJson).Should().Be("v2.1.2");
    }

    [Fact]
    public void TryParseReleaseTagFromJson_returns_null_for_missing_tag()
    {
        LauncherUpdateService.TryParseReleaseTagFromJson("""{"assets":[]}""").Should().BeNull();
        LauncherUpdateService.TryParseReleaseTagFromJson("""{"tag_name":""}""").Should().BeNull();
        LauncherUpdateService.TryParseReleaseTagFromJson("").Should().BeNull();
    }

    [Fact]
    public void ParseReleaseFromJson_returns_release_when_tag_name_present()
    {
        GitHubRelease? release = LauncherUpdateService.ParseReleaseFromJson(SampleReleaseJson);

        release.Should().NotBeNull();
        release!.tag_name.Should().Be("v2.1.2");
        release.assets.Should().HaveCount(1);
        release.assets[0].name.Should().Be("Quiver-Windows-x64.zip");
    }

    [Theory]
    [InlineData(true, "W/\"abc\"", "v2.1.1", false)]
    [InlineData(false, null, "v2.1.1", false)]
    [InlineData(false, "", "v2.1.1", false)]
    [InlineData(false, "W/\"abc\"", null, false)]
    [InlineData(false, "W/\"abc\"", "", false)]
    [InlineData(false, "W/\"abc\"", "   ", false)]
    [InlineData(false, "W/\"abc\"", "v2.1.1", true)]
    public void ShouldSendConditionalRequest_requires_non_manual_valid_etag_and_last_known_version(
        bool isManualCheck,
        string? etag,
        string? lastKnownVersion,
        bool expected)
    {
        LauncherUpdateService.ShouldSendConditionalRequest(isManualCheck, etag, lastKnownVersion)
            .Should().Be(expected);
    }
}
