using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using NavigationDirection = Avalonia.Input.NavigationDirection;

namespace QuiverLauncher;

/// <summary>
/// Virtualizing uniform grid for catalog-review cards. Only realizes items in the
/// effective viewport so Android can open large catalogs without ANR.
/// </summary>
public sealed class CatalogReviewVirtualizingGridPanel : VirtualizingPanel
{
    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<CatalogReviewVirtualizingGridPanel, Control, object?>("RecycleKey");

    private readonly Dictionary<int, Control> _realized = [];
    private readonly Dictionary<Control, object?> _containerItems = [];
    private readonly Dictionary<object, Stack<Control>> _recyclePool = [];
    private Size _itemSize = new(172, 240);
    private Rect _viewport;
    private int _columns = 1;
    private bool _isInLayout;
    private bool _continuationQueued;

    public CatalogReviewVirtualizingGridPanel()
    {
        EffectiveViewportChanged += (_, e) =>
        {
            if (_viewport == e.EffectiveViewport)
                return;
            _viewport = e.EffectiveViewport;
            if (!_isInLayout)
                InvalidateMeasure();
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        using var timing = QuiverLauncher.Core.Services.CatalogPerformance.Measure("grid-measure", _realized.Count);
        var items = Items;
        var count = items.Count;
        if (count == 0)
        {
            RecycleOutside(0, -1);
            return default;
        }

        _isInLayout = true;
        var layoutStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var width = availableSize.Width;
            if (double.IsInfinity(width) || width <= 0)
                width = _viewport.Width > 0 ? _viewport.Width : _itemSize.Width;

            var probe = GetOrCreate(0);
            double itemWidth;
            if (PlatformCapabilities.IsMobile)
            {
                _columns = CatalogReviewGridLayout.GetColumns(width, IsLandscape(width));
                itemWidth = width / _columns;
                probe.HorizontalAlignment = HorizontalAlignment.Stretch;
                probe.Measure(new Size(itemWidth, double.PositiveInfinity));
            }
            else
            {
                probe.Measure(new Size(width, double.PositiveInfinity));
                itemWidth = probe.DesiredSize.Width;
                if (itemWidth <= 1)
                    itemWidth = Math.Min(width, _itemSize.Width);
                _columns = Math.Max(1, (int)Math.Floor(width / itemWidth));
                itemWidth = width / _columns;
                probe.Measure(new Size(itemWidth, double.PositiveInfinity));
            }

            _itemSize = new Size(itemWidth, Math.Max(1, probe.DesiredSize.Height));

            var viewport = _viewport.Height > 1
                ? _viewport
                : new Rect(0, 0, width, Math.Max(availableSize.Height, _itemSize.Height * 4));

            var firstRow = Math.Max(0, (int)Math.Floor(viewport.Y / _itemSize.Height) - 1);
            var lastRow = (int)Math.Ceiling((viewport.Y + viewport.Height) / _itemSize.Height) + 1;
            var first = Math.Clamp(firstRow * _columns, 0, count - 1);
            var last = Math.Clamp(lastRow * _columns + (_columns - 1), 0, count - 1);

            RecycleOutside(first, last);
            for (var i = first; i <= last; i++)
            {
                // Cold mobile templates are expensive. Yield between new cards so
                // input can be processed while the nearby viewport is realized.
                if (PlatformCapabilities.IsMobile && !_realized.ContainsKey(i) &&
                    System.Diagnostics.Stopwatch.GetElapsedTime(layoutStart).TotalMilliseconds >= 40)
                {
                    if (!_continuationQueued)
                    {
                        _continuationQueued = true;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _continuationQueued = false;
                            InvalidateMeasure();
                        }, Avalonia.Threading.DispatcherPriority.Background);
                    }
                    break;
                }
                var child = GetOrCreate(i);
                if (PlatformCapabilities.IsMobile)
                    child.HorizontalAlignment = HorizontalAlignment.Stretch;
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
            }

