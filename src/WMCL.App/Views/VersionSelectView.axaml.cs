using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class VersionSelectView : UserControl
{
    private (ListBox List, VersionItem Item)? _pressedVersion;

    public VersionSelectView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnVersionListPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnVersionListPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DetachedFromVisualTree += (_, _) => _pressedVersion = null;
    }

    private async void OnFolderMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: GameFolderItem folder } ||
            DataContext is not VersionSelectViewModel viewModel) return;
        var name = await Services.DialogService.PromptAsync(
            "重命名文件夹", "输入左侧列表显示的文件夹名称：", folder.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        viewModel.RenameFolder(folder, name);
    }

    private async void OnFolderMenuRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: GameFolderItem folder } &&
            DataContext is VersionSelectViewModel viewModel)
        {
            if (!string.Equals(folder.Path, viewModel.GameDirectory, StringComparison.OrdinalIgnoreCase))
                viewModel.SelectedFolder = folder;
            await viewModel.RefreshVersionsAsync();
        }
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
        if (e.KeyModifiers != KeyModifiers.None || DataContext is not VersionSelectViewModel viewModel) return;
        if (e.Key == Key.F11)
        {
            viewModel.ShowHidden = !viewModel.ShowHidden;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && FindVersionRow(e.Source) is { } row)
        {
            viewModel.ActivateVersion(row.Item);
            e.Handled = true;
        }
    }

    private async void OnImportLocalModpackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VersionSelectViewModel viewModel) return;
        if (viewModel.IsImporting)
        {
            viewModel.CancelImportCommand.Execute(null);
            return;
        }
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

    private static (ListBox List, VersionItem Item)? FindVersionRow(object? source)
    {
        if (source is not Avalonia.Visual visual || visual is Button || visual.FindAncestorOfType<Button>() is not null) return null;
        var container = visual as ListBoxItem ?? visual.FindAncestorOfType<ListBoxItem>();
        var list = container?.FindAncestorOfType<ListBox>();
        return list?.Classes.Contains("versionList") == true && container?.DataContext is VersionItem item ? (list, item) : null;
    }

    private void OnVersionListPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _pressedVersion = e.KeyModifiers == KeyModifiers.None ? FindVersionRow(e.Source) : null;

    private void OnVersionListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pressedVersion;
        _pressedVersion = null;
        if (DataContext is not VersionSelectViewModel viewModel || e.KeyModifiers != KeyModifiers.None ||
            pressed is not { } start || FindVersionRow(this.InputHitTest(e.GetPosition(this))) is not { } end ||
            !ReferenceEquals(start.List, end.List) || !ReferenceEquals(start.Item, end.Item)) return;
        if (e.InitialPressMouseButton == MouseButton.Left) viewModel.ActivateVersion(end.Item);
        else if (e.InitialPressMouseButton == MouseButton.Right) viewModel.OpenVersionSettings(end.Item);
        else return;
        e.Handled = true;
    }
}
