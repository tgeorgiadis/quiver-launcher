using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace QuiverLauncher.Services;

/// <summary>
/// Any content on one line: cut off with a fade when idle, and marquee-scrolled while <see cref="IsActive"/>, like
/// <see cref="HoverScrollText"/> for a row of chips or coloured text.
/// </summary>
public sealed class HoverScrollRow : Decorator
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<HoverScrollRow, bool>(nameof(IsActive));

    private static readonly IBrush Fade = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0.8), new GradientStop(Colors.Transparent, 1) },
    };

    private readonly TranslateTransform _transform = new();
    private DispatcherTimer? _timer;
    private DateTime _phaseStarted;
    private int _phase; // 0 out, 1 pause at the end, 2 back, 3 pause at the start
    private double _distance;
    private TimeSpan _travel;

    public HoverScrollRow() => ClipToBounds = true;

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private double Overflow => Child == null ? 0 : Math.Max(0, Child.DesiredSize.Width - Bounds.Width);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty && Child != null)
            Child.RenderTransform = _transform;
        else if (change.Property == IsActiveProperty)
            Dispatcher.UIThread.Post(Restart, DispatcherPriority.Render);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null) return default;
        Child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var width = Child.DesiredSize.Width;
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, Math.Max(Child.DesiredSize.Width, finalSize.Width), finalSize.Height));
        return finalSize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Restart();
    }

    private void Restart()
    {
        _timer?.Stop();
        _timer = null;
        _transform.X = 0;
        var overflow = Overflow;
        OpacityMask = overflow > 1 && !IsActive ? Fade : null;
        if (!IsActive || overflow <= 1) return;
        _distance = overflow;
        _travel = TimeSpan.FromSeconds(Math.Clamp(_distance / 36.0, 1.0, 8.0));
        _phase = 0;
        _phaseStarted = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsActive)
        {
            Restart();
            return;
        }
        var elapsed = (DateTime.UtcNow - _phaseStarted).TotalMilliseconds;
        var progress = Math.Clamp(elapsed / _travel.TotalMilliseconds, 0, 1);
        switch (_phase)
        {
            case 0: _transform.X = -_distance * progress; if (progress >= 1) Next(); break;
            case 1: if (elapsed >= 700) Next(); break;
            case 2: _transform.X = -_distance * (1 - progress); if (progress >= 1) Next(); break;
            default: _transform.X = 0; if (elapsed >= 900) Next(); break;
        }
    }

    private void Next()
    {
        _phase = (_phase + 1) % 4;
        _phaseStarted = DateTime.UtcNow;
    }
}
