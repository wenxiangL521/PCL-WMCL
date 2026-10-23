using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class LaunchView : UserControl
{
    public LaunchView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => LoadHomeNews();
        DataContextChanged += (_, _) => LoadHomeNews();

        LogBox.TextChanged += (_, _) =>
        {
            if (LogBox.Text is { Length: > 0 })
                LogBox.CaretIndex = LogBox.Text.Length;
        };
    }

    private void LoadHomeNews()
    {
        if (DataContext is ViewModels.LaunchViewModel viewModel)
            _ = viewModel.EnsureHomeNewsAsync();
    }

    private void OnHomeNewsClick(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: Services.MinecraftNewsItem item } ||
            !Services.MinecraftNewsService.IsSafeNewsLink(item.Url))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (DataContext is ViewModels.LaunchViewModel viewModel)
                viewModel.HomeNewsStatus = "打开新闻失败：" + ex.Message;
        }
    }

    private void OnOpenNewsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        _ = new MinecraftNewsWindow().ShowDialog(owner);
    }

    private void OnOpenMsSkinClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LaunchViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner) return;
        _ = new MsSkinWindow(viewModel).ShowDialog(owner);
    }

    private void OnSkinClosetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LaunchViewModel viewModel) return;
        var url = viewModel.AuthlibSkinClosetUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            viewModel.AccountStatus = "当前认证服务器没有可用的皮肤衣柜地址";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            viewModel.AccountStatus = "打开皮肤衣柜失败：" + ex.Message;
        }
    }

    private void OnRedeemCdkeyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.minecraft.net/zh-hans/redeem")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            if (DataContext is ViewModels.LaunchViewModel viewModel)
                viewModel.AccountStatus = "打开 CDKEY 兑换页失败：" + ex.Message;
        }
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

    private async void OnSaveSkinClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LaunchViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.AccountSkinPath) ||
            !File.Exists(viewModel.AccountSkinPath))
            return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "保存皮肤文件",
            SuggestedFileName = viewModel.PlayerName + "-skin.png",
            DefaultExtension = "png",
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("PNG")
                {
                    Patterns = ["*.png"]
                }
            ]
        });
        if (file is null) return;
        File.Copy(viewModel.AccountSkinPath, file.Path.LocalPath, true);
        viewModel.AccountStatus = "皮肤文件已保存：" + file.Path.LocalPath;
    }

    private async void OnOnlineAccountClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        if (viewModel.IsMicrosoftAccount) return;
        if (viewModel.TryUseSavedMicrosoftAccount()) return;

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
        if (!viewModel.IsAuthlibAccount && viewModel.TryUseSavedAuthlibAccount()) return;
        var loginViewModel = new AuthlibLoginViewModel(viewModel.AuthlibService);
        if (!string.IsNullOrWhiteSpace(viewModel.RequiredAuthServer)) loginViewModel.Server = viewModel.RequiredAuthServer;
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
        viewModel.SelectOfflineAccountCommand.Execute(null);
    }

    private void OnOpenVersionSelectClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel main)
            main.OpenVersionSelect();
    }

    private void ResetAccountRadios(LaunchViewModel viewModel)
    {
        OnlineAccountRadio.IsChecked = viewModel.IsMicrosoftAccount;
        OfflineAccountRadio.IsChecked = viewModel.IsOfflineAccount;
        AuthlibAccountRadio.IsChecked = viewModel.IsAuthlibAccount;
    }
}
