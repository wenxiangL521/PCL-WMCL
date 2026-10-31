using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class AuthlibLoginView : UserControl
{
    public AuthlibLoginView() => InitializeComponent();

    /// <summary>用户点击“返回”时触发，由宿主决定是否关闭窗口或切回登录前页面。</summary>
    public event EventHandler? Cancelled;

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AuthlibLoginViewModel viewModel)
            viewModel.CancelCommand.Execute(null);
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
