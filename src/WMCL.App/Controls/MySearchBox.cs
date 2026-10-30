using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace WMCL.App.Controls;

/// <summary>统一搜索框：放大镜 + 输入（PART_TextBox）+ 清除钮（PART_ClearButton，文本非空时可见）。</summary>
public class MySearchBox : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MySearchBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<MySearchBox, string?>(nameof(Watermark));

    public static readonly DirectProperty<MySearchBox, bool> IsClearButtonVisibleProperty =
        AvaloniaProperty.RegisterDirect<MySearchBox, bool>(
            nameof(IsClearButtonVisible), control => control.IsClearButtonVisible);

    private bool _isClearButtonVisible;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public bool IsClearButtonVisible
    {
        get => _isClearButtonVisible;
        private set => SetAndRaise(IsClearButtonVisibleProperty, ref _isClearButtonVisible, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            IsClearButtonVisible = !string.IsNullOrEmpty(Text);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_ClearButton") is { } clearButton)
            clearButton.Click += (_, _) => Text = "";
    }
}
