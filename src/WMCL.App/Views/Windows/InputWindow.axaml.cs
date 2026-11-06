using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.Animation;

namespace WMCL.App.Views;

public partial class InputWindow : Window
{
    public InputWindow()
    {
        InitializeComponent();
    }

    public InputWindow(string title, string message, string initial) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        InputBox.Text = initial;
        Opened += async (_, _) =>
        {
            await ModalMotion.AnimateInAsync(this);
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private async void OnCancel(object? sender, RoutedEventArgs e)
    {
        await ModalMotion.AnimateOutAsync(this);
        Close(null);
    }

    private async void OnConfirm(object? sender, RoutedEventArgs e)
    {
        await ModalMotion.AnimateOutAsync(this);
        Close(string.IsNullOrWhiteSpace(InputBox.Text) ? null : InputBox.Text.Trim());
    }
}