            var rows = (int)Math.Ceiling(count / (double)_columns);
            return new Size(width, rows * _itemSize.Height);
        }
        finally
        {
            _isInLayout = false;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, control) in _realized)
        {
            var col = index % _columns;
            var row = index / _columns;
            control.Arrange(new Rect(
                col * _itemSize.Width,
                row * _itemSize.Height,
                _itemSize.Width,
                _itemSize.Height));
        }

        return finalSize;
    }

    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count)
            return null;

        var row = index / Math.Max(1, _columns);
        this.BringIntoView(new Rect(0, row * _itemSize.Height, _itemSize.Width, _itemSize.Height));
        return GetOrCreate(index);
    }

    private bool IsLandscape(double availableWidth)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var size = topLevel.ClientSize;
            if (size.Width > 1 && size.Height > 1 && size.Width > size.Height)
                return true;
        }

        // Panel Bounds.Height is the full scroll extent, so do not use it.
        // Pixel 8 portrait is ~411 DIP; landscape is ~914.
        return availableWidth >= 600;
    }

    protected override Control? ContainerFromIndex(int index) =>
        _realized.TryGetValue(index, out var control) ? control : null;

    protected override int IndexFromContainer(Control container)
    {
        foreach (var pair in _realized)
        {
            if (ReferenceEquals(pair.Value, container))
                return pair.Key;
        }

        return -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers() => _realized.Values;

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var count = Items.Count;
        if (count == 0)
            return null;

        var fromIndex = from is Control control ? IndexFromContainer(control) : 0;
        if (fromIndex < 0)
            fromIndex = 0;

        var next = direction switch
        {
            NavigationDirection.Left => fromIndex - 1,
            NavigationDirection.Right => fromIndex + 1,
            NavigationDirection.Up => fromIndex - _columns,
            NavigationDirection.Down => fromIndex + _columns,
            NavigationDirection.Previous => fromIndex - 1,
            NavigationDirection.Next => fromIndex + 1,
            NavigationDirection.First => 0,
            NavigationDirection.Last => count - 1,
            _ => fromIndex
        };

        if (wrap)
            next = (next % count + count) % count;
        else
            next = Math.Clamp(next, 0, count - 1);

        return GetOrCreate(next);
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            RecycleOutside(0, -1);
        else
        {
            // A single catalog action must not clear/rebind every visible card.
            var indices = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < items.Count; i++)
                if (items[i] is { } item) indices.TryAdd(item, i);
            var retained = new List<(int OldIndex, int Index, Control Control)>();
            foreach (var (oldIndex, control) in _realized.ToArray())
            {
                if (_containerItems.TryGetValue(control, out var item) && item != null && indices.TryGetValue(item, out var index))
                    retained.Add((oldIndex, index, control));
                else
                    Recycle(oldIndex);
            }
            _realized.Clear();
            foreach (var (oldIndex, index, control) in retained)
            {
                _realized[index] = control;
                if (oldIndex != index) ItemContainerGenerator?.ItemContainerIndexChanged(control, oldIndex, index);
            }
        }
        InvalidateMeasure();
    }

    private Control GetOrCreate(int index)
    {
        if (_realized.TryGetValue(index, out var existing))
        {
            existing.IsVisible = true;
            return existing;
        }

        var generator = ItemContainerGenerator ??
                        throw new InvalidOperationException("Catalog grid panel is not attached.");
        var item = Items[index];
        var needsContainer = generator.NeedsContainer(item, index, out var recycleKey);
        Control container;

        if (!needsContainer)
        {
            container = (Control)item!;
            generator.PrepareItemContainer(container, item, index);
            AddInternalChild(container);
            container.SetValue(RecycleKeyProperty, this);
            generator.ItemContainerPrepared(container, item, index);
        }
        else if (recycleKey != null &&
                 _recyclePool.TryGetValue(recycleKey, out var pool) &&
                 pool.Count > 0)
        {
            container = pool.Pop();
            container.IsVisible = true;
            generator.PrepareItemContainer(container, item, index);
            generator.ItemContainerPrepared(container, item, index);
        }
        else
        {
            container = generator.CreateContainer(item, index, recycleKey);
            container.SetValue(RecycleKeyProperty, recycleKey);
            generator.PrepareItemContainer(container, item, index);
            AddInternalChild(container);
            generator.ItemContainerPrepared(container, item, index);
        }

        _realized[index] = container;
        _containerItems[container] = item;
        if (PlatformCapabilities.IsMobile)
        {
            container.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (container is ContentControl content)
                content.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        return container;
    }

    private void RecycleOutside(int first, int last)
    {
        if (_realized.Count == 0)
            return;

        var generator = ItemContainerGenerator;
        List<int>? stale = null;
        foreach (var index in _realized.Keys)
        {
            if (index >= first && index <= last)
                continue;
            stale ??= [];
            stale.Add(index);
        }

        if (stale == null || generator == null)
            return;

        foreach (var index in stale)
            Recycle(index);
    }

    private void Recycle(int index)
    {
        var generator = ItemContainerGenerator;
        if (generator == null) return;
        if (!_realized.Remove(index, out var element))
            return;

        _containerItems.Remove(element);
        var recycleKey = element.GetValue(RecycleKeyProperty);
        generator.ClearItemContainer(element);
        if (recycleKey != null && !ReferenceEquals(recycleKey, this))
        {
            if (!_recyclePool.TryGetValue(recycleKey, out var pool))
                _recyclePool[recycleKey] = pool = new Stack<Control>();
            pool.Push(element);
        }

        element.IsVisible = false;
    }
}
