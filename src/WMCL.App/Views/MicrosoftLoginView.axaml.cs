using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class MicrosoftLoginView : UserControl
{
    private bool _started;

    public MicrosoftLoginView() => InitializeComponent();

    /// <summary>用户点击“返回”时触发，由宿主决定是否关闭窗口或切回登录前页面。</summary>
    public event EventHandler? Cancelled;

    /// <summary>用户点击“去设置”时触发。</summary>
    public event EventHandler? SettingsRequested;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_started || DataContext is not MicrosoftLoginViewModel viewModel) return;
        _started = true;
        _ = viewModel.StartCommand.ExecuteAsync(null);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MicrosoftLoginViewModel viewModel)
            viewModel.CancelCommand.Execute(null);
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
