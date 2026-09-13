using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public sealed record LauncherInputActions(Func<NavigationDirection, bool> Navigate, Action Confirm, Action Cancel,
    Action Options, Action<bool> ConnectionChanged, Action ActivateKeyboardChrome);

/// <summary>Owns input polling, keyboard interception, and singleton callback subscriptions for one shell.</summary>
public sealed class LauncherInputController : IDisposable
{
    private readonly Control _view;
    private readonly Func<AppSettings> _settingsSource;
    private AppSettings _settings => _settingsSource();
    private readonly InputBindingsViewModel _bindings;
    private readonly Func<bool> _hostActive, _promptOpen;
    private readonly Action _dismissPrompt;
    private readonly LauncherInputActions _actions;
    private bool _disposed;
    public InputService? Service { get; }
    public LauncherInputController(Control view, Func<AppSettings> settings, InputBindingsViewModel bindings,
        Func<bool> hostActive, Func<bool> promptOpen, Action dismissPrompt, LauncherInputActions actions, bool enableInput)
    {
        _view = view; _settingsSource = settings; _bindings = bindings;
        _hostActive = hostActive; _promptOpen = promptOpen; _dismissPrompt = dismissPrompt; _actions = actions;
        if (enableInput)
        {
            Service = new InputService(view, _settings);
            Service.NavigationInterceptor = actions.Navigate;
            Service.OnConfirm += actions.Confirm;
            Service.OnCancel += actions.Cancel;
            Service.OnOptions += actions.Options;
            Service.OnGamepadConnectionChanged += actions.ConnectionChanged;
        }
        _keyboardActionResolver = (key, modifiers) =>
        {
            if (_disposed) return null;
            _settings.EnsureInitialized();
            return KeyboardBindingDefaults.FindAction(_settings.KeyboardBindings, key, modifiers);
        };
        GamepadContextMenuNavigation.Instance.ResolveKeyboardAction = _keyboardActionResolver;
        GamepadModalDialogNavigation.Instance.ResolveKeyboardAction = _keyboardActionResolver;
        GamepadModalDialogNavigation.Instance.OnKeyboardNavigationActivated = actions.ActivateKeyboardChrome;
        view.AddHandler(InputElement.KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.KeyUpEvent, MainWindow_KeyUp, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.GotFocusEvent, OnTextBoxGotFocusForSteamOsk, RoutingStrategies.Bubble);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.RemoveHandler(InputElement.KeyDownEvent, MainWindow_KeyDown);
        _view.RemoveHandler(InputElement.KeyUpEvent, MainWindow_KeyUp);
        _view.RemoveHandler(InputElement.GotFocusEvent, OnTextBoxGotFocusForSteamOsk);
        if (GamepadContextMenuNavigation.Instance.ResolveKeyboardAction == _keyboardActionResolver)
            GamepadContextMenuNavigation.Instance.ResolveKeyboardAction = null;
        if (GamepadModalDialogNavigation.Instance.ResolveKeyboardAction == _keyboardActionResolver)
            GamepadModalDialogNavigation.Instance.ResolveKeyboardAction = null;
        if (GamepadModalDialogNavigation.Instance.OnKeyboardNavigationActivated == _actions.ActivateKeyboardChrome)
            GamepadModalDialogNavigation.Instance.OnKeyboardNavigationActivated = null;
        if (Service is { } input)
        {
            input.NavigationInterceptor = null;
            input.OnConfirm -= _actions.Confirm; input.OnCancel -= _actions.Cancel;
            input.OnOptions -= _actions.Options; input.OnGamepadConnectionChanged -= _actions.ConnectionChanged;
            input.Dispose();
        }
    }        private Func<Key, KeyModifiers, GamepadAction?>? _keyboardActionResolver;


        private bool _isProcessingInput = false;

        /// <summary>
        /// After Space/Enter confirm, suppress the matching KeyUp so a newly focused CheckBox
        /// (e.g. catalog Enabled) does not also toggle.
        /// </summary>
        private bool _suppressConfirmKeyUp;


