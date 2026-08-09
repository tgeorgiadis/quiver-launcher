using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace QuiverLauncher.Services;

public sealed class GamepadModalDialogNavigation
{
    private static GamepadModalDialogNavigation? _instance;

    private readonly List<Window> _dialogStack = [];
    private readonly Dictionary<Window, Action<bool>> _questionResultCallbacks = new();
    private readonly HashSet<Window> _keyboardAttachedDialogs = [];
    private List<Control> _dialogControls = [];
    private int _focusedControlIndex;
    private InputService? _inputService;
    private readonly EventHandler<PointerEventArgs> _dialogControlPointerEntered;
    private readonly EventHandler<KeyEventArgs> _dialogKeyDownHandler;
    private readonly EventHandler<KeyEventArgs> _dialogKeyUpHandler;

    public static GamepadModalDialogNavigation Instance => _instance ??= new GamepadModalDialogNavigation();

    public bool HasActiveDialog => _dialogStack.Count > 0;

    public Window? ActiveDialog =>
        _dialogStack.Count > 0 ? _dialogStack[^1] : null;

    public int DialogStackCount => _dialogStack.Count;

    /// <summary>
    /// Resolves a key press to a <see cref="GamepadAction"/> using the app's keyboard bindings.
    /// </summary>
    public Func<Key, KeyModifiers, GamepadAction?>? ResolveKeyboardAction { get; set; }

    /// <summary>
    /// Called when keyboard navigation is used on a modal so focus chrome can activate.
    /// </summary>
    public Action? OnKeyboardNavigationActivated { get; set; }

    public GamepadModalDialogNavigation()
    {
        _dialogControlPointerEntered = OnDialogControlPointerEntered;
        _dialogKeyDownHandler = OnDialogKeyDown;
        _dialogKeyUpHandler = OnDialogKeyUp;
    }

    public void Configure(InputService inputService)
    {
        _inputService = inputService;
    }

    /// <summary>
    /// Syncs <c>gamepad-chrome</c> on all open modal dialog windows.
    /// </summary>
    public void SyncChromeClass(bool active)
    {
        foreach (var dialog in _dialogStack)
            GamepadFocusChrome.ApplyToWindow(dialog, active);
    }

    public static void Attach(Window dialog) =>
        Attach(dialog, setQuestionResult: null);

    public static void Attach(Window dialog, Action<bool>? setQuestionResult)
    {
        Instance.PrepareDialog(dialog, setQuestionResult);
    }

    public void PrepareDialog(Window dialog, Action<bool>? setQuestionResult = null)
    {
        if (setQuestionResult != null)
            _questionResultCallbacks[dialog] = setQuestionResult;
        else
            _questionResultCallbacks.Remove(dialog);

        if (_dialogStack.Contains(dialog))
        {
            BringToTop(dialog);
            AttachDialogKeyboardHandlers(dialog);
            RefreshDialogButtons();
            return;
        }

        _dialogStack.Add(dialog);
        AttachDialogKeyboardHandlers(dialog);
        GamepadFocusChrome.ApplyToWindow(dialog, GamepadFocusChrome.IsActive);
        RefreshDialogButtons();

        void OnDialogOpened(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(ActiveDialog, dialog))
                return;

            RefreshDialogButtons();

            void OnLayoutUpdated(object? s, EventArgs args)
            {
                if (!ReferenceEquals(ActiveDialog, dialog))
                    return;

                RefreshDialogButtons();
                dialog.LayoutUpdated -= OnLayoutUpdated;
            }

            dialog.LayoutUpdated += OnLayoutUpdated;
        }

        void OnDialogClosed(object? sender, EventArgs e)
        {
            dialog.Opened -= OnDialogOpened;
            dialog.Closed -= OnDialogClosed;
            UnregisterModalDialog(dialog);
        }

