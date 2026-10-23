using Avalonia.Controls;

namespace WMCL.App.Views;

public partial class LinkSettingsView : UserControl
{
    private async void OnChooseTerracottaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LinkSettingsViewModel settings || !settings.Connection.IsIdle) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择 Terracotta 可执行文件",
            AllowMultiple = false
        });
        if (files.Count > 0 && settings.Connection.IsIdle)
            settings.Connection.TerracottaExecutablePath = files[0].Path.LocalPath;
    }

    public LinkSettingsView() => InitializeComponent();
}
