using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;

namespace WMCL.App.Features.Settings;

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
    public LogsViewModel(IUserSettingsProvider services) : this(services, null, null, null) { }

    internal LogsViewModel(IUserSettingsProvider services, string? logsDirectory,
        Func<string, string, Task<bool>>? confirm, Action<string>? open,
        Func<string, Task<string?>>? pickBundlePath = null)
    {
        _pickBundlePath = pickBundlePath;
        _services = services;
        _directory = Path.GetFullPath(logsDirectory ?? WmclPaths.LogsDirectory);
        _confirm = confirm ?? ((title, message) => DialogService.ConfirmAsync(title, message));
        _open = open ?? ExternalLauncher.Open;
    }

    private readonly IUserSettingsProvider _services;
    private readonly string _directory;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly Action<string> _open;
    private Func<string, Task<string?>>? _pickBundlePath;
    public ObservableCollection<LogFileItem> Logs { get; } = [];
    public string LogsDirectory => _directory;

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

    /// <summary>一键导出诊断包：环境信息 + 脱敏设置 + 启动器日志 + 实例日志/崩溃报告。</summary>
    [RelayCommand]
    private async Task ExportBundleAsync()
    {
        var path = _pickBundlePath is null
            ? await DialogService.PickLogBundlePathAsync(
                "WMCL-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip")
            : await _pickBundlePath("WMCL-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
        if (string.IsNullOrWhiteSpace(path)) return;
        Status = "正在导出诊断日志…";
        try
        {
            var result = await LogBundleExporter.ExportAsync(_services, path);
            Status = $"已导出 {result.FileCount} 个文件：{result.Path}";
            _open(Path.GetDirectoryName(result.Path) ?? LogsDirectory);
        }
        catch (Exception exception)
        {
            Status = "导出失败：" + exception.Message;
        }
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
        var target = SelectedLog;
        if (target is null) return;
        if (!await _confirm(
                "删除日志",
                $"确定删除 {target.Name}？"))
            return;
        try
        {
            File.Delete(target.Path);
            Refresh();
            Status = $"已删除 {target.Name}";
        }
        catch (Exception ex)
        {
            Status = "删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ClearOldAsync()
    {
        var targets = Logs.ToArray();
        if (targets.Length == 0) return;
        if (!await _confirm(
                "清理日志",
                $"确定删除当前列表中的 {targets.Length} 个 .log/.txt 文件？"))
            return;
        try
        {
            var deleted = 0;
            var skipped = 0;
            foreach (var log in targets)
            {
                try
                {
                    if (!File.Exists(log.Path) && !Directory.Exists(log.Path)) continue;
                    File.Delete(log.Path);
                    deleted++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { skipped++; }
            }
            Refresh();
            Status = $"已清理 {deleted} 个日志" + (skipped > 0 ? $"，跳过 {skipped} 个无法删除的文件" : "");
        }
        catch (Exception ex)
        {
            Status = "清理失败：" + ex.Message;
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            _open(path);
        }
        catch (Exception exception)
        {
            Status = "打开失败：" + exception.Message;
        }
    }
}
