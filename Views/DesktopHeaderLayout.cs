using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace QuiverLauncher.Views;

/// <summary>Reserves desktop chrome space and moves feature tools without reparenting them.</summary>
internal sealed class DesktopHeaderLayout : IDisposable
{
    private readonly Grid _header;
    private readonly Control _title, _tools, _actions;
    private readonly Func<double> _preferredToolsWidth;
    private bool _queued, _disposed, _secondRow;

    public DesktopHeaderLayout(Grid header, Control title, Control tools, Control actions,
        Func<double> preferredToolsWidth)
    {
        _header = header;
        _title = title;
        _tools = tools;
        _actions = actions;
        _preferredToolsWidth = preferredToolsWidth;
        header.SizeChanged += SizeChanged;
        header.Loaded += Loaded;
        tools.SizeChanged += SizeChanged;
        actions.SizeChanged += SizeChanged;
        Refresh();
    }

    private void SizeChanged(object? sender, SizeChangedEventArgs e) => Refresh();
    private void Loaded(object? sender, RoutedEventArgs e) => Refresh();

    public void Refresh()
    {
        if (_disposed || _queued)
            return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            if (_disposed || !_header.IsLoaded || _header.Bounds.Width <= 0)
                return;
            // Only remeasure on size/content changes, never on every layout pass.
            var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
            _title.Measure(unconstrained);
            _actions.Measure(unconstrained);
            _tools.Width = double.NaN;
            _tools.Measure(unconstrained);
            var toolsWidth = _preferredToolsWidth();
            var required = _actions.DesiredSize.Width + Math.Min(240, _title.DesiredSize.Width) + toolsWidth + (toolsWidth > 0 ? 12 : 0);
            var secondRow = toolsWidth > 0 && _header.Bounds.Width < required + (_secondRow ? 16 : 0);
            _secondRow = secondRow;
            Grid.SetRow(_tools, secondRow ? 1 : 0);
            Grid.SetColumn(_tools, secondRow ? 0 : 2);
            Grid.SetColumnSpan(_tools, secondRow ? 4 : 1);
            _tools.Width = secondRow ? double.NaN : toolsWidth;
            _tools.Margin = secondRow ? new Thickness(12, 8, 12, 0) : toolsWidth > 0 ? new Thickness(0, 0, 12, 0) : default;
            _header.InvalidateMeasure();
        }, DispatcherPriority.Loaded);
    }

    public void Dispose()
    {
        _disposed = true;
        _header.SizeChanged -= SizeChanged;
        _header.Loaded -= Loaded;
        _tools.SizeChanged -= SizeChanged;
        _actions.SizeChanged -= SizeChanged;
    }
}
