using Avalonia.Controls;

namespace WMCL.App.Views;

public partial class InstanceView : UserControl
{
    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = SectionList?.SelectedIndex ?? -1;
        if (index < 0) return;
        var target = index switch
        {
            0 => OverviewCard,
            1 => ServerCard,
            2 => ExportCard,
            3 => SettingsCard,
            _ => null
        };
        target?.BringIntoView();
    }

    private async void OnChooseExportPathClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.InstanceViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "选择导出路径",
            SuggestedFileName = "instance-export.zip",
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("zip") { Patterns = ["*.zip"] },
                new Avalonia.Platform.Storage.FilePickerFileType("mrpack") { Patterns = ["*.mrpack"] }
            ]
        });
        if (file is not null)
            viewModel.ExportPath = file.Path.LocalPath;
    }

    private async void OnImportServersClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.InstanceViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "导入服务器列表",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("servers.dat")
                {
                    Patterns = ["*.dat"]
                }
            ]
        });
        if (files.Count > 0) viewModel.ImportServers(files[0].Path.LocalPath);
    }

    private async void OnExportServersClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.InstanceViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "导出服务器列表",
            SuggestedFileName = "servers.dat",
            DefaultExtension = "dat",
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("servers.dat")
                {
                    Patterns = ["*.dat"]
                }
            ]
        });
        if (file is not null) viewModel.ExportServers(file.Path.LocalPath);
    }

    private async void OnChooseInstanceLogoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.InstanceViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择实例图标",
            AllowMultiple = false,
            FileTypeFilter = [Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll]
        });
        if (files.Count > 0)
            viewModel.InstanceLogoPath = files[0].Path.LocalPath;
    }

    public InstanceView()
    {
        InitializeComponent();
    }
}
