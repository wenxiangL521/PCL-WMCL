using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;

namespace WMCL.App.ViewModels;

public sealed record LogFileItem(string Name, string Path)
{
    public string SizeText
    {
        get
        {
            try
            {
                var size = new FileInfo(Path).Length;
                return size >= 1024 * 1024
                    ? $"{size / 1024.0 / 1024.0:0.0} MB"
                    : $"{size / 1024.0:0.0} KB";
            }
            catch
            {
                return "";
            }
        }
    }

    public string DateText
    {
        get
        {
            try
            {
                return File.GetLastWriteTime(Path).ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "";
            }
        }
    }
}

public partial class LogsViewModel : ViewModelBase
{
    public LogsViewModel(AppServices services)
    {
        _services = services;
    }

    private readonly AppServices _services;
    public ObservableCollection<LogFileItem> Logs { get; } = [];
    public string LogsDirectory => WmclPaths.LogsDirectory;

    [ObservableProperty]
    private LogFileItem? _selectedLog;

    [ObservableProperty]
    private string _status = "选择日志文件查看";

    [RelayCommand]
    public void Refresh()
    {
        var selected = SelectedLog?.Path;
        Logs.Clear();
        if (Directory.Exists(LogsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(LogsDirectory)
                         .Where(path => path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(File.GetLastWriteTime))
                Logs.Add(new LogFileItem(Path.GetFileName(path), path));
        }

        SelectedLog = Logs.FirstOrDefault(log => log.Path == selected) ?? Logs.FirstOrDefault();
        Status = Logs.Count == 0 ? "暂无日志文件" : $"共 {Logs.Count} 个日志文件";
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(LogsDirectory);
        OpenPath(LogsDirectory);
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedLog is not null) OpenPath(SelectedLog.Path);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedLog is null) return;
        if (!await DialogService.ConfirmAsync(
                "删除日志",
                $"确定删除 {SelectedLog.Name}？"))
            return;
        try
        {
            var name = SelectedLog.Name;
            File.Delete(SelectedLog.Path);
            Refresh();
            Status = $"已删除 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ClearOldAsync()
    {
        if (Logs.Count == 0) return;
        if (!await DialogService.ConfirmAsync(
                "清理日志",
                "确定删除日志目录下所有 .log/.txt 文件？"))
            return;
        try
        {
            foreach (var log in Logs.ToList())
            {
                try { File.Delete(log.Path); } catch { /* 跳过被占用的文件 */ }
            }
            Refresh();
            Status = "已清理历史日志";
        }
        catch (Exception ex)
        {
            Status = "清理失败：" + ex.Message;
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? path : "xdg-open",
                Arguments = OperatingSystem.IsWindows() ? "" : path,
                UseShellExecute = OperatingSystem.IsWindows()
            };
            Process.Start(start);
        }
        catch
        {
            // 打开失败不阻塞页面
        }
    }
}
