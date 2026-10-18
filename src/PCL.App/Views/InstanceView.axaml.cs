using Avalonia.Controls;

namespace PCL.App.Views;

public partial class InstanceView : UserControl
{
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
