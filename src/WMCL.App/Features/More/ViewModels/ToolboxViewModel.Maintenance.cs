using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.Features.More;

public partial class ToolboxViewModel
{
    [RelayCommand]
    private void OpenLogs() => Open(WmclPaths.LogsDirectory);

    [RelayCommand]
    private void OpenGame() => Open(_services.Settings.GameDirectory);

    [RelayCommand]
    private void OpenConfig() => Open(WmclPaths.ConfigDirectory);

    private void Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            ExternalLauncher.Open(path);
            Status = "已打开：" + path;
        }
        catch (Exception ex)
        {
            Status = "打开失败：" + ex.Message;
        }
    }

    // ── 清理垃圾 ──

    [RelayCommand]
    private async Task CleanJunkAsync()
    {
        if (!CanCleanJunk) return;
        IsCleaningJunk = true;
        JunkProgress = 0;
        JunkStatus = "正在扫描…";
        try
        {
            var totalBytes = 0L;
            var cleanedBytes = 0L;
            var targets = new List<(string Desc, string Path)>
            {
                ("临时文件", Path.Combine(WmclPaths.DataDirectory, "temp")),
                ("缓存文件", Path.Combine(WmclPaths.DataDirectory, "cache")),
            };

            foreach (var (desc, path) in targets)
            {
                if (!Directory.Exists(path)) continue;
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        totalBytes += fi.Length;
                        fi.Delete();
                        cleanedBytes += fi.Length;
                    }
                    catch { /* 跳过无法删除的文件 */ }
                }
                JunkProgress += 25;
            }

            // downloads 目录同时是“自定义下载”的默认保存位置（StartCustomDownloadAsync），
            // 不能整体清空；只按缓存语义清理 7 天前的旧文件，避免误删用户刚下载的内容。
            var downloads = Path.Combine(WmclPaths.DataDirectory, "downloads");
            if (Directory.Exists(downloads))
            {
                var staleCutoff = DateTime.Now.AddDays(-7);
                foreach (var file in Directory.GetFiles(downloads, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        totalBytes += fi.Length;
                        if (fi.LastWriteTime >= staleCutoff) continue;
                        fi.Delete();
                        cleanedBytes += fi.Length;
                    }
                    catch { /* 跳过无法删除的文件 */ }
                }
                JunkProgress += 25;
            }

            // 清理旧日志（保留最近 7 天的）
            var logsDir = WmclPaths.LogsDirectory;
            if (Directory.Exists(logsDir))
            {
                var cutoff = DateTime.Now.AddDays(-7);
                foreach (var logFile in Directory.GetFiles(logsDir, "*.log"))
                {
                    try
                    {
                        var fi = new FileInfo(logFile);
                        if (fi.LastWriteTime < cutoff)
                        {
                            totalBytes += fi.Length;
                            fi.Delete();
                            cleanedBytes += fi.Length;
                        }
                    }
                    catch { /* 跳过 */ }
                }
                JunkProgress = 100;
            }

            JunkStatus = cleanedBytes > 0
                ? $"已清理 {cleanedBytes / 1024 / 1024:F1} MB"
                : "没有可清理的文件";
            Status = JunkStatus;
        }
        catch (Exception ex)
        {
            JunkStatus = "清理失败：" + ex.Message;
        }
        finally
        {
            IsCleaningJunk = false;
        }
    }

    // ── 启动计数 ──

    [RelayCommand]
    private void IncrementLaunchCount()
    {
        LaunchCount++;
        SaveLaunchCount();
        LaunchCountStatus = $"本次启动是第 {LaunchCount} 次";
        Status = LaunchCountStatus;
    }

    private void LoadLaunchCount()
    {
        var path = Path.Combine(WmclPaths.DataDirectory, "launch_count.txt");
        if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var count))
        {
            LaunchCount = count;
        }
        LaunchCountStatus = $"已启动 {LaunchCount} 次";
    }

    private void SaveLaunchCount()
    {
        var path = Path.Combine(WmclPaths.DataDirectory, "launch_count.txt");
        Directory.CreateDirectory(WmclPaths.DataDirectory);
        File.WriteAllText(path, LaunchCount.ToString());
    }

    // ── 创建快捷方式 ──

    [RelayCommand]
    private void CreateShortcut()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrWhiteSpace(exePath))
            {
                ShortcutStatus = "无法获取程序路径";
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                // 使用 PowerShell 创建桌面快捷方式
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var linkPath = Path.Combine(desktop, "WMCL.lnk");
                var psi = new ProcessStartInfo("powershell", "-Command")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add(
                    $"$ws = New-Object -ComObject WScript.Shell; " +
                    $"$link = $ws.CreateShortcut('{linkPath}'); " +
                    $"$link.TargetPath = '{exePath}'; $link.Save()");
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
                ShortcutStatus = "已在桌面创建快捷方式";
            }
            else if (OperatingSystem.IsMacOS())
            {
                ShortcutStatus = "macOS 请将 .app 拖到应用程序文件夹";
            }
            else
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var linkPath = Path.Combine(desktop, "WMCL.desktop");
                var content = $"[Desktop Entry]\nType=Application\nName=WMCL\nExec={exePath}\nIcon={exePath}\n";
                File.WriteAllText(linkPath, content);
                ShortcutStatus = "已在桌面创建 .desktop 文件";
            }
            Status = ShortcutStatus;
        }
        catch (Exception ex)
        {
            ShortcutStatus = "创建快捷方式失败：" + ex.Message;
            Status = ShortcutStatus;
        }
    }
}
