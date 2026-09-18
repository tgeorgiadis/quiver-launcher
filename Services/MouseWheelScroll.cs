using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace QuiverLauncher.Services;

/// <summary>Scales discrete wheel input while retaining the native scrolling and boundary behaviour.</summary>
public sealed class MouseWheelScroll : AvaloniaObject
{
    public static readonly AttachedProperty<int> MultiplierProperty =
        AvaloniaProperty.RegisterAttached<MouseWheelScroll, Control, int>("Multiplier", 1);
    public static int GetMultiplier(Control control) => control.GetValue(MultiplierProperty);
    public static void SetMultiplier(Control control, int value) => control.SetValue(MultiplierProperty, value);

    static MouseWheelScroll()
    {
        MultiplierProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            control.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
            if (GetMultiplier(control) is 2 or 3 or 5)
                control.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        });
    }

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Control control || e is ScaledWheelEventArgs || e.Handled ||
            e.Pointer.Type != PointerType.Mouse || e.KeyModifiers != KeyModifiers.None ||
            e.Delta.X != 0 || e.Delta.Y == 0 || e.Delta.Y != Math.Truncate(e.Delta.Y) ||
            e.Source is not Interactive source) return;
        // A dropdown or text editor owns its own wheel input.
        if (source.GetSelfAndVisualAncestors().TakeWhile(v => v != control)
            .Any(v => v is ComboBox or TextBox or Slider)) return;
        var point = e.GetCurrentPoint(control);
        var scaled = new ScaledWheelEventArgs(source, e, control, point, GetMultiplier(control));
        source.RaiseEvent(scaled);
        e.Handled = true;
    }

    private sealed class ScaledWheelEventArgs(object source, PointerWheelEventArgs original,
        Control root, PointerPoint point, int multiplier)
        : PointerWheelEventArgs(source, original.Pointer, root, point.Position, original.Timestamp,
            point.Properties, original.KeyModifiers, original.Delta * multiplier);
}
