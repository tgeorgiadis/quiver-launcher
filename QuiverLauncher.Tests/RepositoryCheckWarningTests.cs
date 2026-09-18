using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class RepositoryCheckWarningTests
{
    [AvaloniaFact]
    public void Warning_tracks_check_result_and_exposes_the_reason()
    {
        var game = new GameInfo { Name = "Uninstalled app", Repository = "fixture/missing" };
        var warning = new RepositoryCheckWarning { DataContext = game };
        var window = new Window { Content = warning };
        try
        {
            window.Show();
            warning.IsVisible.Should().BeFalse();
            game.RepositoryCheckError = "Repository not found (HTTP 404).";
            Dispatcher.UIThread.RunJobs();
            warning.IsVisible.Should().BeTrue();
            ToolTip.GetTip(warning).Should().Be(game.RepositoryCheckError);
            game.RepositoryCheckError = null;
            Dispatcher.UIThread.RunJobs();
            warning.IsVisible.Should().BeFalse();
        }
        finally { window.Close(); }
    }
}
