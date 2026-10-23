using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class InstanceContentView : UserControl
{
    public InstanceContentView()
    {
        InitializeComponent();
    }

    private async void OnSaveResourceFileClick(object? sender, RoutedEventArgs e)
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
    private bool _returningToResults;

    private async void OnReturnToResultsClick(object? sender, RoutedEventArgs e)
    {
        if (_returningToResults || DataContext is not InstanceContentViewModel viewModel) return;
        _returningToResults = true;
        try
        {
            if (this.FindControl<StackPanel>("ResourceDetailPanel") is { } panel)
                await ResourceDetailMotion.ExitAsync(panel);
            viewModel.ReturnToResultsCommand.Execute(null);
        }
        finally { _returningToResults = false; }
    }

}
