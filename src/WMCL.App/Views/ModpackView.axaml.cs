using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class ModpackView : UserControl
{
    private async void OnImportPackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.ModpackViewModel viewModel || viewModel.IsBusy) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "安装已有整合包",
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("整合包") { Patterns = ["*.zip", "*.mrpack"] }]
        });
        if (files.Count == 0 || viewModel.IsBusy) return;
        viewModel.LocalPath = files[0].Path.LocalPath;
        await viewModel.InstallLocalCommand.ExecuteAsync(null);
    }

    public ModpackView()
    {
        InitializeComponent();
    }
    private bool _returningToResults;

    private async void OnReturnToResultsClick(object? sender, RoutedEventArgs e)
    {
        if (_returningToResults || DataContext is not ModpackViewModel viewModel) return;
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
