using Avalonia.Controls;

namespace WMCL.App.Views;

public partial class AuthlibLoginWindow : Window
{
    public AuthlibLoginWindow()
    {
        InitializeComponent();
        Closing += (_, _) =>
        {
            if (DataContext is ViewModels.AuthlibLoginViewModel viewModel) viewModel.CancelCommand.Execute(null);
        };
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AuthlibLoginViewModel viewModel) viewModel.CancelCommand.Execute(null);
        Close();
    }
}
