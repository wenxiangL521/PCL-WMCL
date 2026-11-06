using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Input;

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
            _ = clipboard.SetValueAsync(DataFormat.Text, text);
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
            await clipboard.SetValueAsync(DataFormat.Text, text).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 读取文本。XWayland 下部分合成器（如 niri）不会把 Wayland 文本同步到 X11 选择区，
    /// 此时退回 wl-paste 直接读取；其它平台直接读 Avalonia 剪贴板。
    /// </summary>
    public static async Task<string?> ReadTextAsync(CancellationToken ct = default)
    {
        var wayland = await ReadViaWlPasteAsync(ct);
        if (!string.IsNullOrEmpty(wayland)) return wayland;
        try
        {
            return GetClipboard() is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ReadViaWlPasteAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return null;
        try
        {
            var startInfo = new ProcessStartInfo("wl-paste")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--no-newline");
            startInfo.ArgumentList.Add("--type");
            startInfo.ArgumentList.Add("text/plain");

            using var process = Process.Start(startInfo);
            if (process is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var text = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 && !string.IsNullOrEmpty(text) ? text : null;
        }
        catch
        {
            return null;
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
