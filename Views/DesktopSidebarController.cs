using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>Keeps the desktop sidebar toggle outside the pane it controls.</summary>
internal sealed class DesktopSidebarController : IDisposable
{
    private readonly SplitView _split;
    private readonly Border _sidebar;
    private readonly Button _toggle;
    private readonly SettingsViewModel _settings;
    private readonly Action _focusToggle;

    public DesktopSidebarController(SplitView split, Border sidebar, Button toggle,
        SettingsViewModel settings, Action focusToggle)
    {
        _split = split;
        _sidebar = sidebar;
        _toggle = toggle;
        _settings = settings;
        _focusToggle = focusToggle;
        // Keep labels laid out at their normal width while SplitView clips the animated pane.
        sidebar.Width = split.OpenPaneLength;
        Apply();
        toggle.Click += Toggle;
        settings.PropertyChanged += SettingsChanged;
    }

    private void Toggle(object? sender, RoutedEventArgs e)
    {
        _settings.DesktopSidebarCollapsed = !_settings.DesktopSidebarCollapsed;
        _focusToggle();
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.DesktopSidebarCollapsed))
            Apply();
    }

    private void Apply()
    {
        var collapsed = _settings.DesktopSidebarCollapsed;
        if (collapsed && _sidebar.IsKeyboardFocusWithin) _focusToggle();
        // Fluent SplitView supplies the short eased open/close animation.
        _split.IsPaneOpen = !collapsed;
        _sidebar.IsEnabled = !collapsed;
        _sidebar.IsHitTestVisible = !collapsed;
        var label = collapsed ? "Show sidebar" : "Hide sidebar";
        ToolTip.SetTip(_toggle, label);
        AutomationProperties.SetName(_toggle, label);
        if (_toggle.Content is Avalonia.Controls.Shapes.Path icon)
            icon.Data = Geometry.Parse(collapsed
                ? "M3,3 L21,3 L21,21 L3,21 Z M9,3 L9,21 M13,8 L17,12 L13,16"
                : "M3,3 L21,3 L21,21 L3,21 Z M9,3 L9,21 M16,8 L12,12 L16,16");
    }

    public void Dispose()
    {
        _toggle.Click -= Toggle;
        _settings.PropertyChanged -= SettingsChanged;
    }
}
