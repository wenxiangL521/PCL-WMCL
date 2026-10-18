using Avalonia.Controls;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class MicrosoftLoginWindow : Window
{
    public MicrosoftLoginWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            if (DataContext is not MicrosoftLoginViewModel viewModel) return;
            viewModel.LoginSucceeded += (_, _) => Close();
            await viewModel.StartCommand.ExecuteAsync(null);
        };
    }
}
