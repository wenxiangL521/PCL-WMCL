using Avalonia;
using Avalonia.Controls;
using WMCL.App.Animation;

namespace WMCL.App.Controls;

public enum WmclStatusState
{
    Idle,
    Busy,
    Ok,
    Failed
}

/// <summary>状态点：8px 圆点 + 文案，四态（空闲/进行中/成功/失败）。</summary>
public partial class WmclStatusDot : UserControl
{
    public static readonly StyledProperty<WmclStatusState> StateProperty =
        AvaloniaProperty.Register<WmclStatusDot, WmclStatusState>(nameof(State), WmclStatusState.Idle);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<WmclStatusDot, string?>(nameof(Text));

    public WmclStatusDot()
    {
        InitializeComponent();
        UpdateParts();
    }

    static WmclStatusDot()
    {
        StateProperty.Changed.AddClassHandler<WmclStatusDot>((control, _) => control.UpdateParts());
        TextProperty.Changed.AddClassHandler<WmclStatusDot>((control, _) => control.UpdateParts());
    }

    public WmclStatusState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void UpdateParts()
    {
        if (PART_Dot is null) return;
        var resourceKey = State switch
        {
            WmclStatusState.Busy => "WmclBlue3",
            WmclStatusState.Ok => "WmclSuccess",
            WmclStatusState.Failed => "WmclDanger",
            _ => "WmclTextFaint"
        };
        PART_Dot.Background = this.FindResource(resourceKey) as Avalonia.Media.IBrush;
        PART_Text.Text = Text ?? "";
        PART_Text.IsVisible = !string.IsNullOrEmpty(Text);
        Classes.Set("busy", State == WmclStatusState.Busy && !Motion.ReducedMotion);
    }
}
