using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace QuiverLauncher.Views;

/// <summary>Builds what's inside a library card only while the card is on or near the screen.</summary>
/// <remarks>
/// A card's controls cost about 1.6 MB, almost all of it styling, and the library has a card for every
/// app so gamepad navigation can find each card's position. The card itself (its border, size and
/// place in the grid) always exists; only its insides come and go. An empty card keeps the height cards
/// of the same <see cref="Group"/> last had, so the layout doesn't move when it fills in.
/// </remarks>
public sealed class LazyCardContent : ContentControl
{
    public static readonly StyledProperty<string> GroupProperty =
        AvaloniaProperty.Register<LazyCardContent, string>(nameof(Group), "");

    /// <summary>The height an empty card takes before any card of its group has been built.</summary>
    public static readonly StyledProperty<double> PlaceholderHeightProperty =
        AvaloniaProperty.Register<LazyCardContent, double>(nameof(PlaceholderHeight));

    // Built a little before a card scrolls into view, and dropped once it is well past it.
    private const double BuildWithin = 0.5;
    private const double DropBeyond = 1.5;

    private static readonly Dictionary<string, double> KnownHeights = [];

    protected override Type StyleKeyOverride => typeof(ContentControl);

    public string Group { get => GetValue(GroupProperty); set => SetValue(GroupProperty, value); }
    public double PlaceholderHeight { get => GetValue(PlaceholderHeightProperty); set => SetValue(PlaceholderHeightProperty, value); }

    public LazyCardContent()
    {
        Focusable = false;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    /// <summary>Whether the card's insides are built.</summary>
    public bool IsBuilt => Content != null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // A built card given another app shows that app instead.
        if (change.Property == DataContextProperty && Content != null)
            Content = DataContext;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var viewport = e.EffectiveViewport;
        var bounds = new Rect(Bounds.Size);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;
        if (viewport.Inflate(new Thickness(0, viewport.Height * BuildWithin)).Intersects(bounds))
            Build();
        else if (!viewport.Inflate(new Thickness(0, viewport.Height * DropBeyond)).Intersects(bounds))
            Drop();
    }

    private void Build()
    {
        if (Content == null && DataContext != null)
            Content = DataContext;
    }

    private void Drop()
    {
        // Keep a card that has keyboard focus or an open menu.
        if (Content == null || IsKeyboardFocusWithin || ContextMenuIsOpen())
            return;
        Content = null;
    }

    private bool ContextMenuIsOpen() =>
        this.GetVisualDescendants().OfType<Control>().Any(control => control.ContextMenu?.IsOpen == true);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Content == null)
        {
            var height = KnownHeights.TryGetValue(Group, out var known) ? known : PlaceholderHeight;
            return new Size(0, double.IsFinite(availableSize.Height) ? Math.Min(height, availableSize.Height) : height);
        }
        var size = base.MeasureOverride(availableSize);
        if (size.Height > 0 && Group.Length > 0)
            KnownHeights[Group] = size.Height;
        return size;
    }
}
