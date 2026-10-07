using Avalonia;
using Avalonia.Controls;

namespace QuiverLauncher.Views;

/// <summary>
/// Cards in equal columns that fill the width, as the website lays them out on a phone: as many columns of at least
/// <see cref="MinColumnWidth"/> as fit, and never fewer than <see cref="MinColumns"/>. Each row is as tall as its tallest card.
/// </summary>
public sealed class CardColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<CardColumnsPanel, double>(nameof(MinColumnWidth), 160);
    public static readonly StyledProperty<int> MinColumnsProperty =
        AvaloniaProperty.Register<CardColumnsPanel, int>(nameof(MinColumns), 2);
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CardColumnsPanel, double>(nameof(Spacing), 12);

    static CardColumnsPanel() => AffectsMeasure<CardColumnsPanel>(MinColumnWidthProperty, MinColumnsProperty, SpacingProperty);

    public double MinColumnWidth { get => GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MinColumns { get => GetValue(MinColumnsProperty); set => SetValue(MinColumnsProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public int ColumnsFor(double width) => Math.Max(Math.Max(1, MinColumns), (int)((width + Spacing) / (MinColumnWidth + Spacing)));

    private double ColumnWidth(double width, int columns) => Math.Max(0, (width - Spacing * (columns - 1)) / columns);

    /// <summary>How wide each card is at this width.</summary>
    public double ColumnWidthFor(double width) => ColumnWidth(width, ColumnsFor(width));

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ColumnsFor(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width);
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth * columns + Spacing * (columns - 1) : availableSize.Width;
        var columnWidth = ColumnWidth(width, columns);
        var height = 0.0;
        foreach (var row in Rows(columns))
        {
            var rowHeight = 0.0;
            foreach (var child in row)
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            height += (height > 0 ? Spacing : 0) + rowHeight;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var columnWidth = ColumnWidth(finalSize.Width, columns);
        var y = 0.0;
        foreach (var row in Rows(columns))
        {
            var rowHeight = row.Max(child => child.DesiredSize.Height);
            for (var i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * (columnWidth + Spacing), y, columnWidth, rowHeight));
            y += rowHeight + Spacing;
        }
        return finalSize;
    }

    private IEnumerable<List<Control>> Rows(int columns) =>
        Children.Where(child => child.IsVisible).Chunk(columns).Select(row => row.ToList());
}
