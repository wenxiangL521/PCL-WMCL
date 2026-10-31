using Avalonia;
using Avalonia.Controls;

namespace WMCL.App.Controls;

public partial class WmclCardHeader : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WmclCardHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<WmclCardHeader, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<WmclCardHeader, object?>(nameof(Action));

    public WmclCardHeader()
    {
        InitializeComponent();
        UpdateParts();
    }

    static WmclCardHeader()
    {
        TitleProperty.Changed.AddClassHandler<WmclCardHeader>((control, _) => control.UpdateParts());
        SubtitleProperty.Changed.AddClassHandler<WmclCardHeader>((control, _) => control.UpdateParts());
        ActionProperty.Changed.AddClassHandler<WmclCardHeader>((control, _) => control.UpdateParts());
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

    private void UpdateParts()
    {
        if (TitleText is null || SubtitleText is null || ActionPresenter is null) return;
        TitleText.Text = Title ?? "";
        SubtitleText.Text = Subtitle ?? "";
        SubtitleText.IsVisible = !string.IsNullOrEmpty(Subtitle);
        ActionPresenter.Content = Action;
    }
}
