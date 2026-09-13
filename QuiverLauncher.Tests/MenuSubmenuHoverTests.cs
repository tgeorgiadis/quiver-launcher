using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MenuSubmenuHoverTests
{
    [AvaloniaFact]
    public void Shared_behavior_reattaches_and_handles_only_the_submenu_exit_without_changing_click_dismissal()
    {
        var anchor = new Button { Content = "Options" };
        var window = new Window { Content = anchor, Width = 800, Height = 600 };
        var leaf = new MenuItem { Header = "Run" };
        var parent = new MenuItem { Header = "Options", Items = { leaf } };
        var menu = new ContextMenu { Items = { parent } };
        var exits = new List<bool>();
        var clicks = 0;
        leaf.Click += (_, _) => clicks++;
        menu.AddHandler(MenuItem.PointerExitedItemEvent, (_, e) =>
        {
            if (ReferenceEquals(e.Source, parent)) exits.Add(e.Handled);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        try
        {
            window.Show();
            for (var i = 0; i < 3; i++)
            {
                menu.Open(anchor); Settle(window);
                parent.IsSubMenuOpen = true; Settle(window);
                var root = TopLevel.GetTopLevel(parent)!;
                root.UpdateLayout();
                root.MouseMove(parent.TranslatePoint(new Point(10, 10), root)!.Value);
                root.MouseMove(new Point(-20, -20));
                Assert.Equal(i + 1, exits.Count);
                Assert.True(exits.Last());
                var childRoot = TopLevel.GetTopLevel(leaf)!;
                childRoot.UpdateLayout();
                var point = leaf.TranslatePoint(new Point(12, 12), childRoot)!.Value;
                childRoot.MouseMove(point);
                childRoot.MouseDown(point, MouseButton.Left);
                childRoot.MouseUp(point, MouseButton.Left);
                Assert.Equal(i + 1, clicks);
                Assert.False(menu.IsOpen);
            }
        }
        finally { menu.Close(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void Mouse_can_cross_popup_roots_return_and_leave_without_a_competing_close(bool openLeft)
    {
        var anchor = new Button { Content = "Options" };
        var window = new Window { Content = anchor, Width = 800, Height = 600 };
        var parent = new MenuItem { Header = "Launch Options", Items = { new MenuItem { Header = "Create Desktop Shortcut" } } };
        MenuSubmenuOverlap.SetEnable(parent, false);
        var menu = new ContextMenu { Items = { parent } };
        Popup? popup = null;
        parent.TemplateApplied += (_, e) => popup = e.NameScope.Find<Popup>("PART_Popup");
        var deadlines = new List<Action>();
        try
        {
            window.Show(); menu.Open(anchor); Settle(window);
            parent.IsSubMenuOpen = true; Settle(window);
            Assert.NotNull(popup);
            popup!.Placement = openLeft ? PlacementMode.LeftEdgeAlignedTop : PlacementMode.RightEdgeAlignedTop;
            Settle(window);
            using var hover = new MenuSubmenuHover(parent, popup!, (callback, _) => { deadlines.Add(callback); return new Cancellation(); });
            var parentRoot = TopLevel.GetTopLevel(parent)!;
            var childRoot = TopLevel.GetTopLevel(popup!.Child)!;
            parentRoot.UpdateLayout(); childRoot.UpdateLayout();
            parentRoot.MouseMove(parent.TranslatePoint(new Point(10, 10), parentRoot)!.Value);
            Assert.True(parent.IsPointerOver);
            // Separate native windows emit exit before entry into the child popup.
            parentRoot.MouseMove(new Point(-20, -20));
            childRoot.MouseMove(popup.Child!.TranslatePoint(new Point(10, 10), childRoot)!.Value);
            foreach (var deadline in deadlines.ToArray()) deadline();
            Assert.True(parent.IsSubMenuOpen);
            childRoot.MouseMove(new Point(-20, -20));
            parentRoot.MouseMove(parent.TranslatePoint(new Point(10, 10), parentRoot)!.Value);
            foreach (var deadline in deadlines.ToArray()) deadline();
            Assert.True(parent.IsSubMenuOpen);
            parentRoot.MouseMove(new Point(-20, -20));
            Assert.NotEmpty(deadlines);
            deadlines.Last()();
            Assert.False(parent.IsSubMenuOpen);
            parent.IsSubMenuOpen = true; Settle(window);
            foreach (var deadline in deadlines.ToArray()) deadline();
            Assert.True(parent.IsSubMenuOpen);
            menu.Close();
            foreach (var deadline in deadlines.ToArray()) deadline();
            Assert.False(menu.IsOpen);
        }
        finally { menu.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public void Dynamically_added_nested_submenu_keeps_ancestors_open_and_disposal_detaches_handlers()
    {
        var anchor = new Button { Content = "Options" };
        var window = new Window { Content = anchor, Width = 900, Height = 650 };
        var parent = new MenuItem { Header = "Versions", Items = { new MenuItem { Header = "Latest" } } };
        MenuSubmenuOverlap.SetEnable(parent, false);
        var menu = new ContextMenu { Items = { parent } };
        Popup? popup = null;
        parent.TemplateApplied += (_, e) => popup = e.NameScope.Find<Popup>("PART_Popup");
        var deadlines = new List<Action>();
        IDisposable Schedule(Action action, TimeSpan delay) { deadlines.Add(action); return new Cancellation(); }
        try
        {
            window.Show(); menu.Open(anchor); Settle(window);
            parent.IsSubMenuOpen = true; Settle(window);
            using var hover = new MenuSubmenuHover(parent, popup!, Schedule);
            var nested = new MenuItem { Header = "Older", Items = { new MenuItem { Header = "v1" } } };
            MenuSubmenuOverlap.SetEnable(nested, false);
            Popup? nestedPopup = null;
            nested.TemplateApplied += (_, e) => nestedPopup = e.NameScope.Find<Popup>("PART_Popup");
            parent.Items.Add(nested); Settle(window);
            nested.IsSubMenuOpen = true; Settle(window);
            using var nestedHover = new MenuSubmenuHover(nested, nestedPopup!, Schedule);
            var root = TopLevel.GetTopLevel(popup!.Child)!;
            var descendant = TopLevel.GetTopLevel(nestedPopup!.Child)!;
            root.UpdateLayout(); descendant.UpdateLayout();
            root.MouseMove(nested.TranslatePoint(new Point(10, 10), root)!.Value);
            Assert.True(nested.IsPointerOver);
            root.MouseMove(new Point(-20, -20));
            Assert.NotEmpty(deadlines);
            descendant.MouseMove(nestedPopup.Child!.TranslatePoint(new Point(10, 10), descendant)!.Value);
            Assert.True(nestedPopup.Child!.IsPointerOver);
            foreach (var deadline in deadlines.ToArray()) deadline();
            Assert.True(parent.IsSubMenuOpen);
            Assert.True(nested.IsSubMenuOpen);
            descendant.MouseMove(new Point(-20, -20));
            nestedHover.Dispose(); hover.Dispose();
            foreach (var deadline in deadlines.ToArray()) deadline();
            Assert.True(parent.IsSubMenuOpen);
            var count = deadlines.Count;
            root.MouseMove(nested.TranslatePoint(new Point(10, 10), root)!.Value);
            root.MouseMove(new Point(-20, -20));
            Assert.Equal(count, deadlines.Count);
        }
        finally { menu.Close(); window.Close(); }
    }

    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private sealed class Cancellation : IDisposable { public void Dispose() { } }
}
