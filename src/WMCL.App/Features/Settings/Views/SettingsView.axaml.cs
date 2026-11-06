using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WMCL.App.ViewModels;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace WMCL.App.Features.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnCopyNiriRuleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel model) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("剪贴板不可用");
            await clipboard.SetValueAsync(DataFormat.Text, model.NiriWindowRule);
            model.WindowEffectsStatus = "已复制仅匹配 WMCL 的 Niri 规则；原配置未修改。";
        }
        catch (Exception exception) { model.WindowEffectsStatus = "复制失败：" + exception.Message; }
    }

    private async void OnChooseBackgroundFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择背景文件夹",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            viewModel.BackgroundImageFolder = folders[0].Path.LocalPath;
    }

    private async void OnChooseHomePageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择自定义主页 XAML",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Avalonia XAML") { Patterns = ["*.axaml", "*.xaml"] }
            ]
        });
        if (files.Count > 0)
            viewModel.CustomHomePagePath = files[0].Path.LocalPath;
    }

    private async void OnChooseMusicFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择背景音乐文件夹",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            viewModel.MusicFolder = folders[0].Path.LocalPath;
    }

    private async void OnChooseLogoClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Logo",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count > 0)
            viewModel.CustomLogoPath = files[0].Path.LocalPath;
    }

    private async void OnChooseBackgroundClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择背景图",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count > 0)
            viewModel.BackgroundImagePath = files[0].Path.LocalPath;
    }
}
