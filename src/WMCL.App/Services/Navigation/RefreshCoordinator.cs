using System.ComponentModel;
using WMCL.App.Features.Downloads;
using WMCL.App.Features.Instances;
using WMCL.App.Features.Mods;
using WMCL.App.ViewModels;
using WMCL.Core.Jobs;

namespace WMCL.App.Services.Navigation;

/// <summary>
/// 跨页面的刷新与联动：游戏目录变化、安装完成、实例改名、任务属性变化统一在这里订阅，
/// 不再散落在 <c>MainWindowViewModel</c> 的构造函数里。
/// </summary>
public sealed class RefreshCoordinator : IDisposable
{
    private readonly AppServices _services;
    private readonly LaunchViewModel _launch;
    private readonly InstanceViewModel _instance;
    private readonly InstanceContentViewModel _content;
    private readonly ModsViewModel _mods;
    private readonly ModpackViewModel _modpack;
    private readonly InstallViewModel _install;
    private readonly VersionSelectViewModel _versionSelect;
    private readonly DownloadViewModel _download;
    private readonly CleanroomViewModel _cleanroom;
    private readonly ResourceFavoriteBatchViewModel? _favoriteBatch;
    private readonly Func<Task> _reloadGameFolder;
    private readonly Func<string, Task> _openInstanceSettings;
    private readonly Action _refreshTaskState;
    private readonly Action<InstanceChangeEventArgs> _instanceChanged;
    private readonly Action<InstallCompletedEventArgs> _installCompleted;
    private readonly Action _existingInstallSubmitted;
    private readonly Func<TaskEntry, Task> _installTaskFinished;

    public RefreshCoordinator(
        AppServices services,
        LaunchViewModel launch,
        InstanceViewModel instance,
        InstanceContentViewModel content,
        ModsViewModel mods,
        ModpackViewModel modpack,
        InstallViewModel install,
        VersionSelectViewModel versionSelect,
        DownloadViewModel download,
        CleanroomViewModel cleanroom,
        ResourceFavoriteBatchViewModel? favoriteBatch,
        Func<Task> reloadGameFolder,
        Func<string, Task> openInstanceSettings,
        Action refreshTaskState,
        Action<InstanceChangeEventArgs> instanceChanged,
        Action<InstallCompletedEventArgs> installCompleted,
        Action existingInstallSubmitted,
        Func<TaskEntry, Task> installTaskFinished)
    {
        _services = services;
        _launch = launch;
        _instance = instance;
        _content = content;
        _mods = mods;
        _modpack = modpack;
        _install = install;
        _versionSelect = versionSelect;
        _download = download;
        _cleanroom = cleanroom;
        _favoriteBatch = favoriteBatch;
        _reloadGameFolder = reloadGameFolder;
        _openInstanceSettings = openInstanceSettings;
        _refreshTaskState = refreshTaskState;
        _instanceChanged = instanceChanged;
        _installCompleted = installCompleted;
        _existingInstallSubmitted = existingInstallSubmitted;
        _installTaskFinished = installTaskFinished;
        Wire();
    }

    private void Wire()
    {
        _services.GameDirectoryChanged += OnGameDirectoryChanged;
        _cleanroom.VersionInstalled += OnLaunchReloadWithId;
        _mods.VersionInstalled += OnLaunchReload;
        _instance.InstanceSettingsSaved += OnLaunchReload;
        _versionSelect.InstanceInstalled += OnInstanceInstalled;
        _modpack.InstanceInstalled += OnInstanceInstalled;
        _launch.VersionSettingsRequested += OnVersionSettingsRequested;
        _versionSelect.VersionSettingsRequested += OnVersionSettingsRequested;
        _install.InstallCompleted += (_, result) => _installCompleted(result);
        _install.ExistingInstallSubmitted += (_, _) => _existingInstallSubmitted();
        _install.InstancesChanged += (_, change) => _instanceChanged(change);
        _instance.InstancesChanged += (_, change) => _instanceChanged(change);
        _install.TaskFinished += (_, task) => _ = _installTaskFinished(task);

        foreach (var source in TaskSources())
            source.PropertyChanged += OnTaskPropertyChanged;
    }

    private IEnumerable<INotifyPropertyChanged> TaskSources()
    {
        yield return _download;
        yield return _mods;
        yield return _modpack;
        yield return _content;
        yield return _instance;
        yield return _install;
        yield return _launch;
        if (_favoriteBatch is not null) yield return _favoriteBatch;
    }

    public void Dispose()
    {
        _services.GameDirectoryChanged -= OnGameDirectoryChanged;
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e) => _refreshTaskState();

    private void OnGameDirectoryChanged(object? sender, string directory) => _ = _reloadGameFolder();

    private void OnLaunchReload(object? sender, EventArgs e) => _ = _launch.LoadAsync();

    private void OnLaunchReloadWithId(object? sender, string id) => _ = _launch.LoadAsync();

    private void OnInstanceInstalled(object? sender, EventArgs e)
    {
        _ = _launch.LoadAsync();
        _ = _instance.LoadAsync();
        _ = _content.LoadAsync();
        _ = _mods.LoadAsync();
    }

    private async void OnVersionSettingsRequested(object? sender, string id) =>
        await _openInstanceSettings(id);
}
