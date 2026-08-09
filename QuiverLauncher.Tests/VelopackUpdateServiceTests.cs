using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class VelopackUpdateServiceTests
{
    [Theory]
    [InlineData("2.4.2", false, false)]
    [InlineData("2.4.3", false, false)]
    [InlineData("2.4.3", true, true)]
    [InlineData("2.4.3-rc.1", false, true)]
    [InlineData("2.4.3-rc.1", true, true)]
    [InlineData("2.4.3-beta.2", false, true)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    public void EffectiveIncludePrerelease_matches_setting_or_hyphen_version(
        string? currentVersion,
        bool allowPrereleaseLauncherUpdates,
        bool expected)
    {
        VelopackUpdateService.EffectiveIncludePrerelease(currentVersion, allowPrereleaseLauncherUpdates)
            .Should().Be(expected);
    }
}
