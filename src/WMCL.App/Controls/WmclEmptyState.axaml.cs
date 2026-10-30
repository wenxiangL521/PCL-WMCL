using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WMCL.App.Controls;

/// <summary>空状态：图标（缺省 inbox）+ 标题 + 说明 + 可选操作槽，垂直居中。</summary>
public partial class WmclEmptyState : UserControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<WmclEmptyState, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WmclEmptyState, string?>(nameof(Title), "暂无内容");

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<WmclEmptyState, string?>(nameof(Description));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<WmclEmptyState, object?>(nameof(Action));

    public WmclEmptyState()
    {
        InitializeComponent();
        UpdateParts();
    }

    static WmclEmptyState()
    {
        IconProperty.Changed.AddClassHandler<WmclEmptyState>((control, _) => control.UpdateParts());
        TitleProperty.Changed.AddClassHandler<WmclEmptyState>((control, _) => control.UpdateParts());
        DescriptionProperty.Changed.AddClassHandler<WmclEmptyState>((control, _) => control.UpdateParts());
        ActionProperty.Changed.AddClassHandler<WmclEmptyState>((control, _) => control.UpdateParts());
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    private void UpdateParts()
    {
        if (PART_Icon is null) return;
        PART_Icon.Data = Icon ?? this.FindResource("WmclIconInbox") as Geometry;
        PART_Title.Text = Title ?? "暂无内容";
        PART_Description.Text = Description ?? "";
        PART_Description.IsVisible = !string.IsNullOrEmpty(Description);
        PART_Action.Content = Action;
        PART_Action.IsVisible = Action is not null;
    }
}
