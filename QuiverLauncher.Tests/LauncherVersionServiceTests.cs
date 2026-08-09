using System.Reflection;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherVersionServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("V2.0", "1.9.9", true)]
    public void IsNewerVersion_compares_semantic_versions(string candidate, string baseline, bool expected)
    {
        LauncherVersionService.IsNewerVersion(candidate, baseline).Should().Be(expected);
    }

    [Fact]
    public void AreVersionsEquivalent_treats_v_prefix_as_equal()
    {
        LauncherVersionService.AreVersionsEquivalent("v1.0.0", "1.0.0").Should().BeTrue();
    }

    [Fact]
    public void NormalizeVersionString_pads_short_versions()
    {
        LauncherVersionService.NormalizeVersionString("v2").Should().Be("2.0.0");
    }

    [Fact]
    public void NormalizeVersionString_strips_plus_build_metadata()
    {
        LauncherVersionService.NormalizeVersionString("1.2.3+abc").Should().Be("1.2.3");
    }

    [Fact]
    public void StripBuildMetadata_removes_git_suffix()
    {
        LauncherVersionService.StripBuildMetadata("3.0.0-rc.1+abc123")
            .Should().Be("3.0.0-rc.1");
    }

    [Fact]
    public void ReadInstalledVersion_returns_assembly_informational_version_without_build_metadata()
    {
        var expected = typeof(LauncherVersionService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        expected.Should().NotBeNullOrWhiteSpace();
        var cleaned = LauncherVersionService.StripBuildMetadata(expected!.Trim());

        LauncherVersionService.ReadInstalledVersion().Should().Be(cleaned);
        // Directory argument is ignored (no version.txt fallback).
        LauncherVersionService.ReadInstalledVersion(Path.GetTempPath()).Should().Be(cleaned);
    }
}
