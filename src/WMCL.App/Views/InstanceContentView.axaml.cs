using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;
using WMCL.Modding.Local;

namespace WMCL.App.Views;

public partial class InstanceContentView : UserControl
{
    private InstanceContentViewModel? _heightObserved;

    private void ObserveHeightSections()
    {
        if (_heightObserved is not null) _heightObserved.PropertyChanged -= OnHeightSectionChanged;
        _heightObserved = DataContext as InstanceContentViewModel;
        if (_heightObserved is not null) _heightObserved.PropertyChanged += OnHeightSectionChanged;
        SyncManagementListHeight();
    }

    private void OnHeightSectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceContentViewModel.ShowResourcePacks)
            or nameof(InstanceContentViewModel.ShowShaders)
            or nameof(InstanceContentViewModel.ShowWorlds)
            or nameof(InstanceContentViewModel.IsDownloadPage))
            SyncManagementListHeight();
    }

    /// <summary>让本地资源列表撑满页面剩余高度，列表内部滚动。</summary>
    private void SyncManagementListHeight()
    {
        if (DataContext is not InstanceContentViewModel vm || vm.IsDownloadPage) return;
        var height = Math.Max(160, Bounds.Height - 230);
        if (vm.ShowResourcePacks) ResourcePackList.Height = height;
        else if (vm.ShowShaders) ShaderPackList.Height = height;
        else if (vm.ShowWorlds) WorldList.Height = height;
    }

    public InstanceContentView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => SyncManagementListHeight();
        DataContextChanged += (_, _) => ObserveHeightSections();
        ObserveHeightSections();
    }

    private async void OnImportContentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        string? path;
        if (viewModel.ShowWorlds)
        {
            var folders = await storage.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "导入存档目录",
                    AllowMultiple = false
                });
            path = folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }
        else
        {
            var files = await storage.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "导入本地资源",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new Avalonia.Platform.Storage.FilePickerFileType("Zip 压缩包")
                        {
                            Patterns = ["*.zip"]
                        }
                    ]
                });
            path = files.Count > 0 ? files[0].Path.LocalPath : null;
        }
        if (string.IsNullOrWhiteSpace(path)) return;
        viewModel.ImportPath = path;
        viewModel.ImportContentCommand.Execute(null);
    }

    private static IReadOnlyList<InstanceContentItem> Selected(ListBox list) =>
        list.SelectedItems?.OfType<InstanceContentItem>().ToArray() ?? [];

    private void OnToggleResourcePacksClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        var selected = Selected(ResourcePackList);
        if (selected.Count == 0) return;
        viewModel.SetResourcePacksEnabled(selected, !selected.All(item => item.IsEnabled));
    }

    private async void OnDeleteResourcePacksClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        await viewModel.DeleteInstanceContentAsync(InstanceContentKind.ResourcePack, Selected(ResourcePackList));
    }

    private async void OnDeleteShaderPacksClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        await viewModel.DeleteInstanceContentAsync(InstanceContentKind.ShaderPack, Selected(ShaderPackList));
    }

    private async void OnDeleteWorldsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        await viewModel.DeleteInstanceContentAsync(InstanceContentKind.World, Selected(WorldList));
    }

}
