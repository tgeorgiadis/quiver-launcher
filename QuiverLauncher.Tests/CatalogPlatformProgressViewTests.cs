using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class CatalogPlatformProgressViewTests
{
    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(510)]
    public void Status_wraps_without_taking_focus_and_clears_after_completion(int width)
    {
        var model = new CatalogSyncViewModel();
        var view = new CatalogReviewView { DataContext = model };
        var focusTarget = new TextBox();
        var host = new StackPanel(); host.Children.Add(focusTarget); host.Children.Add(view);
        var window = new Window { Width = width, Height = 700, Content = host };
        try
        {
            window.Show(); focusTarget.Focus();
            var strip = view.FindControl<WrapPanel>("CatalogPlatformCheckStatus")!;
            strip.IsVisible.Should().BeFalse();
            model.SetPlatformCheck(new(12, 48, CatalogReleaseWarmupOutcome.Running));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            strip.IsVisible.Should().BeTrue();
            strip.Focusable.Should().BeFalse();
            model.PlatformCheckText.Should().Contain("36 apps");
            foreach (var text in strip.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible))
            {
                text.Bounds.Width.Should().BeGreaterThan(0);
                text.Bounds.Width.Should().BeLessThanOrEqualTo(width);
            }
            focusTarget.IsFocused.Should().BeTrue();
            model.SetPlatformCheck(new(13, 48, CatalogReleaseWarmupOutcome.RateLimited));
            model.ShowPlatformCheck.Should().BeTrue(); model.IsCheckingPlatforms.Should().BeFalse();
            model.PlatformCheckText.Should().Be("Compatibility checks paused");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var details = view.FindControl<Button>("CatalogPlatformDetailsButton")!;
            var menu = (MenuFlyout)details.Flyout!;
            menu.IsOpen.Should().BeFalse("technical details stay collapsed by default");
            strip.Bounds.Height.Should().BeLessThan(80);
            menu.ShowAt(details);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var content = (StackPanel)((MenuItem)menu.Items[0]!).Header!;
            var explanation = content.Children.OfType<TextBlock>().Single(t => t.Name == "CatalogPlatformCheckExplanation");
            explanation.Text.Should().Contain("Add a valid GitHub token");
            explanation.Bounds.Width.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(width);
            menu.Hide(); focusTarget.Focus();
            model.SetPlatformCheck(new(48, 48, CatalogReleaseWarmupOutcome.Completed));
            strip.IsVisible.Should().BeFalse(); focusTarget.IsFocused.Should().BeTrue();
        }
        finally { window.Close(); }
    }
}
