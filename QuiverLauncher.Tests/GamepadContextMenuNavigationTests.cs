using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher.Services;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Tests;

public class GamepadContextMenuNavigationTests
{
    [AvaloniaFact]
    public void Attach_registers_menu_before_opened_event()
    {
        var menu = new ContextMenu
        {
            Items = { new MenuItem { Header = "Option A" } },
        };

        try
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
            GamepadContextMenuNavigation.Instance.HasActiveContextMenu.Should().BeFalse();

            GamepadContextMenuNavigation.Attach(menu);

            GamepadContextMenuNavigation.Instance.HasActiveContextMenu.Should().BeTrue();
        }
        finally
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
        }
    }

    [Fact]
    public void MoveMenuItemIndex_moves_down_and_wraps()
    {
        GamepadContextMenuNavigation.MoveMenuItemIndex(0, NavigationDirection.Down, 3).Should().Be(1);
        GamepadContextMenuNavigation.MoveMenuItemIndex(2, NavigationDirection.Down, 3).Should().Be(0);
    }

    [Fact]
    public void MoveMenuItemIndex_moves_up_and_wraps()
    {
        GamepadContextMenuNavigation.MoveMenuItemIndex(0, NavigationDirection.Up, 3).Should().Be(2);
        GamepadContextMenuNavigation.MoveMenuItemIndex(1, NavigationDirection.Up, 3).Should().Be(0);
    }

    [Fact]
    public void MoveMenuItemIndex_ignores_left_right()
    {
        GamepadContextMenuNavigation.MoveMenuItemIndex(0, NavigationDirection.Right, 2).Should().Be(0);
        GamepadContextMenuNavigation.MoveMenuItemIndex(1, NavigationDirection.Left, 2).Should().Be(1);
    }

    [AvaloniaFact]
    public void CollectNavigableMenuItems_skips_disabled_headers()
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "Select executable:", IsEnabled = false },
                new Separator(),
                new MenuItem { Header = "game.exe" },
                new MenuItem { Header = "launcher.exe" },
                new MenuItem { Header = "Cancel" },
            },
        };

        var items = GamepadContextMenuNavigation.CollectNavigableMenuItems(menu);

        items.Should().HaveCount(3);
        items[0].Header.Should().Be("game.exe");
        items[2].Header.Should().Be("Cancel");
    }

    [AvaloniaFact]
    public void FindCancelMenuItemIndex_finds_cancel_option()
    {
        var items = new List<MenuItem>
        {
            new() { Header = "game.exe" },
            new() { Header = "Cancel" },
        };

        GamepadContextMenuNavigation.FindCancelMenuItemIndex(items).Should().Be(1);
    }

    [Fact]
    public void MoveMenuItemIndex_keeps_single_item_index()
    {
        GamepadContextMenuNavigation.MoveMenuItemIndex(0, NavigationDirection.Down, 1).Should().Be(0);
        GamepadContextMenuNavigation.MoveMenuItemIndex(0, NavigationDirection.Up, 1).Should().Be(0);
    }

    [AvaloniaFact]
    public void HasOpenableChildren_detects_nested_items()
    {
        var parent = new MenuItem
        {
            Header = "Catalog",
            Items =
            {
                new MenuItem { Header = "Browse" },
                new MenuItem { Header = "Refresh" },
            },
        };

        GamepadContextMenuNavigation.HasOpenableChildren(parent).Should().BeTrue();
        GamepadContextMenuNavigation.HasOpenableChildren(new MenuItem { Header = "Leaf" }).Should().BeFalse();
    }

    [AvaloniaFact]
    public void TryHandleOptionsDismiss_closes_active_context_menu()
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "Download" },
                new MenuItem { Header = "Cancel" },
            },
        };

        try
        {
            GamepadContextMenuNavigation.Attach(menu);
            GamepadContextMenuNavigation.Instance.HasActiveContextMenu.Should().BeTrue();

            GamepadContextMenuNavigation.Instance.TryHandleOptionsDismiss().Should().BeTrue();
            GamepadContextMenuNavigation.Instance.HasActiveContextMenu.Should().BeFalse();
        }
        finally
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
        }
    }

    [AvaloniaFact]
    public void TryHandleNavigation_right_opens_submenu_and_left_closes_it()
    {
        var childA = new MenuItem { Header = "Browse" };
        var childB = new MenuItem { Header = "Refresh" };
        var parent = new MenuItem
        {
            Header = "Catalog",
            Items = { childA, childB },
        };
        var leaf = new MenuItem { Header = "Download" };
        var menu = new ContextMenu { Items = { parent, leaf } };

        try
        {
            GamepadContextMenuNavigation.Attach(menu);
            GamepadContextMenuNavigation.Instance.TryHandleNavigation(NavigationDirection.Right)
                .Should().BeTrue();
            parent.IsSubMenuOpen.Should().BeTrue();

            GamepadContextMenuNavigation.Instance.TryHandleNavigation(NavigationDirection.Down)
                .Should().BeTrue();

            GamepadContextMenuNavigation.Instance.TryHandleCancel().Should().BeTrue();
            parent.IsSubMenuOpen.Should().BeFalse();
            GamepadContextMenuNavigation.Instance.HasActiveContextMenu.Should().BeTrue();
        }
        finally
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_opens_submenu_for_parent_items()
    {
        var child = new MenuItem { Header = "Browse" };
        var parent = new MenuItem
        {
            Header = "Catalog",
            Items = { child },
        };
        var menu = new ContextMenu { Items = { parent } };

        try
        {
            GamepadContextMenuNavigation.Attach(menu);
            GamepadContextMenuNavigation.Instance.TryHandleConfirm().Should().BeTrue();
            parent.IsSubMenuOpen.Should().BeTrue();
        }
        finally
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_on_nested_leaf_closes_root_context_menu()
    {
        var editTagsClicked = false;
        var editTags = new MenuItem { Header = "Edit Tags" };
        editTags.Click += (_, _) => editTagsClicked = true;
        var catalog = new MenuItem
        {
            Header = "Catalog",
            Items = { editTags },
        };
        var button = new Button { Content = "Options" };
        var menu = new ContextMenu { Items = { catalog } };
        var window = new Window
        {
            Content = button,
            Width = 240,
            Height = 180,
        };

        try
        {
            window.Show();
            GamepadContextMenuNavigation.Attach(menu);
            menu.Open(button);
            menu.IsOpen.Should().BeTrue();

            GamepadContextMenuNavigation.Instance.TryHandleConfirm().Should().BeTrue();
            catalog.IsSubMenuOpen.Should().BeTrue();

            GamepadContextMenuNavigation.Instance.TryHandleConfirm().Should().BeTrue();
            editTagsClicked.Should().BeTrue();
            menu.IsOpen.Should().BeFalse();
            GamepadContextMenuNavigation.Instance.HasActiveContextMenu.Should().BeFalse();
        }
        finally
        {
            GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu);
            if (menu.IsOpen)
                menu.Close();
            window.Close();
        }
    }

    [Fact]
    public void ResolveKeyboardCancel_invokes_cancel_for_bound_back_key()
    {
        var bindings = KeyboardBindingDefaults.Create();
        GamepadAction? Resolve(Key key, KeyModifiers modifiers) =>
            KeyboardBindingDefaults.FindAction(bindings, key, modifiers);

        GamepadContextMenuNavigation.ResolveKeyboardCancel(Key.Escape, KeyModifiers.None, Resolve)
            .Should().Be(ContextMenuKeyboardCancelResult.InvokeCancel);

        KeyboardBindingDefaults.AssignExclusive(
            bindings,
            GamepadAction.Cancel,
            KeyboardBinding.Of(Key.Back));

        GamepadContextMenuNavigation.ResolveKeyboardCancel(Key.Back, KeyModifiers.None, Resolve)
            .Should().Be(ContextMenuKeyboardCancelResult.InvokeCancel);
        GamepadContextMenuNavigation.ResolveKeyboardCancel(Key.Escape, KeyModifiers.None, Resolve)
            .Should().Be(ContextMenuKeyboardCancelResult.SwallowEscape);
    }

    [Fact]
    public void ResolveKeyboardCancel_none_when_resolver_missing()
    {
        GamepadContextMenuNavigation.ResolveKeyboardCancel(Key.Escape, KeyModifiers.None, null)
            .Should().Be(ContextMenuKeyboardCancelResult.None);
    }
}
