using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class InputServiceOverlayTests
{
    [AvaloniaFact]
    public void ShouldKeepPollingWhenDeactivated_is_false_without_overlay()
    {
        var mainWindow = new Window();
        var settings = new AppSettings { EnableGamepadInput = true };

        using var inputService = new InputService(mainWindow, settings);

        inputService.ShouldKeepPollingWhenDeactivated().Should().BeFalse();
        inputService.IsGamepadOverlayActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ShouldKeepPollingWhenDeactivated_is_true_when_modal_attached()
    {
        var mainWindow = new Window();
        var settings = new AppSettings { EnableGamepadInput = true };
        var dialog = new Window { Content = new Button { Content = "OK" } };

        try
        {
            using var inputService = new InputService(mainWindow, settings);
            GamepadModalDialogNavigation.Attach(dialog);

            inputService.ShouldKeepPollingWhenDeactivated().Should().BeTrue();
            inputService.IsGamepadOverlayActive.Should().BeTrue();
        }
        finally
        {
            GamepadModalDialogNavigation.Instance.UnregisterModalDialog(dialog);
        }
    }

    [AvaloniaFact]
    public void ShouldKeepPollingWhenDeactivated_is_true_when_context_menu_attached()
    {
        var mainWindow = new Window();
        var settings = new AppSettings { EnableGamepadInput = true };
        var menu = new ContextMenu
        {
            Items = { new MenuItem { Header = "Option A" } },
        };

        try
        {
            using var inputService = new InputService(mainWindow, settings);
            GamepadContextMenuNavigation.Attach(menu);

            inputService.ShouldKeepPollingWhenDeactivated().Should().BeTrue();
            inputService.IsGamepadOverlayActive.Should().BeTrue();
        }
        finally
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
        }
    }
}
