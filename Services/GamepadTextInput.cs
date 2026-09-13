using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

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
        // Tab content can unload and return without its attached property changing.
        // A class handler restores interaction without retaining unloaded controls.
        Control.LoadedEvent.AddClassHandler<TextBox>((box, _) =>
        {
            if (GetEngageOnConfirm(box))
                Attach(box);
        });
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
    /// Skip unless this exact field is in Confirm-edit (caret / OSK).
    /// </summary>
    public static bool ShouldSkipXyFocusOnHighlight(Control? current) =>
        current is TextBox box && !(IsEditing && ReferenceEquals(box, Active));

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

        // Focus recovery may select the current field again. Only an explicit
        // end-edit transition should remove its caret and selection.
        if (IsEditing && ReferenceEquals(Active, textBox))
            return;

        _applying = true;
        try
        {
            if (Active != null && !ReferenceEquals(Active, textBox))
            {
                ApplyEditVisuals(Active);
                IsEditing = false;
            }

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
            if (Active != null && !ReferenceEquals(Active, textBox))
                ApplyEditVisuals(Active);

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
        if (Dispatcher.UIThread.CheckAccess())
        {
            foreach (var pair in States.ToArray())
            {
                if (pair.Value.HighlightApplied)
                    ApplyEditVisuals(pair.Key);
            }

            if (Active != null)
                ApplyEditVisuals(Active);
        }

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
            ApplyEditVisuals(box);
    }

    private static void OnEngageOnConfirmChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
            Attach(box);
        else if (States.TryGetValue(box, out var state))
            Detach(box, state);
    }

    private static void Attach(TextBox box)
    {
        var state = GetState(box);
        if (state.Attached)
            return;

        box.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        box.GotFocus += OnGotFocus;
        box.LostFocus += OnLostFocus;
        box.Unloaded += OnUnloaded;
        state.Attached = true;
    }

    private static void Detach(TextBox box, FieldState state)
    {
        box.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        box.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        box.GotFocus -= OnGotFocus;
        box.LostFocus -= OnLostFocus;
        box.Unloaded -= OnUnloaded;
        state.Attached = false;

        if (state.HighlightApplied)
            ApplyEditVisuals(box);

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

        if (e.Pointer.Type != PointerType.Mouse)
            return;

        // Clicking an already-highlighted field does not fire GotFocus. Enter edit
        // here so Paste/Ctrl+V work without waiting for a focus change.
        GetState(box).PointerEngaging = true;
        BeginEdit(box);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        if (IsEditing && ReferenceEquals(Active, box))
            return;

        if (!ShouldBeginEditOnKey(e))
            return;

        if (!ReferenceEquals(Active, box) && !box.IsFocused)
            return;

        BeginEdit(box);
    }

    /// <summary>
    /// Paste/cut/copy/select-all and typing should enter edit while highlighted.
    /// Navigation, Confirm, and Cancel stay with the window/gamepad handlers.
    /// </summary>
    private static bool ShouldBeginEditOnKey(KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter or Key.Return or Key.Tab
            or Key.Up or Key.Down or Key.Left or Key.Right
            or Key.PageUp or Key.PageDown or Key.Home or Key.End
            or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return false;
        }

        if (e.Key is >= Key.F1 and <= Key.F24)
            return false;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return false;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            return e.Key is Key.V or Key.C or Key.X or Key.A or Key.Z or Key.Y;

        return true;
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

        var state = GetState(box);
        if (!state.HighlightApplied && !(IsEditing && ReferenceEquals(Active, box)))
            return;

        if (ReferenceEquals(Active, box))
            IsEditing = false;

        ApplyEditVisuals(box);
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && States.TryGetValue(box, out var state))
            Detach(box, state);
    }

    private static void ApplyHighlightVisuals(TextBox box)
    {
        var state = GetState(box);
        if (!state.HighlightApplied)
        {
            state.SavedReadOnly = box.IsReadOnly;
            state.SavedCaretBrush = box.CaretBrush;
            state.HighlightApplied = true;
        }

        box.IsReadOnly = true;
        box.CaretBrush = Brushes.Transparent;
        var caret = box.CaretIndex;
        box.SelectionStart = caret;
        box.SelectionEnd = caret;
    }

    private static void ApplyEditVisuals(TextBox box)
    {
        var state = GetState(box);
        if (!state.HighlightApplied && state.SavedReadOnly == null && state.SavedCaretBrush == null)
            return;

        if (state.SavedReadOnly != null)
            box.IsReadOnly = state.SavedReadOnly.Value;

        if (state.SavedCaretBrush != null)
            box.CaretBrush = state.SavedCaretBrush;
        else
            box.ClearValue(TextBox.CaretBrushProperty);

        state.HighlightApplied = false;
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
        public bool HighlightApplied;
        public bool? SavedReadOnly;
        public IBrush? SavedCaretBrush;
    }
}
