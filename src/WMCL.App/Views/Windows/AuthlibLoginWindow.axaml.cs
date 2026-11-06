using Avalonia.Controls;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class AuthlibLoginWindow : Window
{
    public AuthlibLoginWindow()
    {
        InitializeComponent();
        LoginView.Cancelled += async (_, _) =>
        {
            await ModalMotion.AnimateOutAsync(this);
            Close();
        };
        Opened += async (_, _) => await ModalMotion.AnimateInAsync(this);
        Closing += (_, _) =>
        {
            if (DataContext is AuthlibLoginViewModel { Account: null } viewModel)
                viewModel.CancelCommand.Execute(null);
        };
    }
}
