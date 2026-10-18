using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCL.App.ViewModels;

public sealed record TaskEntry(
    string Title,
    string Detail,
    double Progress,
    string PercentText,
    Action? Cancel)
{
    public bool HasCancel => Cancel is not null;
}

public partial class TaskManagerViewModel : ViewModelBase
{
    private readonly DownloadViewModel _download;
    private readonly ModsViewModel _mods;
    private readonly ModpackViewModel _modpack;
    private readonly InstanceContentViewModel _content;
    private readonly InstanceViewModel _instance;
    private readonly InstallViewModel _install;

    public TaskManagerViewModel(
        DownloadViewModel download,
        ModsViewModel mods,
        ModpackViewModel modpack,
        InstanceContentViewModel content,
        InstanceViewModel instance,
        InstallViewModel install)
    {
        _download = download;
        _mods = mods;
        _modpack = modpack;
        _content = content;
        _instance = instance;
        _install = install;

        foreach (var source in new INotifyPropertyChanged[]
                 {
                     download, mods, modpack, content, instance, install
                 })
            source.PropertyChanged += (_, _) => Refresh();

        Refresh();
    }

    public ObservableCollection<TaskEntry> Tasks { get; } = [];

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _overallText = "没有进行中的任务";

    [ObservableProperty]
    private bool _hasTasks;

    public void Refresh()
    {
        var tasks = new List<TaskEntry>();

        if (_download.IsBusy && _download.CanCancel)
            tasks.Add(new TaskEntry(
                "安装 / 校验 Minecraft",
                Detail(_download.ProgressText, _download.Status),
                _download.Progress,
                Percent(_download.Progress),
                () => _download.CancelCommand.Execute(null)));

        if (_mods.IsBusy)
            tasks.Add(new TaskEntry(
                "安装 Fabric",
                Detail(_mods.ProgressText, _mods.Status),
                _mods.Progress,
                Percent(_mods.Progress),
                () => _mods.CancelCommand.Execute(null)));

        if (_mods.IsForgelikeBusy)
            tasks.Add(new TaskEntry(
                "安装 Forge / NeoForge",
                Detail(_mods.ForgelikeProgressText, _mods.ForgelikeStatus),
                _mods.ForgelikeProgress,
                Percent(_mods.ForgelikeProgress),
                () => _mods.CancelForgelikeCommand.Execute(null)));

        if (_mods.IsOptiFineBusy)
            tasks.Add(new TaskEntry(
                "安装 OptiFine",
                Detail(_mods.OptiFineProgressText, _mods.OptiFineStatus),
                _mods.OptiFineProgress,
                Percent(_mods.OptiFineProgress),
                () => _mods.CancelOptiFineCommand.Execute(null)));

        if (_install.IsCombinedBusy)
            tasks.Add(new TaskEntry(
                "组合安装",
                Detail(_install.CombinedProgressText, _install.CombinedStatus),
                _install.CombinedProgress,
                Percent(_install.CombinedProgress),
                null));

        if (_modpack.IsBusy)
            tasks.Add(new TaskEntry(
                "安装整合包",
                Detail(_modpack.ProgressText, _modpack.Status),
                _modpack.Progress,
                Percent(_modpack.Progress),
                () => _modpack.CancelCommand.Execute(null)));

        if (_content.IsDownloadBusy)
            tasks.Add(new TaskEntry(
                "下载资源",
                Detail(_content.DownloadProgressText, _content.DownloadStatus),
                _content.DownloadProgress,
                Percent(_content.DownloadProgress),
                null));

        if (_instance.IsImporting)
            tasks.Add(new TaskEntry(
                "导入实例",
                _instance.Status,
                0,
                "…",
                null));

        Tasks.Clear();
        foreach (var task in tasks) Tasks.Add(task);

        HasTasks = tasks.Count > 0;
        OverallProgress = tasks.Count == 0 ? 0 : tasks.Average(t => t.Progress);
        OverallText = tasks.Count == 0
            ? "没有进行中的任务"
            : $"{tasks.Count} 个任务 · {OverallProgress:0}%";
    }

    [RelayCommand]
    private void CancelTask(TaskEntry? task)
    {
        task?.Cancel?.Invoke();
        Refresh();
    }

    [RelayCommand]
    private void CancelAll()
    {
        foreach (var task in Tasks) task.Cancel?.Invoke();
        Refresh();
    }

    private static string Detail(string? progressText, string? status) =>
        !string.IsNullOrWhiteSpace(progressText)
            ? progressText
            : status ?? "";

    private static string Percent(double progress) =>
        progress > 0 ? $"{progress:0}%" : "…";
}
