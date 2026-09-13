using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;
using QuiverLauncher.ViewModels;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Tests;

public class GitHubRateLimitDialogTests
{
    private static void Layout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaTheory]
    [InlineData(600, 520, 14)]
    [InlineData(320, 480, 14)]
    [InlineData(360, 360, 18)]
    public void Text_wraps_and_actions_stay_visible_when_content_scrolls(int width, int height, int fontSize)
    {
        var dialog = new GitHubRateLimitDialog(() => { }, () => { }) { SizeToContent = SizeToContent.Manual, Width = width, Height = height, FontSize = fontSize };
        try
        {
            dialog.Show();
            Layout(dialog);
            var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
            scroll.Extent.Width.Should().BeLessThanOrEqualTo(scroll.Viewport.Width + 1);
            var actions = dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Parent is WrapPanel).ToArray();
            actions.Select(b => b.Content).Should().Equal("Open Settings", "Create token", "Close");
            foreach (var button in actions)
            {
                button.GetVisualAncestors().Should().NotContain(scroll);
                var position = button.TranslatePoint(default, dialog)!.Value;
                position.X.Should().BeGreaterThanOrEqualTo(0);
                (position.X + button.Bounds.Width).Should().BeLessThanOrEqualTo(dialog.ClientSize.Width);
                (position.Y + button.Bounds.Height).Should().BeLessThanOrEqualTo(dialog.ClientSize.Height);
            }
            var original = actions[0].TranslatePoint(default, dialog);
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Layout(dialog);
            actions[0].TranslatePoint(default, dialog).Should().Be(original);
        }
        finally { dialog.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(225)]
    public void Actual_dialog_scaling_preserves_finite_width_and_sizes_height_to_content(int percent)
    {
        var model = new SettingsViewModel(new Store(new() { InterfaceScalePercent = percent }));
        var owner = new Window { Width = 1000, Height = 700, Content = new Border() };
        using var scaling = new DesktopInterfaceScaling(owner, model, () => new Size(3840, 2160));
        var dialog = new GitHubRateLimitDialog(() => { }, () => { });
        try
        {
            owner.Show();
            Layout(owner);
            DesktopInterfaceScaling.PrepareDialog(dialog, owner);
            dialog.Show();
            Layout(dialog);
            var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Extent.Width.Should().BeLessThanOrEqualTo(scroll.Viewport.Width + 1);
            var text = (StackPanel)scroll.Content!;
            foreach (var block in text.GetVisualDescendants().OfType<TextBlock>())
                block.Bounds.Width.Should().BeLessThanOrEqualTo(scroll.Viewport.Width + 1);
            // The body should not be stretched hundreds of pixels beyond its text.
            (scroll.Viewport.Height - text.DesiredSize.Height).Should().BeLessThanOrEqualTo(2);
            dialog.ClientSize.Height.Should().BeLessThanOrEqualTo(dialog.MaxHeight);
            dialog.SizeToContent.Should().Be(SizeToContent.Height);
            var originalHeight = dialog.ClientSize.Height;
            text.Children.RemoveAt(3);
            Layout(dialog);
            dialog.ClientSize.Height.Should().BeLessThan(originalHeight, "the window follows its content instead of reserving a fixed empty area");
        }
        finally { dialog.Close(); owner.Close(); }
    }

    private sealed class Store(AppSettings settings) : ISettingsStore
    {
        public AppSettings Current => settings;
        public AppSettings Load() => settings;
        public void Save(AppSettings value) { }
    }

    [AvaloniaFact]
    public void Create_token_is_reachable_by_gamepad_and_keyboard_and_keeps_prompt_open()
    {
        var created = 0;
        var dialog = new GitHubRateLimitDialog(() => { }, () => created++);
        var nav = GamepadModalDialogNavigation.Instance;
        var previousResolver = nav.ResolveKeyboardAction;
        try
        {
            nav.ResolveKeyboardAction = (key, modifiers) => KeyboardBindingDefaults.FindAction(KeyboardBindingDefaults.Create(), key, modifiers);
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            Layout(dialog);
            nav.RefreshDialogButtons();
            Layout(dialog);
            nav.TryHandleNavigation(NavigationDirection.Right).Should().BeTrue();
            nav.TryHandleConfirm().Should().BeTrue();
            created.Should().Be(1);
            dialog.IsVisible.Should().BeTrue();
            nav.TryHandleDialogKeyDown(Key.Enter, KeyModifiers.None).Should().BeTrue();
            created.Should().Be(2);
            nav.TryHandleDialogKeyDown(Key.Escape, KeyModifiers.None).Should().BeTrue();
            dialog.IsVisible.Should().BeFalse();
        }
        finally
        {
            nav.ResolveKeyboardAction = previousResolver;
            GamepadFocusChrome.SetActive(false);
            dialog.Close();
        }
    }
}
