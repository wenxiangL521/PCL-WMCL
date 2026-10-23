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

    public ConfirmWindow(string title, string message, string confirmText = "确定删除", bool showCancel = true) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.IsVisible = showCancel;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
