using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace QuiverLauncher.Views;

/// <summary>Claims only deliberate, single-finger right swipes starting at the left edge.</summary>
internal sealed class MobileEdgeSwipe(Control root, Func<bool> canOpen, Action open) : IDisposable
{
    private IPointer? _pointer;
    private Point _start;
    private bool _claimed;
    private bool _takingCapture;

    public void Attach()
    {
        root.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Bubble, true);
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (_pointer != null) { Reset(); return; }
        var position = e.GetPosition(root);
        if (e.Pointer.Type != PointerType.Touch || !canOpen() || position.X < 0 || position.X > 24)
            return;
        _pointer = e.Pointer;
        _start = position;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _pointer) return;
        if (!canOpen()) { Reset(); return; }
        var delta = e.GetPosition(root) - _start;
        if (!_claimed)
        {
            if (Math.Abs(delta.Y) > 12 || delta.X < -12) { Reset(); return; }
            if (delta.X < 12 || delta.X < Math.Abs(delta.Y) * 2) return;
            // Capturing cancels the child button/scroll gesture before it can activate.
            _claimed = true;
            _takingCapture = true;
            try { e.Pointer.Capture(root); }
            finally { _takingCapture = false; }
        }
        e.Handled = true;
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _pointer) return;
        var delta = e.GetPosition(root) - _start;
        var shouldOpen = _claimed && canOpen() && delta.X >= 48 && delta.X > Math.Abs(delta.Y) * 2;
        e.Handled |= _claimed;
        Reset();
        if (shouldOpen) open();
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_takingCapture && e.Pointer == _pointer && e.Pointer.Captured != root) Reset();
    }

    private void Reset()
    {
        var pointer = _pointer;
        _pointer = null;
        _claimed = false;
        if (pointer?.Captured == root) pointer.Capture(null);
    }

    public void Dispose()
    {
        Reset();
        root.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
        root.RemoveHandler(InputElement.PointerMovedEvent, Moved);
        root.RemoveHandler(InputElement.PointerReleasedEvent, Released);
        root.RemoveHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
    }
}
