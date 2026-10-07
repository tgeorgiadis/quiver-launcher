using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace QuiverLauncher.Views;

/// <summary>
/// Hides library card menu items other than Launch while kiosk is locked,
/// without clearing the IsVisible bindings those items already use.
/// </summary>
internal sealed class LibraryKioskMenu
{
    private static readonly AttachedProperty<bool> CollapsedProperty =
        AvaloniaProperty.RegisterAttached<LibraryKioskMenu, MenuItem, bool>("KioskCollapsed");

    public static void Apply(ContextMenu menu, bool locked)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
            Apply(item, locked);
    }

    private static void Apply(MenuItem item, bool locked)
    {
        var keep = item.Header as string == "Launch";
        if (locked && !keep)
            Collapse(item);
        else if (item.GetValue(CollapsedProperty))
            Restore(item);

        foreach (var child in item.Items.OfType<MenuItem>())
            Apply(child, locked);
    }

    private static void Collapse(MenuItem item)
    {
        item.SetValue(CollapsedProperty, true);
        item.IsEnabled = false;
        item.Height = 0;
        item.MinHeight = 0;
        item.MaxHeight = 0;
        item.Opacity = 0;
        item.Padding = default;
        item.Margin = default;
        item.IsHitTestVisible = false;
        item.Focusable = false;
    }

    private static void Restore(MenuItem item)
    {
        item.SetValue(CollapsedProperty, false);
        item.ClearValue(InputElement.IsEnabledProperty);
        item.ClearValue(Layoutable.HeightProperty);
        item.ClearValue(Layoutable.MinHeightProperty);
        item.ClearValue(Layoutable.MaxHeightProperty);
        item.ClearValue(Visual.OpacityProperty);
        item.ClearValue(TemplatedControl.PaddingProperty);
        item.ClearValue(Layoutable.MarginProperty);
        item.ClearValue(InputElement.IsHitTestVisibleProperty);
        item.ClearValue(InputElement.FocusableProperty);
    }
}
