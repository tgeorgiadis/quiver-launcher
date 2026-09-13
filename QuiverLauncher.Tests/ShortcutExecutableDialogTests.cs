using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class ShortcutExecutableDialogTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Controller_selection_or_cancel_returns_without_launching(bool cancel)
    {
        var dialog = new ShortcutExecutableDialog("Example", ["/game/first.exe", "/game/second.exe"]);
        var nav = GamepadModalDialogNavigation.Instance;
        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            nav.RefreshDialogButtons();
            Dispatcher.UIThread.RunJobs();
            nav.TryHandleNavigation(NavigationDirection.Down).Should().BeTrue();
            if (cancel)
            {
                nav.TryHandleCancel().Should().BeTrue();
                dialog.SelectedExecutable.Should().BeNull();
            }
            else
            {
                nav.TryHandleConfirm().Should().BeTrue();
                dialog.SelectedExecutable.Should().Be("/game/second.exe");
            }
            dialog.IsVisible.Should().BeFalse();
        }
        finally { GamepadFocusChrome.SetActive(false); dialog.Close(); }
    }
}
