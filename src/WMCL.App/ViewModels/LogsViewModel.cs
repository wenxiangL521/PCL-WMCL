using System.Collections.ObjectModel;
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
    public LogsViewModel(AppServices services) : this(services, null, null, null) { }

    internal LogsViewModel(AppServices services, string? logsDirectory,
        Func<string, string, Task<bool>>? confirm, Action<string>? open)
    {
        _services = services;
        _directory = Path.GetFullPath(logsDirectory ?? WmclPaths.LogsDirectory);
        _confirm = confirm ?? ((title, message) => DialogService.ConfirmAsync(title, message));
        _open = open ?? ExternalLauncher.Open;
    }

    private readonly AppServices _services;
    private readonly string _directory;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly Action<string> _open;
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
