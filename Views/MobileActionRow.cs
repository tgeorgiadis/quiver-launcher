using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using Item = QuiverLauncher.Services.CatalogReviewGridCardActions.ChromeItem;
using Kind = QuiverLauncher.Services.CatalogReviewGridCardActions.ChromeKind;

namespace QuiverLauncher.Views;

/// <summary>A single action strip with measured overflow, local to the realized card.</summary>
public sealed class MobileActionRow : Panel
{
    public static readonly StyledProperty<IReadOnlyList<Item>?> ActionsProperty =
        AvaloniaProperty.Register<MobileActionRow, IReadOnlyList<Item>?>(nameof(Actions));
    public IReadOnlyList<Item>? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public event EventHandler<RoutedEventArgs>? ActionInvoked;
    private const double Gap = 6;
    private readonly Dictionary<Kind, Button> _buttons = new();
    private readonly Dictionary<Button, Size> _sizes = new();
    private readonly Dictionary<Button, Size> _layoutSizes = new();
    private readonly Button _more;
    private readonly MenuFlyout _menu = new() { Placement = PlacementMode.BottomEdgeAlignedLeft };
    private IReadOnlyList<Item> _actions = [];
    private IReadOnlyList<Item> _overflow = [];
    private Kind? _restoreFocus;

    public MobileActionRow()
    {
        _more = MakeButton("More");
        _more.Classes.Add("catalog-review-more");
        _more.Flyout = _menu;
        Children.Add(_more);
        DetachedFromVisualTree += (_, _) => _menu.Hide();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ActionsProperty) return;
        var next = Actions ?? [];
        if (_actions.SequenceEqual(next)) return;
        RememberFocus();
        _menu.Hide();
        _actions = next;
        foreach (var item in next)
        {
            if (!_buttons.TryGetValue(item.Kind, out var button))
            {
                button = MakeButton(item.Label);
                if (item.IsAddStyle) button.Classes.Add("catalog-review-add");
                if (item.IsMergeStyle) button.Classes.Add("catalog-review-merge");
                if (item.IsRemoveStyle) button.Classes.Add("catalog-remove-from-library");
                button.Click += (sender, e) => ActionInvoked?.Invoke(sender, e);
                _buttons.Add(item.Kind, button);
                Children.Insert(Children.Count - 1, button);
            }
            button.DataContext = item;
            button.Tag = item.IdentityKey;
        }
        for (var i = 0; i < next.Count; i++)
            Children.Move(Children.IndexOf(_buttons[next[i].Kind]), i);
        Children.Move(Children.IndexOf(_more), next.Count);
        InvalidateMeasure();
    }

    private Button MakeButton(string label)
    {
        var button = new Button { Content = label, FontSize = 10, FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(8, 4), MinWidth = 44, MinHeight = 32 };
        button.Classes.Add("options");
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == TemplatedControl.FontSizeProperty || e.Property == TemplatedControl.FontFamilyProperty ||
                e.Property == TemplatedControl.FontWeightProperty || e.Property == TemplatedControl.PaddingProperty ||
                e.Property == TemplatedControl.BorderThicknessProperty || e.Property == MinWidthProperty ||
                e.Property == MinHeightProperty || e.Property == ContentControl.ContentProperty)
            {
                _sizes.Remove(button);
                InvalidateMeasure();
            }
        };
        return button;
    }

    private Size NaturalSize(Button button)
    {
        if (!button.IsVisible && _sizes.TryGetValue(button, out var cached)) return cached;
        button.IsVisible = true;
        if (!_sizes.ContainsKey(button))
        {
            // Hidden descendants do not bubble their measure invalidation up the
            // visual tree. Refresh their measurements after typography changes.
            foreach (var child in button.GetVisualDescendants().OfType<Control>()) child.InvalidateMeasure();
            button.InvalidateMeasure();
        }
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _sizes[button] = button.DesiredSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        RememberFocus();
        var sizes = _actions.Select(a => NaturalSize(_buttons[a.Kind])).ToArray();
        var moreSize = NaturalSize(_more);
        for (var i = 0; i < _actions.Count; i++) _layoutSizes[_buttons[_actions[i].Kind]] = sizes[i];
        _layoutSizes[_more] = moreSize;
        var count = ActionOverflowLayout.InlineCount(sizes.Select(s => s.Width).ToArray(), availableSize.Width, moreSize.Width, Gap);
        var overflow = _actions.Skip(count).ToArray();
        if (!_overflow.SequenceEqual(overflow))
        {
            _menu.Hide();
            _menu.Items.Clear();
            foreach (var item in overflow)
            {
                var entry = new MenuItem { Header = item.Label, Tag = item.IdentityKey };
                entry.Click += (_, e) => ActionInvoked?.Invoke(_buttons[item.Kind], e);
                _menu.Items.Add(entry);
            }
            _overflow = overflow;
        }
        var inline = _actions.Take(count).Select(a => a.Kind).ToHashSet();
        var moreWasFocused = _more.IsFocused;
        var styledFocus = Children.Any(c => c.Classes.Contains("gamepad-focused"));
        void Focus(Button target)
        {
            foreach (var control in Children) control.Classes.Set("gamepad-focused", styledFocus && ReferenceEquals(control, target));
            target.Focus(NavigationMethod.Directional);
        }
        // Transfer focus before hiding/removing an action, so input never lands on another card.
        _more.IsVisible = overflow.Length > 0;
        foreach (var kind in inline) _buttons[kind].IsVisible = true;
        if (_restoreFocus is { } focus)
        {
            var target = inline.Contains(focus) ? _buttons[focus] :
                _actions.Any(a => a.Kind == focus) ? _more :
                inline.Contains(Kind.Details) ? _buttons[Kind.Details] : _more;
            if (target.IsVisible) Focus(target);
            _restoreFocus = null;
        }
        else if (moreWasFocused && !_more.IsVisible && count > 0)
            Focus(_buttons[_actions[0].Kind]);
        foreach (var (kind, button) in _buttons) button.IsVisible = inline.Contains(kind);
        var displayed = sizes.Take(count).ToList();
        if (overflow.Length > 0) displayed.Add(moreSize);
        return new Size(Math.Min(availableSize.Width, displayed.Sum(s => s.Width) + Gap * Math.Max(0, displayed.Count - 1)),
            displayed.Count == 0 ? 0 : displayed.Max(s => s.Height));
    }

    private void RememberFocus()
    {
        foreach (var (kind, button) in _buttons)
            if (button.IsFocused) { _restoreFocus = kind; break; }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (var button in _actions.Select(a => _buttons[a.Kind]).Append(_more).Where(b => b.IsVisible))
        {
            var width = _layoutSizes[button].Width;
            button.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Gap;
        }
        return finalSize;
    }
}