        private void OnTextBoxGotFocusForSteamOsk(object? sender, FocusChangedEventArgs e)
        {
            if (e.Source is not TextBox textBox)
                return;

            if (PlatformCapabilities.IsMobile)
            {
                Dispatcher.UIThread.Post(() => { if (!_disposed && textBox.IsAttachedToVisualTree()) textBox.BringIntoView(); }, DispatcherPriority.Loaded);
                return;
            }

            if (!SteamOnScreenKeyboard.ShouldOffer())
                return;

            // Pointer focus keeps click position / selection for copy-paste.
            // Gamepad/tab: caret at end so OSK typing appends instead of inserting at index 0.
            if (e.NavigationMethod != NavigationMethod.Pointer)
                GamepadControlActivation.MoveCaretToEnd(textBox);

            // Gamescope also opens the OSK on native text focus. Wait until Confirm-edit
            // so highlight-only fields do not pop a keyboard that types nowhere.
            if (GamepadTextInput.ShouldOpenSteamOskOnGotFocus)
                SteamOnScreenKeyboard.TryOpen();
        }


        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_isProcessingInput || !_hostActive() || _disposed)
                return;

            _isProcessingInput = true;

            try
            {
                if (_promptOpen() &&
                    (e.Key == Key.Escape || e.Key == Key.BrowserBack))
                {
                    _dismissPrompt();
                    e.Handled = true;
                    return;
                }

                if (_bindings.ListeningKeyboard.HasValue)
                {
                    if (e.Key == Key.Escape)
                    {
                        _bindings.Cancel();
                        e.Handled = true;
                        return;
                    }

                    if (KeyboardBindingDefaults.IsModifierOnlyKey(e.Key))
                        return;

                    _bindings.CaptureKeyboard(new KeyboardBinding(e.Key, e.KeyModifiers));
                    e.Handled = true;
                    return;
                }

                // Esc cancels gamepad rebind listen (same as B / Cancel).
                if (_bindings.ListeningGamepad.HasValue && e.Key == Key.Escape)
                {
                    _bindings.Cancel();
                    e.Handled = true;
                    return;
                }

                _settings.EnsureInitialized();
                var action = KeyboardBindingDefaults.FindAction(
                    _settings.KeyboardBindings,
                    e.Key,
                    e.KeyModifiers);
                if (action == null)
                    return;

                // While editing: let typing through. Escape/Enter (and bound Cancel/Confirm)
                // leave edit mode. A highlighted TextBox is not editing.


            if (GamepadTextInput.IsEditing)
                {
                    if (e.Key == Key.Escape || action is GamepadAction.Cancel)
                    {
                        _actions.ActivateKeyboardChrome();
                        GamepadTextInput.TryEndEdit();
                        e.Handled = true;
                        return;
                    }

                    if (e.Key == Key.Enter || action is GamepadAction.Confirm)
                    {
                        GamepadTextInput.TryEndEdit();
                        e.Handled = true;
                        return;
                    }

                    return;
                }

                switch (action)
                {
                    case GamepadAction.Confirm:
                        _actions.ActivateKeyboardChrome();
                        _actions.Confirm();
                        _suppressConfirmKeyUp = true;
                        e.Handled = true;
                        break;

                    case GamepadAction.Cancel:
                        _actions.ActivateKeyboardChrome();
                        _actions.Cancel();
                        e.Handled = true;
                        break;

                    case GamepadAction.Options:
                        _actions.ActivateKeyboardChrome();
                        _actions.Options();
                        e.Handled = true;
                        break;

                    case GamepadAction.NavUp:
                        _actions.ActivateKeyboardChrome();
                        Service?.HandleNavigation(Services.NavigationDirection.Up);
                        e.Handled = true;
                        break;

                    case GamepadAction.NavDown:
                        _actions.ActivateKeyboardChrome();
                        Service?.HandleNavigation(Services.NavigationDirection.Down);
                        e.Handled = true;
                        break;

                    case GamepadAction.NavLeft:
                        _actions.ActivateKeyboardChrome();
                        Service?.HandleNavigation(Services.NavigationDirection.Left);
                        e.Handled = true;
                        break;

                    case GamepadAction.NavRight:
                        _actions.ActivateKeyboardChrome();
                        Service?.HandleNavigation(Services.NavigationDirection.Right);
                        e.Handled = true;
                        break;
                }
            }
            finally
            {
                _isProcessingInput = false;
            }
        }


        private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
        {
            if (_suppressConfirmKeyUp && IsKeyboardConfirmBoundKey(e.Key))
            {
                _suppressConfirmKeyUp = false;
                e.Handled = true;
                return;
            }

            if (IsKeyboardNavBoundKey(e.Key))
                Service?.ResetNavigationTimer();
        }


        private bool IsKeyboardConfirmBoundKey(Key key)
        {
            _settings.EnsureInitialized();
            return _settings.KeyboardBindings.TryGetValue(GamepadAction.Confirm, out var list) &&
                   list != null &&
                   list.Any(b => b.Key == key);
        }


        private bool IsKeyboardNavBoundKey(Key key)
        {
            _settings.EnsureInitialized();
            foreach (var action in new[]
                     {
                         GamepadAction.NavUp,
                         GamepadAction.NavDown,
                         GamepadAction.NavLeft,
                         GamepadAction.NavRight,
                     })
            {
                if (_settings.KeyboardBindings.TryGetValue(action, out var list) &&
                    list != null &&
                    list.Any(b => b.Key == key))
                {
                    return true;
                }
            }

            return false;
        }

}