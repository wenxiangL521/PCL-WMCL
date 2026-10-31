using Avalonia;
using Avalonia.Controls;

namespace WMCL.App.Controls;

public class WmclFormRow : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<WmclFormRow, string?>(nameof(Label));

    public static readonly StyledProperty<double> LabelWidthProperty =
        AvaloniaProperty.Register<WmclFormRow, double>(nameof(LabelWidth), 110d);

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double LabelWidth { get => GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }
}
