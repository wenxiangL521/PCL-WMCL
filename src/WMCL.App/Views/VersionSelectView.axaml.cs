using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class VersionSelectView : UserControl
{
    public VersionSelectView() => InitializeComponent();

    private async void OnFolderMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: GameFolderItem folder } ||
            DataContext is not VersionSelectViewModel viewModel) return;
        var name = await Services.DialogService.PromptAsync(
            "重命名文件夹", "输入左侧列表显示的文件夹名称：", folder.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        viewModel.RenameFolder(folder, name);
    }

    private void OnFolderMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: GameFolderItem folder } &&
            DataContext is VersionSelectViewModel viewModel)
            viewModel.OpenGameFolder(folder);
    }

    private void OnFolderMenuRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: GameFolderItem folder } &&
            DataContext is VersionSelectViewModel viewModel)
            viewModel.RemoveFolder(folder);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11 || DataContext is not VersionSelectViewModel viewModel) return;
        viewModel.ShowHidden = !viewModel.ShowHidden;
        e.Handled = true;
    }

    private void OnHiddenClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: VersionItem item }) return;
        if (DataContext is VersionSelectViewModel viewModel)
            viewModel.ToggleHidden(item);
    }

    private void OnFavoriteClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: VersionItem item }) return;
        if (DataContext is VersionSelectViewModel viewModel)
            viewModel.ToggleFavorite(item);
    }

    private async void OnImportLocalModpackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VersionSelectViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择本地整合包",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("整合包")
                {
                    Patterns = ["*.zip", "*.mrpack"]
                }
            ]
        });
        if (files.Count > 0)
            await viewModel.ImportLocalAsync(files[0].Path.LocalPath);
    }

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
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>() is not null) return;
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>() is null) return;
        viewModel.SelectedVersion = version;
        viewModel.ConfirmCommand.Execute(null);
    }
}
