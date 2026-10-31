using Avalonia.Controls;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class MicrosoftLoginWindow : Window
{
    public MicrosoftLoginWindow()
    {
        InitializeComponent();

        LoginView.Cancelled += async (_, _) =>
        {
            await ModalMotion.AnimateOutAsync(this);
            Close();
        };
        Opened += async (_, _) =>
        {
            await ModalMotion.AnimateInAsync(this);
            if (DataContext is MicrosoftLoginViewModel viewModel)
                viewModel.LoginSucceeded += async (_, _) =>
                {
                    await ModalMotion.AnimateOutAsync(this);
                    Close();
                };
        };

        Closing += (_, _) =>
        {
            if (DataContext is MicrosoftLoginViewModel { Account: null } viewModel)
                viewModel.CancelCommand.Execute(null);
        };
    }
}
