using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class BackgroundUpdateCheckIntervalsTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(180)]
    [InlineData(360)]
    [InlineData(720)]
    [InlineData(1440)]
    public void Normalize_keeps_preset_values(int minutes)
    {
        BackgroundUpdateCheckIntervals.Normalize(minutes).Should().Be(minutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9999)]
    public void Normalize_falls_back_to_default_for_unknown_values(int minutes)
    {
        BackgroundUpdateCheckIntervals.Normalize(minutes)
            .Should().Be(BackgroundUpdateCheckIntervals.DefaultMinutes);
    }

    [Fact]
    public void FormatLabel_uses_friendly_names()
    {
        BackgroundUpdateCheckIntervals.FormatLabel(60).Should().Be("Every hour");
        BackgroundUpdateCheckIntervals.FormatLabel(1440).Should().Be("Every day");
    }

    [Fact]
    public void AppSettings_EnsureInitialized_normalizes_interval()
    {
        var settings = new AppSettings { BackgroundUpdateCheckIntervalMinutes = 17 };
        settings.EnsureInitialized();
        settings.BackgroundUpdateCheckIntervalMinutes.Should().Be(BackgroundUpdateCheckIntervals.DefaultMinutes);
    }

    [Fact]
    public void AppSettings_AutoUpdateNewlyAddedApps_defaults_to_false()
    {
        new AppSettings().AutoUpdateNewlyAddedApps.Should().BeFalse();
    }
}
