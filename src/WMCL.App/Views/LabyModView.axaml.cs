using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;
using WMCL.Modding.Forgelike;

namespace WMCL.App.Views;

public partial class LabyModView : UserControl
{
    public LabyModView()
    {
        InitializeComponent();
    }

    private void OnWebsiteClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            _ = top.Launcher.LaunchUriAsync(new Uri(LabyModViewModel.Website));
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LabyModViewModel viewModel ||
            sender is not Button { DataContext: LabyModVersionInfo version } ||
            viewModel.IsDownloading)
            return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "保存 LabyMod 安装器",
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
