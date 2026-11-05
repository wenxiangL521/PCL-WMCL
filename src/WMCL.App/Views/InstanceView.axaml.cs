using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class InstanceView : UserControl
{
    private ModsView? _modsPage;
    private InstanceContentView? _contentPage;

    private CancellationTokenSource? _sectionMotion;

    private InstanceViewModel? _viewModel;

    private void ObserveViewModel()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as InstanceViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowSection(_viewModel?.SelectedSectionIndex ?? 0);
        RefreshScreenshots();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(InstanceViewModel.EmbeddedPage))
        {
            // 复用页面视图：几百个 Mod 的列表每次切换都重建控件树会明显卡顿
            var host = EmbeddedPageHost;
            var page = _viewModel?.EmbeddedPage;
            if (page is ModsViewModel mods)
            {
                _modsPage ??= new ModsView { DataContext = mods };
                host.Content = _modsPage;
            }
            else if (page is InstanceContentViewModel content)
            {
                _contentPage ??= new InstanceContentView { DataContext = content };
                host.Content = _contentPage;
            }
        }
        if (change.PropertyName == nameof(InstanceViewModel.SelectedSectionIndex))
        {
            ShowSection(_viewModel?.SelectedSectionIndex ?? 0);
            RefreshScreenshots();
        }
        else if (change.PropertyName == nameof(InstanceViewModel.SelectedInstanceGameDirectory))
            RefreshScreenshots();
        else if (change.PropertyName == nameof(InstanceViewModel.IsSectionPreparing))
        {
            RefreshEmbeddedVisibility();
            if (EmbeddedPageHost.IsVisible) PlaySectionMotion(EmbeddedPageHost);
        }
    }

    private void RefreshEmbeddedVisibility() => EmbeddedPageHost.IsVisible =
        _viewModel is { SelectedSectionIndex: 2 or 3 or 6 or 7 or 8, IsSectionPreparing: false };

    private void RefreshScreenshots()
    {
        var directory = _viewModel?.SelectedInstanceGameDirectory;
        ScreenshotCard.IsVisible = _viewModel?.SelectedSectionIndex == 9 && !string.IsNullOrWhiteSpace(directory);
        if (ScreenshotCard.IsVisible) ScreenshotCard.Load(directory!);
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
            2 or 3 or 6 or 7 or 8 => EmbeddedPageHost,
            _ => null
        };
        var cards = new Control[] { OverviewCard, ServerCard, ExportCard, SettingsCard, ScreenshotCard, EmbeddedPageHost };
        foreach (var card in cards)
        {
            PclPageAnimation.Reset([card]);
            card.IsVisible = ReferenceEquals(card, target);
        }
        PageScroll.IsVisible = index is 0 or 1 or 4 or 5;
        RefreshEmbeddedVisibility();
        PageScroll.ScrollToHome();
        // 异步加载的分区在准备阶段不可见，等加载完成后再播动画，否则动画会被遮住。
        if (target is not null && target.IsVisible) PlaySectionMotion(target);
    }

    private void PlaySectionMotion(Control target)
    {
        _sectionMotion?.Cancel();
        _sectionMotion?.Dispose();
        _sectionMotion = new CancellationTokenSource();
        _ = AnimateSectionAsync(target, _sectionMotion.Token);
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

    private async void OnChooseExportPathClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.InstanceViewModel viewModel) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "选择导出路径",
            SuggestedFileName = (viewModel.SelectedInstance?.Id ?? "instance") + "-export" + viewModel.ExportExtension,
            DefaultExtension = viewModel.ExportExtension.TrimStart('.'),
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType(viewModel.ExportExtension.TrimStart('.'))
                { Patterns = ["*" + viewModel.ExportExtension] }
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
        DataContextChanged += (_, _) => ObserveViewModel();
        AttachedToVisualTree += (_, _) => ObserveViewModel();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
            _sectionMotion?.Cancel();
            _sectionMotion?.Dispose();
            _sectionMotion = null;
            PclPageAnimation.Reset([OverviewCard, ServerCard, ExportCard, SettingsCard, ScreenshotCard, EmbeddedPageHost]);
        };
    }
}
