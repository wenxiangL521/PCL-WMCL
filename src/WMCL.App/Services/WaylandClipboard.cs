using System.Diagnostics;

namespace WMCL.App.Services;

/// <summary>
/// XWayland 剪贴板兜底。WMCL 当前走 Avalonia X11 后端，
/// 部分 Wayland 合成器（如 niri）没有把 Wayland 文本同步到 X11 选择区，
/// 导致 Ctrl+V 读不到文本；此时直接调用 wl-paste 读取。
/// </summary>
internal static class WaylandClipboard
{
    public static async Task<string?> ReadTextAsync(CancellationToken ct = default)
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
