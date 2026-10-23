using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Platform.Storage;

namespace WMCL.App.Services;

public static class DialogService
{
    public static async Task ShowMessageAsync(string title, string message)
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }) return;
        await ShowMessageAsync(owner, title, message);
    }

    internal static async Task ShowMessageAsync(Avalonia.Controls.Window owner, string title, string message)
    {
        var window = new Views.ConfirmWindow(title, message, "确定", showCancel: false);
        await window.ShowDialog<bool>(owner);
    }

    public static async Task<string?> SaveInstallerAsync(string filename, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }) return null;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择保存位置",
            SuggestedFileName = filename,
            DefaultExtension = "jar",
            FileTypeChoices = [new FilePickerFileType("安装器") { Patterns = ["*.jar"] }],
            ShowOverwritePrompt = true
        });
        ct.ThrowIfCancellationRequested();
        if (file is null) return null;
        return file.TryGetLocalPath() ?? throw new IOException("无法保存到非本地文件路径。");
    }

    public static async Task<string?> PromptAsync(
        string title, string message, string initial = "")
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return null;

        var window = new Views.InputWindow(title, message, initial);
        return await window.ShowDialog<string?>(owner);
    }

    public static Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default) =>
        ConfirmAsync(title, message, "确定删除", ct);

    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return false;

        return await ConfirmAsync(owner, title, message, confirmText, ct);
    }

    public static Task<bool> ConfirmAsync(Avalonia.Controls.Window owner, string title, string message, CancellationToken ct = default) =>
        ConfirmAsync(owner, title, message, "确定删除", ct);

    public static async Task<bool> ConfirmAsync(Avalonia.Controls.Window owner, string title, string message, string confirmText, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var window = new Views.ConfirmWindow(title, message, confirmText);
        var result = window.ShowDialog<bool>(owner);
        using var registration = ct.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (window.IsVisible) window.Close(false);
        }));
        var confirmed = await result;
        ct.ThrowIfCancellationRequested();
        return confirmed;
    }
}
