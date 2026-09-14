using Avalonia;
using Avalonia.Controls;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuiverLauncher.Services;

/// <summary>Owns desktop window events, tray transitions, and window-state policy.</summary>
public sealed class DesktopHostController : IDisposable
{
    private readonly MainView _view;
    private bool _forceExit;
    private bool _rewritingState;
    private bool _disposed;
    private readonly DesktopInterfaceScaling _scaling;
    private readonly DesktopWindowPlacementController _placement;
    public Window Window { get; }

    public DesktopHostController(Window window, MainView view)
    {
        Window = window;
        _view = view;
        view.AttachDesktopHost(this);
        window.Opened += OnOpened;
        window.Closing += OnClosing;
        window.Closed += OnClosed;
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.PropertyChanged += OnPropertyChanged;
        view.SettingsModel.PropertyChanged += OnSettingsChanged;
        ApplyWindowChrome();
        _placement = new DesktopWindowPlacementController(window, view.SettingsModel);
        _scaling = new DesktopInterfaceScaling(window, view.SettingsModel);
        _placement.ApplyStartupState();
    }

    private void OnOpened(object? sender, EventArgs e) => _view.HandleOpened();
    private void OnActivated(object? sender, EventArgs e) => _view.HandleActivated();
    private void OnDeactivated(object? sender, EventArgs e) => _view.HandleDeactivated();
    private void OnClosing(object? sender, WindowClosingEventArgs e) => HandleClosing(e);
    private void OnClosed(object? sender, EventArgs e)
    {
        _view.HandleClosed();
        Dispose();
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) HandleStateChanged(e.GetNewValue<WindowState>());
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_disposed && (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(_view.SettingsModel.ShowOSTopBar)))
            ApplyWindowChrome();
    }

    private void ApplyWindowChrome()
    {
        var showTopBar = _view.SettingsModel.ShowOSTopBar;
        Window.WindowDecorations = showTopBar ? WindowDecorations.Full : WindowDecorations.BorderOnly;
        Window.ExtendClientAreaToDecorationsHint = !showTopBar;
    }

    public void HandleStateChanged(WindowState state)
    {
        if (_disposed || _rewritingState) return;
        if (state == WindowState.FullScreen && SteamDeckEnvironment.DisallowsExclusiveFullscreen())
        {
            _rewritingState = true;
            try { Window.WindowState = WindowState.Maximized; }
            finally { _rewritingState = false; }
        }
        _view.NotifyHostWindowStateChanged();
    }

    public void ToggleMaximized() => Window.WindowState =
        Window.WindowState is WindowState.Maximized or WindowState.FullScreen ? WindowState.Normal : WindowState.Maximized;

    public void HandleClosing(WindowClosingEventArgs e)
    {
        _placement.Flush();
        if (!_forceExit && _view.SettingsModel.Current.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _view.PrepareForHostClose();
        _view._app?.SetTrayVisible(false);
    }

    public void HideToTray()
    {
        _placement.Suspend();
        _view.DismissInputForHost();
        Window.Hide();
        _view._app?.SetTrayVisible(true);
        _view.RefreshHostUpdateStatus();
    }

    public void RestoreFromTray()
    {
        Window.Show();
        if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
        _placement.Resume();
        Window.Activate();
        ApplyTraySettings();
        _view.RefreshHostUpdateStatus();
    }

    public void ApplyTraySettings() =>
        _view._app?.SetTrayVisible(_view.SettingsModel.Current.CloseToTray || _view.SettingsModel.Current.BackgroundUpdateCheckEnabled);

    public void RequestExit()
    {
        _forceExit = true;
        Window.Close();
    }

    public void CloseAfterLaunch(bool launched)
    {
        if (!launched || !_view.SettingsModel.Current.CloseAfterLaunch) return;
        _view.DismissInputForHost();
        if (_view.SettingsModel.Current.CloseToTray) { HideToTray(); return; }
        _placement.Suspend();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Window.Hide();
        Window.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _placement.Dispose();
        _scaling.Dispose();
        Window.Opened -= OnOpened;
        Window.Closing -= OnClosing;
        Window.Closed -= OnClosed;
        Window.Activated -= OnActivated;
        Window.Deactivated -= OnDeactivated;
        Window.PropertyChanged -= OnPropertyChanged;
        _view.SettingsModel.PropertyChanged -= OnSettingsChanged;
    }
}
