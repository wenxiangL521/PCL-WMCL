using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace WMCL.App.Views;

public partial class MicrosoftLoginView : UserControl
{
    public MicrosoftLoginView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachAndStart();
    }

    /// <summary>用户点击“返回”时触发，由宿主决定是否关闭窗口或切回登录前页面。</summary>
    public event EventHandler? Cancelled;

    /// <summary>用户点击“去设置”时触发。</summary>
    public event EventHandler? SettingsRequested;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachAndStart();
    }

    private void AttachAndStart()
    {
        if (TopLevel.GetTopLevel(this) is null || DataContext is not MicrosoftLoginViewModel viewModel) return;
        viewModel.AttachClipboardAction(async (text, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("剪贴板不可用");
            await clipboard.SetValueAsync(DataFormat.Text, text);
            ct.ThrowIfCancellationRequested();
        });
        _ = viewModel.EnsureStartedAsync();
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
