using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;
using Item = QuiverLauncher.Services.CatalogReviewGridCardActions.ChromeItem;
using Kind = QuiverLauncher.Services.CatalogReviewGridCardActions.ChromeKind;

namespace QuiverLauncher.Tests;

public class MobileActionRowTests
{
    [Theory]
    [InlineData(110, 2)]
    [InlineData(109, 1)]
    [InlineData(40, 0)]
    public void Fits_actual_widths_including_the_overflow_button(double width, int expected)
        => ActionOverflowLayout.InlineCount([50, 54], width, 40, 6).Should().Be(expected);

    [Theory]
    [InlineData(150, 3)]
    [InlineData(149, 2)]
    [InlineData(110, 1)]
    public void Three_actions_only_overflow_when_necessary(double width, int expected)
        => ActionOverflowLayout.InlineCount([44, 44, 50], width, 40, 6).Should().Be(expected);

    private static Item[] Actions(params Kind[] kinds) => kinds.Select(k => new Item(k, "app")).ToArray();
    private static Button[] Visible(MobileActionRow row) => row.Children.OfType<Button>().Where(b => b.IsVisible).ToArray();
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    [AvaloniaFact]
    public void Measured_actions_resize_without_duplicates_and_keep_focus()
    {
        var row = new MobileActionRow { Width = 300, Actions = Actions(Kind.Add, Kind.Details, Kind.Hide) };
        var window = new Window { Width = 400, Height = 150, Content = row };
        try
        {
            window.Show(); Layout(window);
            Visible(row).Select(b => b.Content).Should().Equal("Add", "Details", "Hide");
            var details = Visible(row)[1];
            details.Classes.Add("gamepad-focused");
            details.Focus();
            row.Width = 100;
            Layout(window);
            var more = Visible(row).Single(b => b.Content as string == "More");
            more.IsFocused.Should().BeTrue();
            Visible(row).Should().ContainSingle(b => b.Classes.Contains("gamepad-focused"));
            var menu = (MenuFlyout)more.Flyout!;
            Visible(row).Where(b => b != more).Select(b => b.Content)
                .Concat(menu.Items.OfType<MenuItem>().Select(m => m.Header)).Should().Equal("Add", "Details", "Hide");
            row.Width = 300;
            Layout(window);
            Visible(row).Should().NotContain(b => b.Content as string == "More");
            Visible(row)[0].IsFocused.Should().BeTrue();
            CatalogReviewNavigation.ResolveMobileActionFocus(Visible(row), 1).Should().Be(0);
            Visible(row).Should().ContainSingle(b => b.Classes.Contains("gamepad-focused"));
            Visible(row)[1].Should().BeSameAs(details);
            Visible(row).Should().OnlyContain(b => b.Bounds.Right <= row.Bounds.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Add_disappearance_rollback_and_recycling_keep_current_actions()
    {
        var row = new MobileActionRow { Width = 180, Actions = Actions(Kind.Add, Kind.Details, Kind.Hide) };
        var window = new Window { Width = 300, Height = 150, Content = row };
        try
        {
            window.Show(); Layout(window);
            Visible(row)[0].Focus();
            row.Actions = Actions(Kind.Details, Kind.Hide);
            Layout(window);
            Visible(row).Select(b => b.Content).Should().Equal("Details", "Hide");
            Visible(row)[0].IsFocused.Should().BeTrue();
            row.Actions = Actions(Kind.Add, Kind.Details, Kind.Hide);
            Layout(window);
            Visible(row).Should().Contain(b => b.Content as string == "Add");
            row.Actions = Actions(Kind.Details, Kind.Remove);
            Layout(window);
            Visible(row).Select(b => b.Content).Should().Equal("Details", "Remove");
            row.Width = 50;
            Layout(window);
            row.Actions = [new(Kind.Details, "new-app"), new(Kind.Unhide, "new-app")];
            Layout(window);
            string? activated = null;
            row.ActionInvoked += (sender, _) => activated = ((Button)sender!).Tag as string;
            var menu = (MenuFlyout)Visible(row).Single().Flyout!;
            menu.Items.OfType<MenuItem>().Select(m => m.Header).Should().Equal("Details", "Unhide");
            menu.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            activated.Should().Be("new-app");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Gamepad_can_activate_overflow_and_pending_add_focuses_more_when_details_cannot_fit()
    {
        var row = new MobileActionRow { Width = 120, Actions = Actions(Kind.Add, Kind.Details, Kind.Hide) };
        var window = new Window { Width = 300, Height = 200, Content = row };
        try
        {
            window.Show(); Layout(window);
            Visible(row)[0].Content.Should().Be("Add");
            Visible(row)[0].Focus();
            row.Actions = Actions(Kind.Details, Kind.Hide);
            Layout(window);
            var more = Visible(row).Single();
            more.Content.Should().Be("More");
            more.IsFocused.Should().BeTrue();
            Kind? selected = null;
            row.ActionInvoked += (sender, _) => selected = ((Item)((Button)sender!).DataContext!).Kind;
            GamepadMenuFlyoutNavigation.Toggle(more);
            Layout(window);
            GamepadMenuFlyoutNavigation.Instance.TryHandleNavigation(NavigationDirection.Down).Should().BeTrue();
            GamepadMenuFlyoutNavigation.Instance.TryHandleConfirm().Should().BeTrue();
            selected.Should().Be(Kind.Hide);
            ((MenuFlyout)more.Flyout!).IsOpen.Should().BeFalse();
        }
        finally { GamepadMenuFlyoutNavigation.Instance.TryHandleCancel(); window.Close(); }
    }

    [AvaloniaFact]
    public void Larger_button_text_remeasures_hidden_actions_without_shrinking_targets()
    {
        var row = new MobileActionRow { Width = 180, Actions = Actions(Kind.Details, Kind.Remove) };
        var window = new Window { Width = 300, Height = 150, Content = row };
        try
        {
            window.Show(); Layout(window);
            Visible(row).Select(b => b.Content).Should().Equal("Details", "Remove");
            foreach (var button in row.Children.OfType<Button>()) button.FontSize = 24;
            Layout(window);
            Visible(row).Should().Contain(b => b.Content as string == "More");
            foreach (var button in row.Children.OfType<Button>()) button.FontSize = 10;
            Layout(window);
            Visible(row).Select(b => b.Content).Should().Equal(new object[] { "Details", "Remove" },
                string.Join("; ", row.Children.OfType<Button>().Select(b => $"{b.Content}: font={b.FontSize} size={b.DesiredSize} visible={b.IsVisible} text=" + string.Join(",", b.GetVisualDescendants().OfType<TextBlock>().Select(t => $"{t.FontSize}/{t.DesiredSize}")))));
            Visible(row).Should().OnlyContain(b => b.Bounds.Height >= 32 && b.Bounds.Width >= 44);
        }
        finally { window.Close(); }
    }
}
