using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace QuiverLauncher.Services;

/// <summary>
/// Single-line text that ellipsizes when idle and marquee-scrolls when <see cref="IsActive"/>.
/// </summary>
public sealed class HoverScrollText : Decorator
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HoverScrollText, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<HoverScrollText, bool>(nameof(IsActive));

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<HoverScrollText>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<HoverScrollText>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<HoverScrollText>();

    private readonly TextBlock _text;
    private readonly TranslateTransform _transform = new();
    private DispatcherTimer? _timer;
    private DateTime _phaseStarted;
    private Phase _phase = Phase.Idle;
    private double _distance;
    private TimeSpan _travel = TimeSpan.FromSeconds(1.5);

    private enum Phase
    {
        Idle,
        Out,
        PauseEnd,
        Back,
        PauseStart,
    }

    static HoverScrollText()
    {
        AffectsMeasure<HoverScrollText>(TextProperty, FontSizeProperty, FontWeightProperty);
        AffectsRender<HoverScrollText>(ForegroundProperty);
    }

    public HoverScrollText()
    {
        ClipToBounds = true;
        _text = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            RenderTransform = _transform,
        };
        Child = _text;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            _text.Text = Text;
            RestartIfActive();
        }
        else if (change.Property == FontSizeProperty)
        {
            _text.FontSize = FontSize;
            RestartIfActive();
        }
        else if (change.Property == FontWeightProperty)
        {
            _text.FontWeight = FontWeight;
            RestartIfActive();
        }
        else if (change.Property == ForegroundProperty)
        {
            _text.Foreground = Foreground;
        }
        else if (change.Property == IsActiveProperty)
        {
            if (IsActive)
                StartScroll();
            else
                StopScroll(reset: true);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncTypography();
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var heightConstraint = availableSize.Height;
        _text.Measure(new Size(Math.Max(width, 0), heightConstraint));
        var height = _text.DesiredSize.Height;
        if (height <= 0)
            height = (FontSize > 0 ? FontSize : 12) * 1.35;

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arrangeWidth = finalSize.Width;
        if (IsActive)
        {
            _text.TextTrimming = TextTrimming.None;
            _text.Measure(new Size(double.PositiveInfinity, finalSize.Height));
            arrangeWidth = Math.Max(_text.DesiredSize.Width, finalSize.Width);
        }
        else
        {
            _text.TextTrimming = TextTrimming.CharacterEllipsis;
        }

        _text.Arrange(new Rect(0, 0, arrangeWidth, finalSize.Height));
        return finalSize;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsActive)
            RestartIfActive();
    }

    private void SyncTypography()
    {
        _text.Text = Text;
        if (FontSize > 0)
            _text.FontSize = FontSize;
        _text.FontWeight = FontWeight;
        if (Foreground != null)
            _text.Foreground = Foreground;
    }

    private void StartScroll()
    {
        StopScroll(reset: false);
        _text.TextTrimming = TextTrimming.None;
        InvalidateArrange();
        Dispatcher.UIThread.Post(BeginScrollIfNeeded, DispatcherPriority.Render);
    }

    private void RestartIfActive()
    {
        if (IsActive)
            StartScroll();
        else
            InvalidateVisual();
    }

    private void BeginScrollIfNeeded()
    {
        if (!IsActive)
            return;

        _text.Measure(new Size(double.PositiveInfinity, Bounds.Height > 0 ? Bounds.Height : double.PositiveInfinity));
        var textWidth = _text.DesiredSize.Width;
        var viewport = Bounds.Width;
        if (textWidth <= viewport + 1 || viewport <= 0)
        {
            _transform.X = 0;
            return;
        }

        _distance = textWidth - viewport;
        _travel = TimeSpan.FromSeconds(Math.Clamp(_distance / 36.0, 1.0, 8.0));
        _phase = Phase.Out;
        _phaseStarted = DateTime.UtcNow;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsActive)
        {
            StopScroll(reset: true);
            return;
        }

        var elapsed = (DateTime.UtcNow - _phaseStarted).TotalMilliseconds;
        switch (_phase)
        {
            case Phase.Out:
            {
                var progress = Math.Clamp(elapsed / _travel.TotalMilliseconds, 0, 1);
                _transform.X = -_distance * progress;
                if (progress >= 1)
                    Advance(Phase.PauseEnd);
                break;
            }
            case Phase.PauseEnd:
                if (elapsed >= 700)
                    Advance(Phase.Back);
                break;
            case Phase.Back:
            {
                var progress = Math.Clamp(elapsed / _travel.TotalMilliseconds, 0, 1);
                _transform.X = -_distance * (1 - progress);
                if (progress >= 1)
                    Advance(Phase.PauseStart);
                break;
            }
            case Phase.PauseStart:
                _transform.X = 0;
                if (elapsed >= 900)
                    Advance(Phase.Out);
                break;
        }
    }

    private void Advance(Phase next)
    {
        _phase = next;
        _phaseStarted = DateTime.UtcNow;
    }

    private void StopScroll(bool reset)
    {
        if (_timer != null)
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
        }

        _phase = Phase.Idle;
        if (reset)
        {
            _transform.X = 0;
            _text.TextTrimming = TextTrimming.CharacterEllipsis;
            InvalidateArrange();
        }
    }
}
