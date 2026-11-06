using Avalonia;
using Avalonia.Controls;

namespace WMCL.App.Controls;

/// <summary>页面级加载遮罩：覆盖内容区并居中显示 PCL 加载指示器。</summary>
public partial class LoadingOverlay : UserControl
{
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(IsRunning));

    public LoadingOverlay()
    {
        InitializeComponent();
        Sync();
    }

    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRunningProperty) Sync();
    }

    private void Sync()
    {
        if (Mask is null || Indicator is null) return;
        Mask.IsVisible = IsRunning;
        Indicator.IsRunning = IsRunning;
    }
}
