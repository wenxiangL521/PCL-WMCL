using Avalonia.Controls;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class MicrosoftLoginWindow : Window
{
    private MicrosoftLoginViewModel? _viewModel;
    private bool _closing;

    public MicrosoftLoginWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveViewModel();
        LoginView.Cancelled += async (_, _) => await CloseAsync();
        Opened += async (_, _) =>
        {
            ObserveViewModel();
            await ModalMotion.AnimateInAsync(this);
            if (_viewModel?.Account is not null) await CloseAsync();
        };
        Closing += (_, _) =>
        {
            if (_viewModel is { Account: null }) _viewModel.CancelCommand.Execute(null);
        };
        Closed += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.LoginSucceeded -= OnLoginSucceeded;
            _viewModel = null;
        };
    }

    private void ObserveViewModel()
    {
        if (ReferenceEquals(_viewModel, DataContext)) return;
        if (_viewModel is not null) _viewModel.LoginSucceeded -= OnLoginSucceeded;
        _viewModel = DataContext as MicrosoftLoginViewModel;
        if (_viewModel is not null) _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private async void OnLoginSucceeded(object? sender, EventArgs args) => await CloseAsync();

    private async Task CloseAsync()
    {
        if (_closing) return;
        _closing = true;
        await ModalMotion.AnimateOutAsync(this);
        Close();
    }
}
