using Avalonia;
using Avalonia.Controls;

namespace WMCL.App.Controls;

public sealed class ResponsiveNewsPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<ResponsiveNewsPanel, double>(nameof(MinItemWidth), 260,
            validate: value => double.IsFinite(value) && value > 0);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ResponsiveNewsPanel, double>(nameof(ColumnSpacing), 10,
            validate: value => double.IsFinite(value) && value >= 0);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ResponsiveNewsPanel, double>(nameof(RowSpacing), 10,
            validate: value => double.IsFinite(value) && value >= 0);

    static ResponsiveNewsPanel()
    {
        AffectsMeasure<ResponsiveNewsPanel>(MinItemWidthProperty, ColumnSpacingProperty, RowSpacingProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize.Width, false);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    private Size Layout(double availableWidth, bool arrange)
    {
        var children = Children.Where(child => child.IsVisible).ToArray();
        if (children.Length == 0) return default;
        var width = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : MinItemWidth;
        var columns = Math.Max(1, (int)Math.Min(children.Length,
            Math.Floor((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing))));
        var itemWidth = Math.Max(0, (width - ColumnSpacing * (columns - 1)) / columns);
        var top = 0d;
        for (var rowStart = 0; rowStart < children.Length; rowStart += columns)
        {
            var rowEnd = Math.Min(rowStart + columns, children.Length);
            var rowHeight = 0d;
            for (var index = rowStart; index < rowEnd; index++)
            {
                children[index].Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
            }
            if (arrange)
            {
                for (var index = rowStart; index < rowEnd; index++)
                    children[index].Arrange(new Rect((index - rowStart) * (itemWidth + ColumnSpacing),
                        top, itemWidth, rowHeight));
            }
            top += rowHeight;
            if (rowEnd < children.Length) top += RowSpacing;
        }
        return new Size(width, top);
    }
}
