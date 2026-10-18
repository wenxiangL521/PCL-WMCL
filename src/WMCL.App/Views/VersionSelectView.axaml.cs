using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class VersionSelectView : UserControl
{
    public VersionSelectView() => InitializeComponent();

    private async void OnCreateGameFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VersionSelectViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择创建 .minecraft 的父目录",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        var parent = folders[0].Path.LocalPath;
        var path = Path.Combine(parent, ".minecraft");
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "versions"));
            viewModel.AddFolder(path);
        }
        catch
        {
            // 权限不足时不切换目录
        }
    }

    private async void OnAddGameFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VersionSelectViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择 Minecraft 文件夹",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            viewModel.AddFolder(folders[0].Path.LocalPath);
    }

    private void OnVersionListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // PCL2 PageSelect：单击版本即选中并返回启动页；异步首次选中不会触发。
        if (DataContext is not VersionSelectViewModel viewModel) return;
        if (sender is not ListBox { SelectedItem: VersionItem version }) return;
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>() is null) return;
        viewModel.SelectedVersion = version;
        viewModel.ConfirmCommand.Execute(null);
    }
}
