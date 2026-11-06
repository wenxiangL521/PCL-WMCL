using Avalonia.Controls;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class ToolboxView : UserControl
{
    public ToolboxView() => InitializeComponent();

    private void OnOpenAchievementGeneratorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            _ = new CustomAchievementWindow().ShowDialog(owner);
    }

    private async void OnChooseDownloadFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ToolboxViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择下载保存目录",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            viewModel.CustomDownloadFolder = folders[0].Path.LocalPath;
    }
}
