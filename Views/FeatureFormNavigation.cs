using Avalonia.Controls;
using Avalonia.Input;
using QuiverLauncher.Services;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Views;
/// <summary>Local traversal for forms with fields followed by a horizontal action row.</summary>
public sealed class FeatureFormNavigation : IFeatureNavigationHandler
{
    private readonly Control _owner;
    private readonly Func<IReadOnlyList<Control>> _controls;
    private readonly IFeatureNavigationHost _host;
    private readonly GamepadNavigationZone _zone;
    private readonly Action _cancel;
    public int FocusIndex { get; set; } = -1;

    public FeatureFormNavigation(Control owner, Func<IReadOnlyList<Control>> controls, IFeatureNavigationHost host, GamepadNavigationZone zone, Action cancel)
    {
        _owner = owner;
        _controls = controls;
        _host = host;
        _zone = zone;
        _cancel = cancel;
    }

    public IReadOnlyList<Control> CollectControls() => _controls().Where(c => c.IsEffectivelyVisible && c.IsEnabled && (c.Focusable || c is CheckBox)).ToList();
    public bool Navigate(NavigationDirection direction)
    {
        var controls = CollectControls();
        if (controls.Count == 0)
            return true;
        var actions = controls.Reverse().TakeWhile(c => c is Button).Count();
        var fields = controls.Count - actions;
        var current = FocusIndex >= 0 && FocusIndex < controls.Count ? controls[FocusIndex] : null;
        if (FocusIndex < fields && !GamepadTextInput.ShouldSkipXyFocusOnHighlight(current))
        {
            var focusManager = TopLevel.GetTopLevel(_owner)?.FocusManager;
            var before = GamepadControlActivation.IndexOfControlContainingFocus(controls, focusManager?.GetFocusedElement());
            if (XyFocusNavigation.TryMove(_owner, direction, _owner))
            {
                var after = GamepadControlActivation.IndexOfControlContainingFocus(controls, focusManager?.GetFocusedElement());
                if (after >= 0 && after != before)
                {
                    ApplySelection(after);
                    return true;
                }
            }
        }

        var next = _host.Navigation.MoveFormIndex(FocusIndex, direction, fields, actions);
        if (next >= 0 && next != FocusIndex)
            ApplySelection(next);
        return true;
    }

    public void ApplySelection(int index)
    {
        var controls = CollectControls();
        FocusIndex = _host.Navigation.ClampIndex(index, controls.Count);
        _host.Navigation.ActiveZone = _zone;
        _host.ClearFocus();
        _host.ClearSidebarFocus();
        ClearFocus();
        if (FocusIndex < 0)
            return;
        controls[FocusIndex].Classes.Add("gamepad-focused");
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[FocusIndex]);
    }

    public bool Confirm()
    {
        var controls = CollectControls();
        var focused = TopLevel.GetTopLevel(_owner)?.FocusManager?.GetFocusedElement();
        var focusedIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        var index = focusedIndex >= 0 ? focusedIndex : _host.Navigation.ClampIndex(FocusIndex, controls.Count);
        if (index < 0)
            return true;
        FocusIndex = index;
        switch (controls[index])
        {
            case CheckBox check:
                GamepadControlActivation.ActivateCheckBox(check);
                break;
            case Button button:
                GamepadControlActivation.ActivateButton(button);
                break;
            case ComboBox combo:
                GamepadComboBoxNavigation.Open(combo);
                break;
            case TextBox text:
                GamepadControlActivation.ActivateTextBox(text);
                break;
            default:
                controls[index].Focus();
                break;
        }

        return true;
    }

    public void ClearFocus()
    {
        foreach (var control in _controls())
            control.Classes.Remove("gamepad-focused");
    }

    public bool Cancel()
    {
        _cancel();
        return true;
    }

    public bool Options() => false;
    public void RestoreFocus() => ApplySelection(Math.Max(0, FocusIndex));
    public bool SynchronizePointer(object? source) => GamepadPointerFocusSync.Hit(_host.Navigation, CollectControls(), _zone, FocusIndex, ApplySelection, source);
}
