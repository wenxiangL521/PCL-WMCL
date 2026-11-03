using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace WMCL.App.Controls;

/// <summary>Pointer/keyboard HSV field, with no dependency on an operating-system colour dialog.</summary>
public sealed class SkinSaturationField : Control
{
    public static readonly StyledProperty<double> HueProperty = AvaloniaProperty.Register<SkinSaturationField, double>(nameof(Hue));
    public static readonly StyledProperty<double> SaturationProperty = AvaloniaProperty.Register<SkinSaturationField, double>(nameof(Saturation));
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SkinSaturationField, double>(nameof(Value), 1);
    public double Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }
    public double Saturation { get => GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public event EventHandler? ColorPositionChanged;
    private bool _dragging;
    private IPointer? _pointer;
    static SkinSaturationField() => AffectsRender<SkinSaturationField>(HueProperty, SaturationProperty, ValueProperty);
    public SkinSaturationField() { Focusable = true; ClipToBounds = true; }
    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var hue = SkinColorPalette.FromHsv(Hue, 1, 1, 255);
        var saturation = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(hue, 1)]
        };
        var value = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1)]
        };
        context.FillRectangle(saturation, new Rect(Bounds.Size));
        context.FillRectangle(value, new Rect(Bounds.Size));
        var point = new Point(Saturation * Bounds.Width, (1 - Value) * Bounds.Height);
        context.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 5, 5);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.5), point, 5, 5);
    }
    private void Select(Point point)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Saturation = Math.Clamp(point.X / Bounds.Width, 0, 1);
        Value = Math.Clamp(1 - point.Y / Bounds.Height, 0, 1);
        ColorPositionChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        _pointer = e.Pointer;
        Select(e.GetPosition(this));
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_dragging) Select(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        _pointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _dragging = false; _pointer = null; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _dragging = false;
        _pointer?.Capture(null);
        _pointer = null;
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Left) Saturation = Math.Max(0, Saturation - 0.01);
        else if (e.Key == Key.Right) Saturation = Math.Min(1, Saturation + 0.01);
        else if (e.Key == Key.Up) Value = Math.Min(1, Value + 0.01);
        else if (e.Key == Key.Down) Value = Math.Max(0, Value - 0.01);
        else return;
        ColorPositionChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}

