using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AndroidLauncherPackageValidationTests
{
    [Theory]
    [InlineData("3.4.0", "3.4.0-beta.1", true)]
    [InlineData("3.4.0-beta.10", "3.4.0-beta.9", true)]
    [InlineData("3.4.0-beta.1", "3.4.0", false)]
    [InlineData("3.4.0+build2", "3.4.0+build1", false)]
    [InlineData("garbage", "3.3.4", false)]
    public void Preview_ordering_respects_release_identity(string candidate, string installed, bool expected) =>
        AndroidLauncherPackageValidation.IsNewerVersion(candidate, installed).Should().Be(expected);
    [Theory]
    [InlineData("wrong.package", 30400, "3.4.0", "v3.4.0")]
    [InlineData("com.quiverlauncher.app", 30303, "3.4.0", "v3.4.0")]
    [InlineData("com.quiverlauncher.app", 30400, "3.3.3", "v3.3.3")]
    [InlineData("com.quiverlauncher.app", 30400, "3.4.0", "v3.5.0")]
    public void Rejects_wrong_package_downgrades_and_mismatched_release(string package, long code, string version, string expected)
    {
        var check = () => AndroidLauncherPackageValidation.ValidateIdentity(new("com.quiverlauncher.app", 30304, "3.3.4"), new(package, code, version), expected);
        check.Should().Throw<InvalidDataException>();
    }
    [Fact]
    public void Allows_preview_to_stable_with_same_android_code() => AndroidLauncherPackageValidation.ValidateIdentity(
        new("com.quiverlauncher.app", 30400, "3.4.0-beta.1"), new("com.quiverlauncher.app", 30400, "3.4.0"), "v3.4.0");
    [Fact]
    public void Signing_keys_must_match_or_rotate_forward_using_android_history()
    {
        AndroidLauncherPackageValidation.HasCompatibleSigningKeys(["release"], ["release"]).Should().BeTrue();
        AndroidLauncherPackageValidation.HasCompatibleSigningKeys(["debug"], ["release"]).Should().BeFalse();
        AndroidLauncherPackageValidation.HasCompatibleSigningKeys([], []).Should().BeFalse();
        AndroidLauncherPackageValidation.HasCompatibleSigningKeys(["old"], ["new"], ["old", "new"]).Should().BeTrue();
        AndroidLauncherPackageValidation.HasCompatibleSigningKeys(["new"], ["old"], ["old"]).Should().BeFalse();
        AndroidLauncherPackageValidation.HasCompatibleSigningKeys(["a", "b"], ["a"], ["a", "b"]).Should().BeFalse();
    }
}
