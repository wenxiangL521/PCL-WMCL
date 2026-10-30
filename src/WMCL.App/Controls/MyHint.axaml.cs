using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

namespace WMCL.App.Controls;

public enum HintLevel
{
    Info,
    Warning,
    Error
}

/// <summary>三级提示条（Info/Warning/Error）：左侧强调条 + 内容 + 可选操作槽。</summary>
public partial class MyHint : UserControl
{
    public static readonly StyledProperty<HintLevel> LevelProperty =
        AvaloniaProperty.Register<MyHint, HintLevel>(nameof(Level), HintLevel.Info);

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<MyHint, object?>(nameof(Action));

    private Border? _border;
    private ContentPresenter? _actionSlot;

    public MyHint()
    {
        InitializeComponent();
    }

    static MyHint()
    {
        LevelProperty.Changed.AddClassHandler<MyHint>((control, _) => control.UpdateLevel());
        ActionProperty.Changed.AddClassHandler<MyHint>((control, _) => control.UpdateAction());
    }

    public HintLevel Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _border = e.NameScope.Find<Border>("PART_Border");
        _actionSlot = e.NameScope.Find<ContentPresenter>("PART_Action");
        UpdateLevel();
        UpdateAction();
    }

    private void UpdateLevel()
    {
        if (_border is null) return;
        _border.Classes.Set("warning", Level == HintLevel.Warning);
        _border.Classes.Set("error", Level == HintLevel.Error);
    }

    private void UpdateAction()
    {
        if (_actionSlot is null) return;
        _actionSlot.Content = Action;
        _actionSlot.IsVisible = Action is not null;
    }
}
