using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AppUpdateSelectionTests
{
    private static GameInfo CreatePending(string name, bool autoUpdate) =>
        new()
        {
            Name = name,
            Status = GameStatus.UpdateAvailable,
            AutoUpdate = autoUpdate,
            InstalledVersion = "v1.0.0",
            LatestVersion = "v1.1.0",
        };

    [Fact]
    public void GetManualPendingUpdates_excludes_auto_update_apps()
    {
        var games = new[]
        {
            CreatePending("Auto", autoUpdate: true),
            CreatePending("Manual", autoUpdate: false),
            new GameInfo { Name = "Installed", Status = GameStatus.Installed, AutoUpdate = false },
        };

        var manual = AppUpdateSelection.GetManualPendingUpdates(games);

        manual.Should().ContainSingle(g => g.Name == "Manual");
    }

    [Fact]
    public void GetAutoPendingUpdates_includes_only_auto_update_apps()
    {
        var games = new[]
        {
            CreatePending("Auto", autoUpdate: true),
            CreatePending("Manual", autoUpdate: false),
        };

        var auto = AppUpdateSelection.GetAutoPendingUpdates(games);

        auto.Should().ContainSingle(g => g.Name == "Auto");
    }

    [Fact]
    public void CountManualPendingUpdates_counts_non_auto_update_available()
    {
        var games = new[]
        {
            CreatePending("Auto", autoUpdate: true),
            CreatePending("Manual A", autoUpdate: false),
            CreatePending("Manual B", autoUpdate: false),
        };

        AppUpdateSelection.CountManualPendingUpdates(games).Should().Be(2);
    }
}
