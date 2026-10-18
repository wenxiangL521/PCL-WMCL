using Avalonia.Controls;
using Avalonia.Interactivity;
using PCL.App.ViewModels;

namespace PCL.App.Views;

public partial class LaunchView : UserControl
{
    public LaunchView()
    {
        InitializeComponent();

        LogBox.TextChanged += (_, _) =>
        {
            if (LogBox.Text is { Length: > 0 })
                LogBox.CaretIndex = LogBox.Text.Length;
        };
    }

    private async void OnMicrosoftLoginClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel) return;

        var loginViewModel = new MicrosoftLoginViewModel(viewModel.Services);
        var window = new MicrosoftLoginWindow { DataContext = loginViewModel };
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        await window.ShowDialog(owner);
        if (loginViewModel.Account is not null)
            viewModel.SetMicrosoftAccount(loginViewModel.Account);
    }
}
