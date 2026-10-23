using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;
using WMCL.Modding.Forgelike;

namespace WMCL.App.Views;

public partial class CleanroomView : UserControl
{
    public CleanroomView()
    {
        InitializeComponent();
    }

    private void OnWebsiteClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            _ = top.Launcher.LaunchUriAsync(new Uri(CleanroomViewModel.Website));
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CleanroomViewModel viewModel ||
            sender is not Button { DataContext: CleanroomVersionInfo version } ||
            !viewModel.CanInstall)
            return;
        await viewModel.InstallAsync(version);
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CleanroomViewModel viewModel ||
            sender is not Button { DataContext: CleanroomVersionInfo version } ||
            viewModel.IsDownloading)
            return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "保存 Cleanroom 安装器",
            SuggestedFileName = version.InstallerFileName,
            DefaultExtension = "jar",
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("JAR")
                {
                    Patterns = ["*.jar"]
                }
            ]
        });
        if (file is not null)
            await viewModel.DownloadInstallerAsync(version, file.Path.LocalPath);
    }
}
