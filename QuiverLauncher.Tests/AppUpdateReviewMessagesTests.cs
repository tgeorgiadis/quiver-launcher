using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AppUpdateReviewMessagesTests
{
    private static GameInfo PendingGame(
        string name,
        string installed,
        string latest) =>
        new()
        {
            Name = name,
            InstalledVersion = installed,
            LatestVersion = latest,
            Status = GameStatus.UpdateAvailable,
        };

    [Fact]
    public void FormatPendingAppUpdatesMessage_single_app_with_prompt()
    {
        var summary = AppUpdateReviewMessages.FormatPendingAppUpdatesMessage(
            [PendingGame("Doom", "v1.0.0", "v1.1.0")],
            includeOpenPrompt: true);

        summary.Should().Be(
            "1 app update needs review:\n\n" +
            "• Doom (v1.0.0 → v1.1.0)\n\n" +
            "Review this update now?");
    }

    [Fact]
    public void FormatPendingAppUpdatesMessage_multiple_apps_with_prompt()
    {
        var summary = AppUpdateReviewMessages.FormatPendingAppUpdatesMessage(
            [
                PendingGame("Beta", "v2.0.0", "v2.1.0"),
                PendingGame("Alpha", "v1.0.0", "v1.2.0"),
            ],
            includeOpenPrompt: true);

        summary.Should().Be(
            "2 app updates need review:\n\n" +
            "• Alpha (v1.0.0 → v1.2.0)\n" +
            "• Beta (v2.0.0 → v2.1.0)\n\n" +
            "Review these updates now?");
    }

    [Fact]
    public void FormatAutoUpdatedSummary_formats_counts()
    {
        AppUpdateReviewMessages.FormatAutoUpdatedSummary(0).Should().BeEmpty();
        AppUpdateReviewMessages.FormatAutoUpdatedSummary(1)
            .Should().Be("1 app was updated automatically.");
        AppUpdateReviewMessages.FormatAutoUpdatedSummary(3)
            .Should().Be("3 apps were updated automatically.");
    }

    [Fact]
    public void FormatGameUpdateLine_uses_placeholders_when_versions_missing()
    {
        var line = AppUpdateReviewMessages.FormatGameUpdateLine(new GameInfo
        {
            Name = "Mystery",
            Status = GameStatus.UpdateAvailable,
        });

        line.Should().Be("• Mystery (? → ?)");
    }

    [Fact]
    public void FormatCombinedUpdatesMessage_lists_quiver_and_apps()
    {
        var summary = AppUpdateReviewMessages.FormatCombinedUpdatesMessage(
            "v2.3.2",
            [PendingGame("Doom", "v1.0.0", "v1.1.0")]);

        summary.Should().Be(
            "Quiver Launcher update v2.3.2 is available.\n\n" +
            "1 app update needs review:\n\n" +
            "• Doom (v1.0.0 → v1.1.0)\n\n" +
            "What would you like to update?");
    }
}
