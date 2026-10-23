using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class InstanceView : UserControl
{
    private CancellationTokenSource? _sectionMotion;

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = SectionList?.SelectedIndex ?? -1;
        if (index < 0) return;
        if (index is 0 or 1 or 4)
        {
            ShowSection(index);
            return;
        }

        if (TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel main)
            return;
        switch (index)
        {
            case 2:
                main.OpenInstanceInstall();
                break;
            case 3:
                main.OpenInstanceMods();
                break;
        }
        if (SectionList is not null) SectionList.SelectedIndex = 0;
    }

    private MainWindowViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private void OnMoreServerClick(object? sender, RoutedEventArgs e)
    {
        if (SectionList is not null) SectionList.SelectedIndex = 0;
        ShowSection(5);
    }

    private void OnMoreResourceClick(object? sender, RoutedEventArgs e) =>
        Main?.OpenInstanceContent("content-resource", "资源包");

    private void OnMoreSavesClick(object? sender, RoutedEventArgs e) =>
        Main?.OpenInstanceContent("content-world", "存档");

    private void OnMoreShaderClick(object? sender, RoutedEventArgs e) =>
        Main?.OpenInstanceContent("content-shader", "光影");

    private void OnMoreScreenshotsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InstanceViewModel viewModel)
            ScreenshotCard.Load(viewModel.GameDirectory);
        ShowSection(9);
    }

    private void ShowSection(int index)
    {
        Control? target = index switch
        {
            0 => OverviewCard,
            1 => SettingsCard,
            4 => ExportCard,
            5 => ServerCard,
            9 => ScreenshotCard,
            _ => null
        };
        if (target is null) return;
        var cards = new Control[] { OverviewCard, ServerCard, ExportCard, SettingsCard, ScreenshotCard };
        foreach (var card in cards)
        {
            PclPageAnimation.Reset([card]);
            card.IsVisible = ReferenceEquals(card, target);
        }
        PageScroll.ScrollToHome();
        _sectionMotion?.Cancel();
        _sectionMotion?.Dispose();
        _sectionMotion = new CancellationTokenSource();
        var token = _sectionMotion.Token;
        _ = AnimateSectionAsync(target, token);
    }

    private async Task AnimateSectionAsync(Control target, CancellationToken token)
    {
        try
        {
            await PclPageAnimation.RunAsync([target], entering: true, token, scrollViewer: PageScroll);
        }
        catch (OperationCanceledException)
        {
            PclPageAnimation.Reset([target]);
        }
    }

    private void OnResetSectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is InstanceViewModel viewModel)
            viewModel.ResetInstanceSettingsCommand.Execute(null);
    }

    private void OnRefreshSectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is InstanceViewModel viewModel)
            viewModel.RefreshCommand.Execute(null);
    }

    private async void OpenScreenshots()
    {
        if (DataContext is not ViewModels.InstanceViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var window = new ScreenshotWindow(viewModel.GameDirectory);
        await window.ShowDialog(owner);
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
        DetachedFromVisualTree += (_, _) =>
        {
            _sectionMotion?.Cancel();
            _sectionMotion?.Dispose();
            _sectionMotion = null;
            PclPageAnimation.Reset([OverviewCard, ServerCard, ExportCard, SettingsCard]);
        };
    }
}
