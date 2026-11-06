using Avalonia.Controls;
using WMCL.App.Animation;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace WMCL.App.Views;

public partial class ConfirmWindow : Window
{
    private bool _isClosing;

    public ConfirmWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            await ModalMotion.AnimateInAsync(this);
            if (IsVisible) (CancelButton.IsDefault ? CancelButton : ConfirmButton).Focus();
        };
    }

    /// <summary>破坏性操作关键词：命中则主按钮使用 danger 语义（spec A4-5a，View 层文案→视觉映射）。</summary>
    private static readonly string[] DestructiveKeywords = ["删除", "卸载", "清空", "移除", "重置", "清理"];

    public ConfirmWindow(string title, string message, string confirmText = "确定", bool showCancel = true,
        bool isWarning = false) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.IsVisible = showCancel;
        var destructive = showCancel && DestructiveKeywords.Any(keyword =>
            confirmText.Contains(keyword, StringComparison.Ordinal));
        if (isWarning || destructive)
            TitleText.Classes.Add("warning");
        if (destructive)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("danger");
        }
        ConfirmButton.IsDefault = !destructive;
        CancelButton.IsDefault = destructive;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        await CompleteAsync(false);
    }

    private async void OnCancel(object? sender, RoutedEventArgs e) => await CompleteAsync(false);

    private async void OnConfirm(object? sender, RoutedEventArgs e) => await CompleteAsync(true);

    private async Task CompleteAsync(bool confirmed)
    {
        if (_isClosing) return;
        _isClosing = true;
        ConfirmButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        await ModalMotion.AnimateOutAsync(this);
        if (IsVisible) Close(confirmed);
    }
}