/// <summary>Visual colour palette: HSV field, hue/alpha sliders, common colours and a session-local recent list.</summary>
public sealed class SkinColorPalette : UserControl
{
    public static readonly StyledProperty<Color> SelectedColorProperty = AvaloniaProperty.Register<SkinColorPalette, Color>(
        nameof(SelectedColor), Color.FromRgb(85, 85, 85));
    public Color SelectedColor { get => GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    internal SkinSaturationField SaturationField { get; } = new() { Height = 150, MinWidth = 250 };
    internal Slider HueSlider { get; } = new() { Minimum = 0, Maximum = 359, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    internal Slider AlphaSlider { get; } = new() { Minimum = 0, Maximum = 255, Value = 255 };
    internal WrapPanel CommonColors { get; } = new() { ItemSpacing = 5, LineSpacing = 5 };
    internal WrapPanel RecentColors { get; } = new() { ItemSpacing = 5, LineSpacing = 5 };
    private readonly TextBlock _alphaLabel = new();
    private readonly TextBlock _hexLabel = new();
    private readonly Border _swatch = new() { Width = 32, Height = 24, CornerRadius = new CornerRadius(3) };
    private readonly List<Color> _recent = [];
    private bool _updating;

    public SkinColorPalette()
    {
        Width = 280;
        var content = new StackPanel { Spacing = 7, Margin = new Thickness(10) };
        Content = content;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        heading.Children.Add(_swatch);
        Grid.SetColumn(_hexLabel, 1);
        heading.Children.Add(_hexLabel);
        content.Children.Add(heading);
        content.Children.Add(SaturationField);
        content.Children.Add(new TextBlock { Text = "色相" });
        content.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Colors.Red, 0), new GradientStop(Colors.Yellow, 1d / 6),
                    new GradientStop(Colors.Lime, 2d / 6), new GradientStop(Colors.Cyan, .5),
                    new GradientStop(Colors.Blue, 4d / 6), new GradientStop(Colors.Magenta, 5d / 6), new GradientStop(Colors.Red, 1)]
            },
            Child = HueSlider,
            CornerRadius = new CornerRadius(3)
        });
        content.Children.Add(_alphaLabel);
        content.Children.Add(AlphaSlider);
        content.Children.Add(new TextBlock { Text = "常用颜色 · 肤色" });
        content.Children.Add(CommonColors);
        uint[] colors = [0xFF000000, 0xFF333333, 0xFF666666, 0xFF999999, 0xFFCCCCCC, 0xFFFFFFFF,
            0xFFE53935, 0xFFFF9800, 0xFFFFD600, 0xFF43A047, 0xFF00ACC1, 0xFF1E88E5,
            0xFF5E35B1, 0xFFEC407A, 0xFF795548, 0xFFFFE0BD, 0xFFEAC086, 0xFFBD8A60,
            0xFF8D5524, 0xFF5C3317, 0xFF252B38, 0xFFB1C7C5, 0xFFB03060, 0x00000000];
        foreach (var argb in colors) CommonColors.Children.Add(ColorButton(Color.FromUInt32(argb), SelectRecent));
        content.Children.Add(new TextBlock { Text = "最近使用" });
        content.Children.Add(RecentColors);
        SaturationField.ColorPositionChanged += (_, _) => UpdateFromControls();
        HueSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || _updating) return;
            SaturationField.Hue = HueSlider.Value;
            UpdateFromControls();
        };
        AlphaSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !_updating) UpdateFromControls(); };
        SyncColor();
    }
    private void SelectRecent(Color color) { SetCurrentValue(SelectedColorProperty, color); RememberColor(color); }
    public void RememberColor(Color color)
    {
        _recent.Remove(color);
        _recent.Insert(0, color);
        if (_recent.Count > 8) _recent.RemoveAt(8);
        RecentColors.Children.Clear();
        foreach (var recent in _recent) RecentColors.Children.Add(ColorButton(recent, SelectRecent));
    }
    private static Button ColorButton(Color color, Action<Color> select)
    {
        var square = new Border { Background = new SolidColorBrush(color), Width = 22, Height = 22, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
        var button = new Button { Width = 27, Height = 27, MinWidth = 0, MinHeight = 0, Padding = new Thickness(1), Content = square };
        button.Classes.Add("taskAction");
        ToolTip.SetTip(button, color.A == 0 ? "透明" : color.ToString());
        Avalonia.Automation.AutomationProperties.SetName(button, "选择颜色 " + color);
        button.Click += (_, _) => select(color);
        return button;
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedColorProperty && Content is not null) SyncColor();
    }
    private void UpdateFromControls()
    {
        if (_updating) return;
        SetCurrentValue(SelectedColorProperty, FromHsv(HueSlider.Value, SaturationField.Saturation, SaturationField.Value, (byte)Math.Round(AlphaSlider.Value)));
    }
    private void SyncColor()
    {
        _updating = true;
        var hsv = SelectedColor.ToHsv();
        if (hsv.S > 0.0001) HueSlider.Value = hsv.H;
        SaturationField.Hue = HueSlider.Value;
        SaturationField.Saturation = hsv.S;
        SaturationField.Value = hsv.V;
        AlphaSlider.Value = SelectedColor.A;
        _alphaLabel.Text = $"不透明度 {SelectedColor.A * 100d / 255:0}%";
        _hexLabel.Text = SelectedColor.ToString();
        _swatch.Background = new SolidColorBrush(SelectedColor);
        _updating = false;
    }
    internal static Color FromHsv(double hue, double saturation, double value, byte alpha) =>
        new HsvColor(alpha / 255d, hue, Math.Clamp(saturation, 0, 1), Math.Clamp(value, 0, 1)).ToRgb();
}
