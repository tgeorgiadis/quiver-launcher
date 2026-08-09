using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherUpdateServiceTests
{
    [Fact]
    public void ComputePendingUpdatesCount_sums_launcher_and_game_updates()
    {
        LauncherUpdateService.ComputePendingUpdatesCount(false, 0).Should().Be(0);
        LauncherUpdateService.ComputePendingUpdatesCount(true, 0).Should().Be(1);
        LauncherUpdateService.ComputePendingUpdatesCount(false, 3).Should().Be(3);
        LauncherUpdateService.ComputePendingUpdatesCount(true, 2).Should().Be(3);
        LauncherUpdateService.ComputePendingUpdatesCount(false, -1).Should().Be(0);
    }

    [Fact]
    public void IsLauncherUpdatePending_requires_newer_last_known_version()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        // LastKnownVersion must be newer than the running assembly version.
        File.WriteAllText(Path.Combine(tempDir, "update_check.json"),
            """{"LastCheckTime":"2026-01-01T00:00:00Z","UpdateAvailable":true,"LastKnownVersion":"v99.0.0","CurrentVersion":"1.0.0"}""");

        try
        {
            var service = new LauncherUpdateService();
            service.IsLauncherUpdatePending(tempDir).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IsUpdateAvailable_detects_newer_tag()
    {
        var service = new LauncherUpdateService();
        service.IsUpdateAvailable("1.0.0", "v1.1.0").Should().BeTrue();
        service.IsUpdateAvailable("1.1.0", "v1.1.0").Should().BeFalse();
    }

    [Fact]
    public void ReadInstalledVersion_returns_assembly_version()
    {
        var fromService = new LauncherUpdateService().ReadInstalledVersion();
        fromService.Should().Be(LauncherVersionService.ReadInstalledVersion());
        fromService.Should().NotBeNullOrWhiteSpace();
        fromService.Should().NotBe("Version information not found");
    }

    [Fact]
    public void IsLauncherUpdatePending_compares_last_known_to_assembly_not_cache_current_version()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        // Cache claims CurrentVersion already matches LastKnownVersion, but pending is
        // decided against the running assembly version.
        File.WriteAllText(Path.Combine(tempDir, "update_check.json"),
            """{"LastCheckTime":"2026-01-01T00:00:00Z","UpdateAvailable":true,"LastKnownVersion":"v99.0.0","CurrentVersion":"v99.0.0"}""");

        try
        {
            new LauncherUpdateService().IsLauncherUpdatePending(tempDir).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ShouldSkipUpdateCheck_skips_only_within_interval()
    {
        var interval = LauncherUpdateService.DefaultUpdateCheckInterval;
        var lastCheck = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);

        LauncherUpdateService.ShouldSkipUpdateCheck(
            lastCheck, lastCheck.Add(interval - TimeSpan.FromSeconds(1)), interval).Should().BeTrue();

        LauncherUpdateService.ShouldSkipUpdateCheck(
            lastCheck, lastCheck.Add(interval), interval).Should().BeFalse();

        LauncherUpdateService.ShouldSkipUpdateCheck(
            DateTime.MinValue, DateTime.UtcNow, interval).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipUpdateCheck_does_not_permanently_skip_when_cache_says_up_to_date()
    {
        var interval = LauncherUpdateService.DefaultUpdateCheckInterval;
        var lastCheck = DateTime.UtcNow - interval - TimeSpan.FromMinutes(1);

        LauncherUpdateService.ShouldSkipUpdateCheck(lastCheck, DateTime.UtcNow, interval).Should().BeFalse();
    }
}