        dialog.Opened += OnDialogOpened;
        dialog.Closed += OnDialogClosed;
    }

    public void RefreshDialogButtons()
    {
        var activeDialog = ActiveDialog;
        if (activeDialog == null)
            return;

        GamepadFocusChrome.ApplyToWindow(activeDialog, GamepadFocusChrome.IsActive);

        DetachDialogControlHoverHandlers(_dialogControls);
        _dialogControls = CollectDialogFocusableControls(activeDialog);
        AttachDialogControlHoverHandlers(_dialogControls);
        _focusedControlIndex = GetDefaultFocusIndex(_dialogControls);

        // With chrome active (gamepad or keyboard nav): paint default focus.
        // Without: wait for mouse hover / first keyboard or D-pad action.
        if (GamepadFocusChrome.IsActive)
            Dispatcher.UIThread.Post(FocusCurrentControl, DispatcherPriority.Loaded);
        else
            ClearGamepadFocusClasses(_dialogControls);
    }

    public void UnregisterModalDialog(Window dialog)
    {
        _questionResultCallbacks.Remove(dialog);
        DetachDialogKeyboardHandlers(dialog);

        var wasTop = ReferenceEquals(ActiveDialog, dialog);
        if (!_dialogStack.Remove(dialog))
            return;

        GamepadFocusChrome.ApplyToWindow(dialog, false);

        if (_dialogStack.Count == 0)
        {
            DetachDialogControlHoverHandlers(_dialogControls);
            ClearGamepadFocusClasses(_dialogControls);
            _dialogControls = [];
            _focusedControlIndex = 0;
            return;
        }

        if (wasTop)
            RefreshDialogButtons();
    }

    private void BringToTop(Window dialog)
    {
        if (!_dialogStack.Remove(dialog))
            return;

        _dialogStack.Add(dialog);
    }

    private void AttachDialogKeyboardHandlers(Window dialog)
    {
        if (!_keyboardAttachedDialogs.Add(dialog))
            return;

        dialog.AddHandler(InputElement.KeyDownEvent, _dialogKeyDownHandler, RoutingStrategies.Tunnel);
        dialog.AddHandler(InputElement.KeyUpEvent, _dialogKeyUpHandler, RoutingStrategies.Tunnel);
    }

    private void DetachDialogKeyboardHandlers(Window dialog)
    {
        if (!_keyboardAttachedDialogs.Remove(dialog))
            return;

        dialog.RemoveHandler(InputElement.KeyDownEvent, _dialogKeyDownHandler);
        dialog.RemoveHandler(InputElement.KeyUpEvent, _dialogKeyUpHandler);
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !ReferenceEquals(sender, ActiveDialog))
            return;

        if (TryHandleDialogKeyDown(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    private void OnDialogKeyUp(object? sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(sender, ActiveDialog))
            return;

        var action = ResolveKeyboardAction?.Invoke(e.Key, e.KeyModifiers);
        if (action is GamepadAction.NavUp or GamepadAction.NavDown or GamepadAction.NavLeft or GamepadAction.NavRight)
            _inputService?.ResetNavigationTimer();
    }

    /// <summary>
    /// Handles a keyboard binding against the active modal dialog.
    /// </summary>
    internal bool TryHandleDialogKeyDown(Key key, KeyModifiers modifiers)
    {
        if (ActiveDialog == null)
            return false;

        // Open ComboBox dropdowns own input; do not ActivateKeyboardChrome (that re-focuses
        // the host ComboBox and steals Up/Down back to dialog field navigation).
        if (TryHandleOpenComboBoxKey(key, modifiers))
            return true;

        // While typing in a dialog TextBox: Enter/A and Escape/B leave edit mode so the user
        // can navigate other fields. A second Escape/B then cancels the dialog.
        var editingText = TopLevel.GetTopLevel(ActiveDialog)?.FocusManager?.GetFocusedElement() is TextBox;
        if (editingText)
        {
            if (key is Key.Escape or Key.Enter)
                return TryExitTextBoxEditMode();

            var editingAction = ResolveKeyboardAction?.Invoke(key, modifiers);
            if (editingAction is GamepadAction.Cancel or GamepadAction.Confirm)
                return TryExitTextBoxEditMode();

            // Let typing / caret keys through to the TextBox.
            return false;
        }

        var action = ResolveKeyboardAction?.Invoke(key, modifiers);
        if (action == null)
            return false;

        switch (action)
        {
            case GamepadAction.Confirm:
                ActivateKeyboardChrome();
                return TryHandleConfirm();

            case GamepadAction.Cancel:
                ActivateKeyboardChrome();
                return TryHandleCancel();

            case GamepadAction.NavUp:
                ActivateKeyboardChrome();
                return TryHandleNavigation(NavigationDirection.Up);

            case GamepadAction.NavDown:
                ActivateKeyboardChrome();
                return TryHandleNavigation(NavigationDirection.Down);

            case GamepadAction.NavLeft:
                ActivateKeyboardChrome();
                return TryHandleNavigation(NavigationDirection.Left);

            case GamepadAction.NavRight:
                ActivateKeyboardChrome();
                return TryHandleNavigation(NavigationDirection.Right);

            case GamepadAction.Options:
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Clears keyboard focus from a dialog TextBox and restores gamepad field highlight.
    /// Returns false when no TextBox is being edited.
    /// </summary>
    internal bool TryExitTextBoxEditMode()
    {
        if (ActiveDialog == null)
            return false;

        var focusManager = TopLevel.GetTopLevel(ActiveDialog)?.FocusManager;
        if (focusManager?.GetFocusedElement() is not TextBox textBox)
            return false;

        EnsureDialogControls();
        var index = _dialogControls.FindIndex(c => ReferenceEquals(c, textBox));
        if (index >= 0)
            _focusedControlIndex = index;

        OnKeyboardNavigationActivated?.Invoke();
        if (!GamepadFocusChrome.IsActive)
        {
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            GamepadFocusChrome.SetActive(true);
            SyncChromeClass(true);
        }

        focusManager.ClearFocus();
        FocusCurrentControl();
        return true;
    }

    private bool TryHandleOpenComboBoxKey(Key key, KeyModifiers modifiers)
    {
        var comboNav = GamepadComboBoxNavigation.Instance;
        if (!comboNav.HasActiveComboBox)
            return false;

        if (key == Key.Escape)
            return comboNav.TryHandleCancel();

        var action = ResolveKeyboardAction?.Invoke(key, modifiers);
        return action switch
        {
            GamepadAction.Confirm => comboNav.TryHandleConfirm(),
            GamepadAction.Cancel => comboNav.TryHandleCancel(),
            GamepadAction.NavUp => comboNav.TryHandleNavigation(NavigationDirection.Up),
            GamepadAction.NavDown => comboNav.TryHandleNavigation(NavigationDirection.Down),
            GamepadAction.NavLeft => comboNav.TryHandleNavigation(NavigationDirection.Left),
            GamepadAction.NavRight => comboNav.TryHandleNavigation(NavigationDirection.Right),
            _ => false,
        };
    }

    private void ActivateKeyboardChrome()
    {
        OnKeyboardNavigationActivated?.Invoke();
        if (!GamepadFocusChrome.IsActive)
        {
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            GamepadFocusChrome.SetActive(true);
            SyncChromeClass(true);
        }

        EnsureDialogControls();
        if (_dialogControls.Count > 0 &&
            (_focusedControlIndex < 0 || _focusedControlIndex >= _dialogControls.Count))
        {
            _focusedControlIndex = GetDefaultFocusIndex(_dialogControls);
        }

        FocusCurrentControl();
    }

    public bool TryHandleNavigation(NavigationDirection direction)
    {
        if (ActiveDialog == null)
            return false;

        // Prefer open ComboBox item navigation over moving between dialog fields.
        if (GamepadComboBoxNavigation.Instance.TryHandleNavigation(direction))
            return true;

        // Leave TextBox edit mode first, then move highlight to the next field.
        TryExitTextBoxEditMode();

        EnsureDialogControls();
        if (_dialogControls.Count == 0)
            return true;

        if (_dialogControls.Count == 1 && GetFocusedControl() is not ListBox)
            return true;

        // ListBox owns Up/Down for moving among its own items; edges fall through to spatial nav.
        if (TryMoveListBoxSelection(direction))
            return true;

        if (_dialogControls.Count == 1)
            return true;

        var positions = GetControlPositions(_dialogControls, GetControlCenter);
        _focusedControlIndex = MoveFocusIndex(_focusedControlIndex, direction, positions);
        FocusCurrentControl();
        return true;
    }

    private bool TryMoveListBoxSelection(NavigationDirection direction)
    {
        if (direction is not (NavigationDirection.Up or NavigationDirection.Down))
            return false;

        if (GetFocusedControl() is not ListBox listBox)
            return false;

        var count = listBox.ItemCount;
        if (count <= 0)
            return false;

        var index = listBox.SelectedIndex;
        if (index < 0)
            index = 0;

        if (direction == NavigationDirection.Down)
        {
            if (index >= count - 1)
                return false;
            listBox.SelectedIndex = index + 1;
        }
        else
        {
            if (index <= 0)
                return false;
            listBox.SelectedIndex = index - 1;
        }

        if (listBox.SelectedItem is Control selectedControl)
            listBox.ScrollIntoView(selectedControl);
        else if (listBox.SelectedItem != null)
            listBox.ScrollIntoView(listBox.SelectedItem);

        FocusCurrentControl();
        return true;
    }

    public bool TryHandleConfirm()
    {
        var activeDialog = ActiveDialog;
        if (activeDialog == null)
            return false;

        if (GamepadComboBoxNavigation.Instance.TryHandleConfirm())
            return true;

        // A / Enter while typing: commit edit and return to field navigation (do not close).
        if (TryExitTextBoxEditMode())
            return true;

        EnsureDialogControls();
        if (_dialogControls.Count == 0)
        {
            InvokeQuestionResult(activeDialog, false);
            CloseDialogIfStillOpen();
            return true;
        }

        var control = GetFocusedControl();
        if (control == null)
            return false;

        if (control is TextBox textBox)
        {
            GamepadControlActivation.ActivateTextBox(textBox);
            return true;
        }

        if (control is ComboBox comboBox)
        {
            GamepadComboBoxNavigation.Open(comboBox);
            return true;
        }

        // Confirm on a file list installs the current selection via the Install button.
        if (control is ListBox)
        {
            var install = FindAffirmativeButton(_dialogControls) ??
                          _dialogControls.OfType<Button>().FirstOrDefault(b =>
                              string.Equals(GetButtonLabel(b), "Install", StringComparison.OrdinalIgnoreCase));
            if (install == null)
                return false;

            InvokeQuestionResult(activeDialog, true);
            ApplyDialogResultHint(activeDialog, install);
            ActivateAndCloseDialogButton(activeDialog, install);
            return true;
        }

        if (control is not Button button)
            return false;

        var accepted = IsAffirmativeDialogButtonLabel(GetButtonLabel(button));
        InvokeQuestionResult(activeDialog, accepted);
        ApplyDialogResultHint(activeDialog, button);
        ActivateAndCloseDialogButton(activeDialog, button);
        return true;
    }

    public bool TryHandleCancel()
    {
        var activeDialog = ActiveDialog;
        if (activeDialog == null)
            return false;

        if (GamepadComboBoxNavigation.Instance.TryHandleCancel())
            return true;

        // Escape / B while typing: leave the TextBox first; second press cancels the dialog.
        if (TryExitTextBoxEditMode())
            return true;

        EnsureDialogControls();

        if (_dialogControls.Count == 0)
        {
            InvokeQuestionResult(activeDialog, false);
            CloseDialogIfStillOpen();
            return true;
        }

        var cancelIndex = FindCancelControlIndex(_dialogControls);
        Control? control = cancelIndex >= 0
            ? _dialogControls[cancelIndex]
            : _dialogControls.Count == 1
                ? _dialogControls[0]
                : GetFocusedControl();

        if (control is not Button button)
        {
            InvokeQuestionResult(activeDialog, false);
            CloseDialogIfStillOpen();
            return true;
        }

        InvokeQuestionResult(activeDialog, false);
        ApplyDialogResultHint(activeDialog, button);
        ActivateAndCloseDialogButton(activeDialog, button);
        return true;
    }

    private void InvokeQuestionResult(Window dialog, bool accepted)
    {
        if (_questionResultCallbacks.TryGetValue(dialog, out var setResult))
            setResult(accepted);
    }

    private static void ActivateAndCloseDialogButton(Window dialog, Button button)
    {
        GamepadControlActivation.ActivateButton(button);
        if (dialog.IsVisible)
            dialog.Close();
    }

    /// <summary>
    /// Stores Yes/No (or equivalent) on <see cref="Window.Tag"/> before activation/close so
    /// force-closing a dialog cannot drop the user's choice when Click handlers are skipped.
    /// </summary>
    internal static void ApplyDialogResultHint(Window dialog, Button button)
    {
        var label = GetButtonLabel(button);
        if (IsAffirmativeDialogButtonLabel(label))
        {
            dialog.Tag = true;
            return;
        }

        if (IsDismissDialogButtonLabel(label))
        {
            dialog.Tag = false;
            return;
        }
    }

    internal static bool IsAffirmativeDialogButtonLabel(string label) =>
        AffirmativeDialogLabels.Any(preferred =>
            string.Equals(label, preferred, StringComparison.OrdinalIgnoreCase));

    internal static bool IsDismissDialogButtonLabel(string label) =>
        DismissDialogLabels.Any(dismiss =>
            string.Equals(label, dismiss, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] AffirmativeDialogLabels =
    [
        "ok",
        "yes",
        "add",
        "install",
        "download anyway",
        "open settings",
        "update quiver launcher",
        "update apps",
        "save",
        "save & download",
    ];

    private static readonly string[] DismissDialogLabels =
    [
        "cancel",
        "no",
        "close",
        "not now",
    ];

    private void CloseDialogIfStillOpen()
    {
        var activeDialog = ActiveDialog;
        if (activeDialog != null && activeDialog.IsVisible)
            activeDialog.Close();
    }

    private void EnsureDialogControls()
    {
        var activeDialog = ActiveDialog;
        if (activeDialog == null || _dialogControls.Count > 0)
            return;

        _dialogControls = CollectDialogFocusableControls(activeDialog);
        _focusedControlIndex = GetDefaultFocusIndex(_dialogControls);
    }

    public static List<Button> CollectDialogButtons(Control root) =>
        CollectDialogFocusableControls(root).OfType<Button>().ToList();

    public static List<Control> CollectDialogFocusableControls(Control root)
    {
        return root.GetVisualDescendants()
            .OfType<Control>()
            .Where(control =>
                control.IsVisible &&
                control.IsEnabled &&
                control.Focusable &&
                (control is Button or TextBox or ListBox or ComboBox) &&
                !IsNestedInsideNavigableHost(control))
            .OrderBy(control => GetApproximateCenter(control)?.Y ?? 0)
            .ThenBy(control => GetApproximateCenter(control)?.X ?? 0)
            .ToList();
    }

    /// <summary>
    /// Skips chrome inside ComboBox/ListBox (e.g. the ComboBox dropdown ToggleButton)
    /// so navigation targets the host control instead.
    /// </summary>
    internal static bool IsNestedInsideNavigableHost(Control control)
    {
        for (var parent = control.GetVisualParent(); parent != null; parent = parent.GetVisualParent())
        {
            if (parent is ComboBox or ListBox)
                return true;
        }

        return false;
    }

    public static int GetDefaultButtonIndex(IReadOnlyList<Button> buttons) =>
        GetDefaultFocusIndex(buttons.Cast<Control>().ToList());

    public static int GetDefaultFocusIndex(IReadOnlyList<Control> controls)
    {
        if (controls.Count == 0)
            return -1;

        // Prefer list selection when present (e.g. multi-file install picker).
        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is ListBox)
                return i;
        }

        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is Button { IsDefault: true })
                return i;
        }

        var affirmative = FindAffirmativeButtonIndex(controls);
        if (affirmative >= 0)
            return affirmative;

        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is TextBox)
                return i;
        }

        return 0;
    }

    private static Button? FindAffirmativeButton(IReadOnlyList<Control> controls)
    {
        var index = FindAffirmativeButtonIndex(controls);
        return index >= 0 ? controls[index] as Button : null;
    }

    private static int FindAffirmativeButtonIndex(IReadOnlyList<Control> controls)
    {
        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is not Button button)
                continue;

            var label = GetButtonLabel(button);
            if (AffirmativeDialogLabels.Any(preferred =>
                    string.Equals(label, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    public static int FindCancelButtonIndex(IReadOnlyList<Button> buttons) =>
        FindCancelControlIndex(buttons.Cast<Control>().ToList());

    public static int FindCancelControlIndex(IReadOnlyList<Control> controls)
    {
        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is not Button button)
                continue;

            var label = GetButtonLabel(button);
            if (DismissDialogLabels.Any(cancel =>
                    string.Equals(label, cancel, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    public static List<(double X, double Y)> GetButtonPositions(
        IReadOnlyList<Button> buttons,
        Func<Button, (double X, double Y)?> getCenter) =>
        GetControlPositions(buttons, getCenter);

    public static List<(double X, double Y)> GetControlPositions<T>(
        IReadOnlyList<T> controls,
        Func<T, (double X, double Y)?> getCenter)
        where T : Control
    {
        var positions = new List<(double X, double Y)>();
        foreach (var control in controls)
            positions.Add(getCenter(control) ?? (0, positions.Count * 40));

        return positions;
    }

    /// <summary>
    /// Spatial move among dialog controls. Stays put at edges (no wrap).
    /// </summary>
    public static int MoveButtonIndex(
        int currentIndex,
        NavigationDirection direction,
        IReadOnlyList<(double X, double Y)> positions,
        double rowTolerance = 24) =>
        MoveFocusIndex(currentIndex, direction, positions, rowTolerance);

    public static int MoveFocusIndex(
        int currentIndex,
        NavigationDirection direction,
        IReadOnlyList<(double X, double Y)> positions,
        double rowTolerance = 24)
    {
        if (positions.Count == 0)
            return -1;

        if (currentIndex < 0 || currentIndex >= positions.Count)
            return 0;

        if (positions.Count == 1)
            return currentIndex;

        var current = positions[currentIndex];
        int? bestIndex = null;
        var bestScore = double.MaxValue;

        for (var i = 0; i < positions.Count; i++)
        {
            if (i == currentIndex)
                continue;

            var score = CalculateNavigationScore(current, positions[i], direction);
            if (!score.HasValue || score.Value >= bestScore)
                continue;

            bestScore = score.Value;
            bestIndex = i;
        }

        return bestIndex ?? currentIndex;
    }

    private void FocusCurrentControl()
    {
        ClearGamepadFocusClasses(_dialogControls);

        var control = GetFocusedControl();
        if (control == null)
            return;

        if (control is StyledElement styled)
            GamepadFocusChrome.SetFocused(styled, true);

        // TextBoxes: visual highlight only — Confirm (A) calls ActivateTextBox / OSK.
        // With chrome active, also move keyboard focus for buttons.
        if (GamepadFocusChrome.IsActive)
            GamepadControlActivation.ApplyGamepadHighlightFocus(control);
    }

    private void AttachDialogControlHoverHandlers(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
            control.PointerEntered += _dialogControlPointerEntered;
    }

    private void DetachDialogControlHoverHandlers(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
            control.PointerEntered -= _dialogControlPointerEntered;
    }

    private void OnDialogControlPointerEntered(object? sender, PointerEventArgs e)
    {
        if (ActiveDialog == null || sender is not Control hovered)
            return;

        var index = _dialogControls.FindIndex(c =>
            ReferenceEquals(c, hovered) ||
            (c is Visual parent && hovered is Visual child && parent.IsVisualAncestorOf(child)));

        if (index < 0)
            return;

        if (_focusedControlIndex == index &&
            GetFocusedControl() is StyledElement styled &&
            styled.Classes.Contains("gamepad-focused"))
        {
            return;
        }

        _focusedControlIndex = index;
        FocusCurrentControl();
    }

    private static void ClearGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
        {
            if (control is StyledElement styled)
                styled.Classes.Set("gamepad-focused", false);
        }
    }

    private Control? GetFocusedControl()
    {
        if (_focusedControlIndex < 0 || _focusedControlIndex >= _dialogControls.Count)
            return null;

        return _dialogControls[_focusedControlIndex];
    }

    private (double X, double Y)? GetControlCenter(Control control)
    {
        var activeDialog = ActiveDialog;
        if (activeDialog == null)
            return GetApproximateCenter(control);

        var topLeft = control.TranslatePoint(new Avalonia.Point(0, 0), activeDialog);
        if (!topLeft.HasValue)
            return null;

        var bounds = control.Bounds;
        return (topLeft.Value.X + bounds.Width / 2, topLeft.Value.Y + bounds.Height / 2);
    }

    private static (double X, double Y)? GetApproximateCenter(Control control)
    {
        var bounds = control.Bounds;
        if (bounds.Width <= 0 && bounds.Height <= 0)
            return (control.GetHashCode() % 1000, 0);

        return (bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static string GetButtonLabel(Button button)
    {
        return button.Content?.ToString()?.Trim() ?? string.Empty;
    }

    private static double? CalculateNavigationScore(
        (double X, double Y) current,
        (double X, double Y) candidate,
        NavigationDirection direction)
    {
        var dx = candidate.X - current.X;
        var dy = candidate.Y - current.Y;

        double primaryDistance;
        double secondaryDistance;

        switch (direction)
        {
            case NavigationDirection.Up:
                if (dy >= -1)
                    return null;
                primaryDistance = Math.Abs(dy);
                secondaryDistance = Math.Abs(dx);
                break;
            case NavigationDirection.Down:
                if (dy <= 1)
                    return null;
                primaryDistance = Math.Abs(dy);
                secondaryDistance = Math.Abs(dx);
                break;
            case NavigationDirection.Left:
                if (dx >= -1)
                    return null;
                primaryDistance = Math.Abs(dx);
                secondaryDistance = Math.Abs(dy);
                break;
            case NavigationDirection.Right:
                if (dx <= 1)
                    return null;
                primaryDistance = Math.Abs(dx);
                secondaryDistance = Math.Abs(dy);
                break;
            default:
                return null;
        }

        var offAxisPenalty = secondaryDistance > 10 ? secondaryDistance * 2.5 : 0;
        return primaryDistance + (secondaryDistance * 0.3) + offAxisPenalty;
    }
}
