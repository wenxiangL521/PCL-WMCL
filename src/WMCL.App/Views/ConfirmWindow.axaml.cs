using Avalonia.Controls;
using WMCL.App.Animation;
using Avalonia.Interactivity;

namespace WMCL.App.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await ModalMotion.AnimateInAsync(this);
    }

    /// <summary>破坏性操作关键词：命中则主按钮使用 danger 语义（spec A4-5a，View 层文案→视觉映射）。</summary>
    private static readonly string[] DestructiveKeywords = ["删除", "卸载", "清空", "移除", "重置"];

    public ConfirmWindow(string title, string message, string confirmText = "确定删除", bool showCancel = true,
        bool isWarning = false) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.IsVisible = showCancel;
        if (isWarning)
            TitleText.Classes.Add("warning");
        if (isWarning || DestructiveKeywords.Any(keyword =>
                confirmText.Contains(keyword, StringComparison.Ordinal)))
            ConfirmButton.Classes.Add("danger");
    }

    private async void OnCancel(object? sender, RoutedEventArgs e)
    {
        await ModalMotion.AnimateOutAsync(this);
        Close(false);
    }

    private async void OnConfirm(object? sender, RoutedEventArgs e)
    {
        await ModalMotion.AnimateOutAsync(this);
        Close(true);
    }
}
