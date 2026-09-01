using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace QuiverLauncher.Services;

/// <summary>
/// Console-style text engagement: D-pad/arrows highlight a field; Confirm edits; Back leaves edit.
/// Attach with <see cref="EngageOnConfirmProperty"/> (global TextBox style) so new fields inherit it.
/// </summary>
internal static class GamepadTextInput
{
    public static readonly AttachedProperty<bool> EngageOnConfirmProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("EngageOnConfirm", typeof(GamepadTextInput));

    private static readonly Dictionary<TextBox, FieldState> States = new();
    private static bool _applying;

    public static bool IsEditing { get; private set; }

    public static TextBox? Active { get; private set; }

    static GamepadTextInput()
    {
        EngageOnConfirmProperty.Changed.AddClassHandler<TextBox>(OnEngageOnConfirmChanged);
    }

    public static bool GetEngageOnConfirm(TextBox box) =>
        box.GetValue(EngageOnConfirmProperty);

    public static void SetEngageOnConfirm(TextBox box, bool value) =>
        box.SetValue(EngageOnConfirmProperty, value);

    public static bool IsEditingTextBox(IInputElement? focused) =>
        IsEditing && focused is TextBox box && ReferenceEquals(box, Active);

    /// <summary>
    /// Test seam. When set, replaces <see cref="ShouldSkipNativeFocus"/>.
    /// </summary>
    public static Func<bool>? SkipNativeFocusOverride { get; set; }

    /// <summary>
    /// True while Confirm-edit is active. Gamescope opens the OSK on native TextBox
    /// focus, so GotFocus handlers must not request Steam's keyboard until then.
    /// </summary>
    public static bool ShouldOpenSteamOskOnGotFocus => IsEditing;

    /// <summary>
    /// Gamescope (Gaming Mode) opens the OSK on native TextBox focus. Desktop Mode
    /// must still Focus so XY navigation and the caret work.
    /// </summary>
    public static bool ShouldSkipNativeFocus() =>
        SkipNativeFocusOverride?.Invoke() ?? SteamDeckEnvironment.IsGamingMode();

    /// <summary>
    /// Highlight-only TextBoxes have no keyboard focus in Gaming Mode; Avalonia XY
    /// would start from the sidebar. Walk the collected list instead.
    /// </summary>
    public static bool ShouldSkipXyFocusOnHighlight(Control? current) =>
        current is TextBox && !IsEditing;

    /// <summary>
    /// Orange-ring highlight without a caret or OSK. Safe to call repeatedly.
    /// Gaming Mode does not Focus the TextBox (Gamescope would spawn the OSK).
    /// Desktop Mode still Focuses so D-pad XY and A/Enter caret work.
    /// </summary>
    public static void Highlight(TextBox textBox)
    {
        if (!textBox.IsEnabled || !textBox.IsVisible)
            return;

        if (_applying)
            return;

        _applying = true;
        try
        {
            if (IsEditing && Active != null && !ReferenceEquals(Active, textBox))
                EndEditCore(restoreHighlight: false);

            Active = textBox;
            IsEditing = false;
            ApplyHighlightVisuals(textBox);

            if (GamepadFocusChrome.IsActive)
                GamepadFocusChrome.SetFocused(textBox, true);

            if (!ShouldSkipNativeFocus())
                textBox.Focus();
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// Enter edit mode: caret, restored read-only, Steam OSK.
    /// </summary>
    public static void BeginEdit(TextBox textBox)
    {
        if (!textBox.IsEnabled || !textBox.IsVisible)
            return;

        var state = GetState(textBox);
        state.BeginEditRequested = true;
        try
        {
            Active = textBox;
            IsEditing = true;
            ApplyEditVisuals(textBox);
            textBox.Focus();
            GamepadControlActivation.MoveCaretToEnd(textBox);
            SteamOnScreenKeyboard.TryOpen();
        }
        finally
        {
            state.BeginEditRequested = false;
        }
    }

    /// <summary>
    /// Leave edit mode and keep the field highlighted. False when not editing.
    /// </summary>
    public static bool TryEndEdit()
    {
        if (!IsEditing || Active == null)
            return false;

        EndEditCore(restoreHighlight: true);
        return true;
    }

    public static void Reset()
    {
        if (Active != null && IsEditing)
            ApplyEditVisuals(Active);

        IsEditing = false;
        Active = null;
        SkipNativeFocusOverride = null;
    }

    private static void EndEditCore(bool restoreHighlight)
    {
        var box = Active;
        IsEditing = false;
        if (box == null)
            return;

        if (restoreHighlight)
            Highlight(box);
        else
            ApplyHighlightVisuals(box);
    }

    private static void OnEngageOnConfirmChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        var enabled = args.GetNewValue<bool>();
        var state = GetState(box);
        if (enabled == state.Attached)
            return;

        if (enabled)
        {
            box.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            box.GotFocus += OnGotFocus;
            box.LostFocus += OnLostFocus;
            box.Unloaded += OnUnloaded;
            state.Attached = true;
        }
        else
        {
            Detach(box, state);
        }
    }

    private static void Detach(TextBox box, FieldState state)
    {
        if (!state.Attached)
            return;

        box.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        box.GotFocus -= OnGotFocus;
        box.LostFocus -= OnLostFocus;
        box.Unloaded -= OnUnloaded;
        state.Attached = false;

        if (ReferenceEquals(Active, box))
        {
            IsEditing = false;
            Active = null;
        }

        States.Remove(box);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        GetState(box).PointerEngaging = e.Pointer.Type == PointerType.Mouse;
    }

    private static void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        var state = GetState(box);
        var pointer = state.PointerEngaging;
        state.PointerEngaging = false;

        if (state.BeginEditRequested)
            return;

        if (pointer)
        {
            BeginEdit(box);
            return;
        }

        if (GamepadFocusChrome.IsActive && !IsEditing)
            Highlight(box);
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        if (IsEditing && ReferenceEquals(Active, box))
        {
            IsEditing = false;
            ApplyHighlightVisuals(box);
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            Detach(box, GetState(box));
    }

    private static void ApplyHighlightVisuals(TextBox box)
    {
        var state = GetState(box);
        state.SavedReadOnly ??= box.IsReadOnly;
        state.SavedCaretBrush ??= box.CaretBrush;

        box.IsReadOnly = true;
        box.CaretBrush = Brushes.Transparent;
        var caret = box.CaretIndex;
        box.SelectionStart = caret;
        box.SelectionEnd = caret;
    }

    private static void ApplyEditVisuals(TextBox box)
    {
        var state = GetState(box);
        if (state.SavedReadOnly != null)
            box.IsReadOnly = state.SavedReadOnly.Value;

        if (state.SavedCaretBrush != null)
            box.CaretBrush = state.SavedCaretBrush;
        else
            box.ClearValue(TextBox.CaretBrushProperty);
    }

    private static FieldState GetState(TextBox box)
    {
        if (!States.TryGetValue(box, out var state))
        {
            state = new FieldState();
            States[box] = state;
        }

        return state;
    }

    private sealed class FieldState
    {
        public bool Attached;
        public bool PointerEngaging;
        public bool BeginEditRequested;
        public bool? SavedReadOnly;
        public IBrush? SavedCaretBrush;
    }
}
