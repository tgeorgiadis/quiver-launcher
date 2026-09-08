using FluentAssertions;
using QuiverLauncher.Services;
using System.Runtime.InteropServices;

namespace QuiverLauncher.Tests;

public class InstallationErrorMessagesTests
{
    [Theory]
    [InlineData("Operation did not complete successfully because the file contains a virus or potentially unwanted software.")]
    [InlineData("Blocked by Windows Defender")]
    [InlineData("potentially unwanted software detected")]
    public void IsLikelyWindowsDefenderBlock_returns_true_for_defender_messages(string errorMessage)
    {
        InstallationErrorMessages.IsLikelyWindowsDefenderBlock(errorMessage).Should().BeTrue();
    }

    [Theory]
    [InlineData("Network error: connection timed out")]
    [InlineData("Permission denied")]
    [InlineData("No releases found")]
    public void IsLikelyWindowsDefenderBlock_returns_false_for_other_errors(string errorMessage)
    {
        InstallationErrorMessages.IsLikelyWindowsDefenderBlock(errorMessage).Should().BeFalse();
    }

    [Fact]
    public void FormatInstallationError_includes_game_name_and_error_message()
    {
        var result = InstallationErrorMessages.FormatInstallationError(
            "Super Smash Bros.",
            "download failed");

        result.Should().Be("Error installing Super Smash Bros.: download failed");
    }

    [Fact]
    public void FormatInstallationError_appends_defender_guidance_on_windows_when_blocked()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var result = InstallationErrorMessages.FormatInstallationError(
            "Super Smash Bros.",
            "contains a virus or potentially unwanted software");

        result.Should().Contain("Error installing Super Smash Bros.:");
        result.Should().Contain("Protection history");
        result.Should().Contain("Allow if you believe it is safe");
    }

    [Fact]
    public void FormatInstallationError_omits_defender_guidance_for_generic_errors()
    {
        var result = InstallationErrorMessages.FormatInstallationError(
            "Super Smash Bros.",
            "Network error: connection timed out");

        result.Should().Be("Error installing Super Smash Bros.: Network error: connection timed out");
        result.Should().NotContain("Protection history");
    }

    [Theory]
    [InlineData("The process cannot access the file because it is being used by another process.")]
    [InlineData("Could not open 'game.7z' because it is still locked by another process.")]
    public void IsLikelyFileLock_returns_true_for_sharing_violations(string errorMessage)
    {
        InstallationErrorMessages.IsLikelyFileLock(errorMessage).Should().BeTrue();
    }

    [Fact]
    public void FormatInstallationError_appends_file_lock_guidance_on_windows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var result = InstallationErrorMessages.FormatInstallationError(
            "Cut the Rope DX",
            "The process cannot access the file because it is being used by another process.");

        result.Should().Contain("Error installing Cut the Rope DX:");
        result.Should().Contain("being used by another process");
        result.Should().Contain("scanning the downloaded archive");
        result.Should().NotContain("Protection history");
    }
}
