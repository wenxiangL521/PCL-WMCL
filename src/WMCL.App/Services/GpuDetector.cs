using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WMCL.App.Services;

/// <summary>
/// 跨平台 GPU 检测。Linux 下读取 lspci / glxinfo / DRI_PRIME 候选；
/// 其他平台返回空列表（无法可靠检测时回退到用户手动选择）。
/// 检测结果用于设置页的显卡选择下拉框。
/// </summary>
public static class GpuDetector
{
    public sealed record GpuDevice(string Index, string Label, string EnvValue);

    /// <summary>检测可用 GPU 列表。失败时返回空列表。</summary>
    public static IReadOnlyList<GpuDevice> Detect()
    {
        if (OperatingSystem.IsLinux())
            return DetectLinux();
        return [];
    }

    private static List<GpuDevice> DetectLinux()
    {
        var devices = new List<GpuDevice>();
        try
        {
            var output = RunProcess("lspci", "-nn");
            if (string.IsNullOrEmpty(output)) return devices;

            var matches = Regex.Matches(output, VgaPattern, RegexOptions.IgnoreCase);
            for (var i = 0; i < matches.Count; i++)
            {
                var label = matches[i].Groups[1].Value.Trim();
                var device = new GpuDevice(i.ToString(), label, i.ToString());
                devices.Add(device);
            }
        }
        catch
        {
            // lspci 不存在或执行失败，回退到手动选择
        }
        return devices;
    }

    private static string? RunProcess(string fileName, string args)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi);
        if (process is null) return null;
        // 异步读取+超时等待：同步 ReadToEnd 在管道写满时会阻塞，
        // 且 WaitForExit(5000) 的超时保护不了已经卡住的读操作。
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(true); } catch { /* 进程可能已退出 */ }
        }
        return output.IsCompleted ? output.Result : null;
    }

    /// <summary>
    /// 匹配 lspci 输出中的 VGA/3D/Display 设备行。
    /// 示例：00:02.0 VGA compatible controller: Intel Corporation ...
    /// </summary>
    private const string VgaPattern =
        @"(?:VGA compatible controller|3D controller|Display controller)[^\n]*?:\s*(.+)";
}
