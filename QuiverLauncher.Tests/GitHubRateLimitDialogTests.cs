using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;
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
