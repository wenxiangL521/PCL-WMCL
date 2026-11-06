using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace WMCL.App.Services;

/// <summary>把「找主窗口 → 取剪贴板」的细节收进服务，ViewModel 不再直接依赖 ApplicationLifetime。</summary>
public static class ClipboardService
{
    /// <summary>复制文本；无可用剪贴板时返回 false。</summary>
    public static bool TrySetText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (GetClipboard() is not { } clipboard) return false;
        try
        {
            _ = clipboard.SetTextAsync(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>异步复制，返回是否成功。</summary>
    public static async Task<bool> SetTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (GetClipboard() is not { } clipboard) return false;
        try
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IClipboard? GetClipboard()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not { } window)
                return null;
            return TopLevel.GetTopLevel(window)?.Clipboard;
        }
        catch
        {
            return null;
        }
    }
}
