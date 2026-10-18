using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

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

    private async void OnUploadSkinClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LaunchViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择皮肤 PNG（64×64 或 64×32）",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("PNG")
                {
                    Patterns = ["*.png"]
                }
            ]
        });
        if (files.Count == 0) return;
        viewModel.AccountStatus = await viewModel.UploadMicrosoftSkinAsync(files[0].Path.LocalPath);
    }

    private async void OnOnlineAccountClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        if (viewModel.IsMicrosoftAccount) return;

        var loginViewModel = new MicrosoftLoginViewModel(viewModel.Services);
        var window = new MicrosoftLoginWindow { DataContext = loginViewModel };
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        await window.ShowDialog(owner);
        if (loginViewModel.Account is not null)
            viewModel.SetMicrosoftAccount(loginViewModel.Account);
        else
            ResetAccountRadios(viewModel);
    }

    private async void OnAuthlibAccountClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        var loginViewModel = new AuthlibLoginViewModel(viewModel.AuthlibService);
        var window = new AuthlibLoginWindow { DataContext = loginViewModel };
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        loginViewModel.LoginSucceeded += (_, _) => window.Close();

        await window.ShowDialog(owner);
        if (loginViewModel.Account is not null)
            viewModel.SetAuthlibAccount(loginViewModel.Account);
        else
            ResetAccountRadios(viewModel);
    }

    private void OnOfflineAccountClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        if (viewModel.IsMicrosoftAccount) viewModel.LogoutMicrosoftCommand.Execute(null);
        if (viewModel.IsAuthlibAccount) viewModel.LogoutAuthlibCommand.Execute(null);
    }

    private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        VersionButton.Flyout?.Hide();
    }

    private void OnDownloadVersionsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LaunchViewModel viewModel)
            viewModel.RequestDownloadVersions();
        VersionButton.Flyout?.Hide();
    }

    private void ResetAccountRadios(LaunchViewModel viewModel)
    {
        OnlineAccountRadio.IsChecked = viewModel.IsMicrosoftAccount;
        OfflineAccountRadio.IsChecked = viewModel.IsOfflineAccount;
        AuthlibAccountRadio.IsChecked = viewModel.IsAuthlibAccount;
    }
}
