using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

/// <summary>
/// A release rebuilt under the installed tag (a nightly) is a new build: told by the installed file's checksum, or,
/// when the nightly names each build's file differently, by when GitHub had its files uploaded.
/// </summary>
public class InstalledBuildTests
{
    private static readonly DateTimeOffset LastNight = new(2026, 10, 17, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tonight = LastNight.AddDays(1);
    private static readonly string OldSha = new('a', 64), NewSha = new('b', 64);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_installed_files_checksum_decides_when_the_release_lists_it(bool rebuilt)
    {
        var build = new InstalledBuild("alpha-windows.zip", OldSha, LastNight);
        // Even an upload time saying otherwise doesn't overrule the checksum.
        var release = Release("nightly", Asset("alpha-windows.zip", rebuilt ? NewSha : OldSha, Tonight));

        InstalledBuild.IsRebuilt(build, LastNight, release, "nightly").Should().Be(rebuilt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nightly_naming_each_builds_file_differently_is_rebuilt_once_a_newer_file_is_uploaded(bool newer)
    {
        var build = new InstalledBuild("alpha-nightly-20261017-windows.zip", OldSha, LastNight);
        var release = Release("nightly", Asset("alpha-nightly-20261018-windows.zip", NewSha, newer ? Tonight : LastNight));

        InstalledBuild.IsRebuilt(build, Tonight.AddHours(5), release, "nightly").Should().Be(newer,
            "the upload times are GitHub's own, so the computer's clock (and when version.txt was written) doesn't matter");
    }

    [Fact]
    public void Renamed_build_of_the_installed_file_decides_over_the_releases_other_files()
    {
        var build = new InstalledBuild("alpha-nightly-20261017-windows.zip", OldSha, LastNight);
        // Tonight added a Linux build; the Windows one was only renamed, not rebuilt.
        var release = Release("nightly", Asset("alpha-nightly-20261018-windows.zip", OldSha, LastNight),
            Asset("alpha-nightly-20261018-linux.zip", NewSha, Tonight));

        InstalledBuild.IsRebuilt(build, null, release, "nightly").Should().BeFalse();
    }

    [Fact]
    public void Installed_file_gone_without_a_recognizable_successor_is_rebuilt_by_any_newer_upload()
    {
        var build = new InstalledBuild("alpha-build511-windows.zip", OldSha, LastNight);

        InstalledBuild.IsRebuilt(build, null, Release("nightly", Asset("alpha-build512-windows.zip", NewSha, Tonight)), "nightly")
            .Should().BeTrue();
        InstalledBuild.IsRebuilt(build, null, Release("nightly", Asset("alpha-build512-windows.zip", NewSha, LastNight)), "nightly")
            .Should().BeFalse();
    }

    [Fact]
    public void Build_recorded_without_its_upload_time_is_compared_with_when_it_was_installed()
    {
        var build = new InstalledBuild("alpha-nightly-20261017-windows.zip", OldSha);
        var release = Release("nightly", Asset("alpha-nightly-20261018-windows.zip", NewSha, Tonight));

        InstalledBuild.IsRebuilt(build, LastNight, release, "nightly").Should().BeTrue();
        InstalledBuild.IsRebuilt(build, Tonight.AddMinutes(1), release, "nightly").Should().BeFalse();
        InstalledBuild.IsRebuilt(build, null, release, "nightly").Should().BeFalse("nothing tells when it was installed");
    }

    [Fact]
    public void Installed_file_listed_without_a_checksum_is_rebuilt_when_it_was_uploaded_again()
    {
        var build = new InstalledBuild("alpha-windows.zip", OldSha, LastNight);

        InstalledBuild.IsRebuilt(build, null, Release("nightly", Asset("alpha-windows.zip", null, Tonight)), "nightly").Should().BeTrue();
        InstalledBuild.IsRebuilt(build, null, Release("nightly", Asset("alpha-windows.zip", null, LastNight)), "nightly").Should().BeFalse();
        InstalledBuild.IsRebuilt(build, null, Release("nightly", Asset("alpha-windows.zip", null, null)), "nightly").Should().BeFalse();
    }

    [Theory]
    [InlineData("nightly", true)]
    [InlineData("latest-nightly", true)]
    [InlineData("continuous", true)]
    // A file added to an ordinary release later (another platform's build) isn't a new build of the installed one.
    [InlineData("v1.2.0", false)]
    [InlineData("v3.5.1-rc.1", false)]
    [InlineData("r23", false)]
    [InlineData("nightly-2026-10-17", false)]
    public void Install_from_before_builds_were_recorded_is_rebuilt_by_a_newer_upload_only_under_a_rolling_tag(string tag, bool rebuilt)
    {
        var release = Release(tag, Asset("alpha-windows.zip", NewSha, Tonight));

        InstalledBuild.IsRebuilt(null, LastNight, release, tag).Should().Be(rebuilt);
        InstalledBuild.IsRebuilt(null, Tonight.AddMinutes(1), release, tag).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Catalog_release_known_to_be_rolling_with_the_installed_file_renamed_is_rebuilt(bool rolling, bool rebuilt)
    {
        // quiverlauncher.com lists files with checksums but no upload times.
        var build = new InstalledBuild("alpha-nightly-20261017-windows.zip", OldSha);
        var release = Release("nightly", Asset("alpha-nightly-20261018-windows.zip", NewSha, null));

        InstalledBuild.IsRebuilt(build, null, release, "nightly", rolling).Should().Be(rebuilt);
        InstalledBuild.IsRebuilt(build, null, Release("nightly"), "nightly", rolling).Should().BeFalse("there's nothing to install");
    }

    [Fact]
    public void Catalog_release_known_to_be_rolling_without_the_installed_file_or_a_renamed_one_is_not_rebuilt()
    {
        // A file outside those Quiver checked was installed: its absence says nothing about a new build.
        var build = new InstalledBuild("alpha-windows-portable.zip", OldSha);
        var release = Release("nightly", Asset("alpha-nightly-20261018-windows.zip", NewSha, null));

        InstalledBuild.IsRebuilt(build, null, release, "nightly", rolling: true).Should().BeFalse();
    }

    [Fact]
    public void Another_tag_is_never_a_rebuild()
    {
        var build = new InstalledBuild("alpha-windows.zip", OldSha, LastNight);
        var release = Release("nightly-2", Asset("alpha-windows.zip", NewSha, Tonight));

        InstalledBuild.IsRebuilt(build, LastNight, release, "nightly", rolling: true).Should().BeFalse();
        InstalledBuild.IsRebuilt(null, LastNight, release, "nightly").Should().BeFalse();
        InstalledBuild.IsRebuilt(build, LastNight, null, "nightly").Should().BeFalse();
    }

    [Fact]
    public async Task Upload_time_is_kept_beside_the_file_and_its_checksum()
    {
        var folder = Path.Combine(Path.GetTempPath(), "quiver-installed-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var build = new InstalledBuild("alpha-windows.zip", OldSha, LastNight.ToOffset(TimeSpan.FromHours(2)));
            await build.WriteAsync(folder);
            InstalledBuild.Read(folder).Should().Be(build);

            // A record from before upload times were kept still reads.
            await File.WriteAllTextAsync(Path.Combine(folder, InstalledBuild.FileNameOnDisk), $"alpha-windows.zip\n{OldSha}\n",
                TestContext.Current.CancellationToken);
            InstalledBuild.Read(folder).Should().Be(new InstalledBuild("alpha-windows.zip", OldSha));
            await new InstalledBuild("alpha-windows.zip", OldSha).WriteAsync(folder);
            (await File.ReadAllTextAsync(Path.Combine(folder, InstalledBuild.FileNameOnDisk), TestContext.Current.CancellationToken))
                .Should().Be($"alpha-windows.zip\n{OldSha}\n");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static GitHubRelease Release(string tag, params GitHubAsset[] assets) => new() { tag_name = tag, prerelease = true, assets = assets };

    private static GitHubAsset Asset(string name, string? sha, DateTimeOffset? uploadedAt) => new()
    {
        name = name, browser_download_url = $"https://downloads.example/{name}",
        digest = sha == null ? null : "sha256:" + sha, updated_at = uploadedAt,
    };
}
