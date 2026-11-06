using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class InstanceResourceBrowser : ResourceBrowserView
{
    public InstanceResourceBrowser()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (_viewModel is null) return;
            _viewModel.PropertyChanging -= OnProjectChanging;
            _viewModel.PropertyChanging += OnProjectChanging;
            BindTargetSelection();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanging -= OnProjectChanging;
            _targetSelection?.Dispose();
            _targetSelection = null;
        };
    }

    private InstanceContentViewModel? _viewModel;
    private ResourceBrowserTargetSelection? _targetSelection;
    private string? _resultsStatus;

    protected override void OnDataContextChanged(EventArgs args)
    {
        base.OnDataContextChanged(args);
        if (_viewModel is not null) _viewModel.PropertyChanging -= OnProjectChanging;
        _targetSelection?.Dispose();
        _targetSelection = null;
        _viewModel = DataContext as InstanceContentViewModel;
        _resultsStatus = null;
        if (_viewModel is not null && this.IsAttachedToVisualTree())
        {
            _viewModel.PropertyChanging += OnProjectChanging;
            BindTargetSelection();
        }
    }

    private void BindTargetSelection()
    {
        _targetSelection?.Dispose();
        if (_viewModel is not { } viewModel) return;
        _targetSelection = new ResourceBrowserTargetSelection(TargetInstanceComboBox, viewModel,
            nameof(InstanceContentViewModel.SelectedInstance), viewModel.Instances, () => viewModel.SelectedInstance);
    }

    private void OnProjectChanging(object? sender, PropertyChangingEventArgs args)
    {
        if (args.PropertyName != nameof(InstanceContentViewModel.SelectedDownloadProject) || _viewModel is not { HasResourceDetail: false }) return;
        _resultsStatus = _viewModel.DownloadStatus;
        RememberResultsPosition();
    }

    private void OnTargetInstanceChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_targetSelection?.IsSynchronizing != true && !IsTargetLocked && DataContext is InstanceContentViewModel { IsDownloadPage: true, IsDownloadBusy: false } viewModel &&
            sender is ComboBox { SelectedItem: string target })
            viewModel.SelectedInstance = target;
    }

    private async void OnReturnToResultsClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is InstanceContentViewModel viewModel)
            await ReturnToResultsAsync(ResourceDetailPanel, () =>
            {
                viewModel.ReturnToResultsCommand.Execute(null);
                if (_resultsStatus is { } status) viewModel.DownloadStatus = status;
            });
    }

    private async void OnSaveResourceFileClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not InstanceContentViewModel viewModel || !viewModel.CanSaveDownload)
            return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var name = viewModel.SaveFileName;
        var extension = Path.GetExtension(name).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension)) extension = "zip";
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "保存资源文件",
            SuggestedFileName = name,
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("文件")
                {
                    Patterns = ["*." + extension]
                }
            ]
        });
        if (file is not null)
            await viewModel.SaveDownloadAsync(file.Path.LocalPath);
    }
}
