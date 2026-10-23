using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class MsSkinWindow : Window
{
    public MsSkinWindow()
    {
        InitializeComponent();
    }

    public MsSkinWindow(LaunchViewModel model) : this()
    {
        DataContext = model;
    }

    private async void OnUploadSkin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择皮肤 PNG（64×64 或 64×32）",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        });
        if (files.Count == 0) return;
        model.AccountStatus = await model.UploadMicrosoftSkinAsync(files[0].Path.LocalPath);
    }

    private async void OnSaveSkin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model ||
            string.IsNullOrWhiteSpace(model.AccountSkinPath) ||
            !File.Exists(model.AccountSkinPath))
            return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存皮肤文件",
            SuggestedFileName = model.PlayerName + "-skin.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        });
        if (file is null) return;
        File.Copy(model.AccountSkinPath, file.Path.LocalPath, true);
        model.AccountStatus = "皮肤文件已保存：" + file.Path.LocalPath;
    }

    private void OnRedeemCdkey(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.minecraft.net/zh-hans/redeem")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            if (DataContext is LaunchViewModel model) model.AccountStatus = "打开 CDKEY 兑换页失败：" + ex.Message;
        }
    }

    private void OnCloset(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        var url = model.AuthlibSkinClosetUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            model.AccountStatus = "当前认证服务器没有可用的皮肤衣柜地址";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            model.AccountStatus = "打开皮肤衣柜失败：" + ex.Message;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
