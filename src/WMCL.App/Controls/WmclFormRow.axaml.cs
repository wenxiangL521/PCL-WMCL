using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

namespace WMCL.App.Controls;

/// <summary>A shared form row which stacks its label only when measured in a genuinely constrained column.</summary>
public class WmclFormRow : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<WmclFormRow, string?>(nameof(Label));

    public static readonly StyledProperty<double> LabelWidthProperty =
        AvaloniaProperty.Register<WmclFormRow, double>(nameof(LabelWidth), 110d);

    public static readonly StyledProperty<double> MinimumInputWidthProperty =
        AvaloniaProperty.Register<WmclFormRow, double>(nameof(MinimumInputWidth), 260d);

    public static readonly DirectProperty<WmclFormRow, bool> IsStackedProperty =
        AvaloniaProperty.RegisterDirect<WmclFormRow, bool>(nameof(IsStacked), row => row.IsStacked);

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double LabelWidth { get => GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }
    public double MinimumInputWidth { get => GetValue(MinimumInputWidthProperty); set => SetValue(MinimumInputWidthProperty, value); }
    public bool IsStacked => _isStacked;

    private bool _isStacked;
    private Grid? _layout;
    private ContentPresenter? _content;
    private double _availableWidth = double.PositiveInfinity;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _layout = e.NameScope.Find<Grid>("PART_FormLayout");
        _content = e.NameScope.Find<ContentPresenter>("PART_FormContent");
        ApplyLayout();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // A horizontal StackPanel is allowed to measure with infinity. Do not use
        // stale Bounds.Width as a substitute: doing so creates layout feedback loops.
        _availableWidth = availableSize.Width;
        ApplyLayout();
        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LabelWidthProperty && change.Property != MinimumInputWidthProperty) return;
        ApplyLayout();
        InvalidateMeasure();
    }

    private void ApplyLayout()
    {
        var labelWidth = double.IsFinite(LabelWidth) ? Math.Max(0, LabelWidth) : 110;
        var inputWidth = double.IsFinite(MinimumInputWidth) ? Math.Max(0, MinimumInputWidth) : 260;
        var stacked = double.IsFinite(_availableWidth) && _availableWidth < labelWidth + 12 + inputWidth;
        SetAndRaise(IsStackedProperty, ref _isStacked, stacked);
        if (_layout is null || _content is null || _layout.ColumnDefinitions.Count < 2) return;
        _layout.ColumnSpacing = stacked ? 0 : 12;
        _layout.RowSpacing = stacked ? 6 : 0;
        _layout.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(labelWidth);
        _layout.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(_content, stacked ? 0 : 1);
        Grid.SetRow(_content, stacked ? 1 : 0);
    }
}
