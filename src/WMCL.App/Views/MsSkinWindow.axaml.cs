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
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择皮肤 PNG（64×64 或 64×32）",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
            });
            if (files.Count == 0) return;
            model.AccountStatus = await model.UploadMicrosoftSkinAsync(files[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            model.AccountStatus = "上传皮肤失败：" + ex.Message;
        }
    }

    private void OnEditSkin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        if (model.AccountSkin is null)
        {
            model.AccountStatus = "当前没有可用皮肤";
            return;
        }
        SkinEditPanel.IsVisible = true;
        model.Editor.LoadSkin(model.AccountSkin);
        model.AccountStatus = "正在编辑皮肤…";
    }

    private void OnBrushSize(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        if (rb.IsChecked != true) return;
        if (DataContext is not LaunchViewModel model) return;
        var content = rb.Content?.ToString() ?? "";
        // Content 形如 "1×"、"2×"，需要剥离非数字字符
        var digits = new string(content.TakeWhile(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var size))
        {
            model.Editor.BrushSize = size;
            // 画笔与橡皮擦是同一分组的单选按钮：选画笔字号意味着退出擦除模式，
            // 否则 UI 显示画笔已选中、实际仍在擦除。
            model.Editor.IsEraser = false;
        }
    }

    private void OnEraser(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.Editor.ToggleEraserCommand.Execute(null);
    }

    private void OnFill(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.Editor.FillAtLast();
    }

    private void OnMirrorH(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.Editor.MirrorLeftToRight();
    }

    private void OnMirrorV(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.Editor.MirrorTopToBottom();
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.Editor.Clear();
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.Editor.UndoCommand.Execute(null);
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.Editor.RedoCommand.Execute(null);
    }

    private async void OnApplyEdit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        model.AccountStatus = await model.SaveEditedSkinAsync(model.Editor);
    }

    private void OnCancelEdit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model) return;
        SkinEditPanel.IsVisible = false;
        model.AccountStatus = "已取消编辑";
    }

    private async void OnSaveSkin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel model ||
            string.IsNullOrWhiteSpace(model.AccountSkinPath) ||
            !File.Exists(model.AccountSkinPath))
            return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
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
        catch (Exception ex)
        {
            model.AccountStatus = "保存皮肤失败：" + ex.Message;
        }
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
