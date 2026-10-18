using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Minecraft;

namespace PCL.App.ViewModels;

public partial class InstanceViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public InstanceViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<VersionItem> Instances { get; } = [];
    public string GameDirectory => _services.Settings.GameDirectory;
    public bool HasInstances => Instances.Count > 0;
    public string SelectedInstanceTitle => SelectedInstance?.Id ?? "未选择实例";
    public string SelectedInstanceInfo => SelectedInstance is null
        ? "从左侧选择一个实例"
        : $"{SelectedInstance.TypeText} · {SelectedInstance.ReleaseTime ?? "未知时间"}";
    public string SelectedInstancePath => SelectedInstance is null
        ? GameDirectory
        : Path.Combine(GameDirectory, "versions", SelectedInstance.Id);

    [ObservableProperty]
    private VersionItem? _selectedInstance;

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private bool _useGlobalMemory = true;

    [ObservableProperty]
    private decimal _instanceMemoryMb = 4096;

    [ObservableProperty]
    private bool _useGlobalJava = true;

    [ObservableProperty]
    private string _instanceJavaPath = "";

    [ObservableProperty]
    private string _instanceJvmArgs = "";

    [ObservableProperty]
    private string _instanceGameArgs = "";

    public bool CanEditMemory => !UseGlobalMemory;
    public bool CanEditJava => !UseGlobalJava;

    partial void OnUseGlobalMemoryChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditMemory));

    partial void OnUseGlobalJavaChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditJava));

    partial void OnSelectedInstanceChanged(VersionItem? value)
    {
        OnPropertyChanged(nameof(SelectedInstanceTitle));
        OnPropertyChanged(nameof(SelectedInstanceInfo));
        OnPropertyChanged(nameof(SelectedInstancePath));
        LoadInstanceSettings(value);
    }

    public async Task LoadAsync()
    {
        Status = "正在扫描实例…";
        try
        {
            var installed = await Task.Run(() => _services.Repository.ListInstalled());
            Instances.Clear();
            foreach (var version in installed)
            {
                Instances.Add(new VersionItem
                {
                    Id = version.Id,
                    Type = version.Type ?? "release",
                    ReleaseTime = version.ReleaseTime,
                    IsInstalled = true
                });
            }

            SelectedInstance = Instances.FirstOrDefault();
            if (SelectedInstance is not null) LoadInstanceSettings(SelectedInstance);
            Status = Instances.Count == 0 ? "还没有安装任何版本" : $"共 {Instances.Count} 个实例";
        }
        catch (Exception ex)
        {
            Status = "扫描失败：" + ex.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(HasInstances));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private void LoadInstanceSettings(VersionItem? instance)
    {
        if (instance is null) return;

        var settings = InstanceSettingsStore.Load(
            Path.Combine(GameDirectory, "versions", instance.Id));
        UseGlobalMemory = settings.MemoryMb is null;
        InstanceMemoryMb = settings.MemoryMb ?? _services.Settings.MemoryMb;
        UseGlobalJava = string.IsNullOrWhiteSpace(settings.JavaPath);
        InstanceJavaPath = settings.JavaPath ?? "";
        InstanceJvmArgs = settings.ExtraJvmArgs;
        InstanceGameArgs = settings.ExtraGameArgs;
    }

    [RelayCommand]
    private void SaveInstanceSettings()
    {
        if (SelectedInstance is null) return;

        InstanceSettingsStore.Save(
            Path.Combine(GameDirectory, "versions", SelectedInstance.Id),
            new InstanceSettings
            {
                MemoryMb = UseGlobalMemory ? null : (int)InstanceMemoryMb,
                JavaPath = UseGlobalJava || string.IsNullOrWhiteSpace(InstanceJavaPath)
                    ? null
                    : InstanceJavaPath.Trim(),
                ExtraJvmArgs = InstanceJvmArgs.Trim(),
                ExtraGameArgs = InstanceGameArgs.Trim()
            });
        Status = $"已保存 {SelectedInstance.Id} 的实例设置";
    }

    [RelayCommand]
    private void ResetInstanceSettings()
    {
        if (SelectedInstance is null) return;
        InstanceSettingsStore.Reset(
            Path.Combine(GameDirectory, "versions", SelectedInstance.Id));
        LoadInstanceSettings(SelectedInstance);
        Status = $"已重置 {SelectedInstance.Id} 的实例设置";
    }

    [RelayCommand]
    private async Task ExportInstanceAsync()
    {
        if (SelectedInstance is null) return;
        Status = $"正在导出 {SelectedInstance.Id}…";
        try
        {
            var result = await InstanceExporter.ExportAsync(
                GameDirectory, SelectedInstance.Id);
            Status = $"已导出 {result.FileCount} 个文件：{result.ArchivePath}";
        }
        catch (Exception ex)
        {
            Status = "导出失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = SelectedInstance is null
            ? GameDirectory
            : Path.Combine(GameDirectory, "versions", SelectedInstance.Id);
        OpenPath(Directory.Exists(path) ? path : GameDirectory);
    }

    [RelayCommand]
    private void OpenGameFolder() => OpenPath(GameDirectory);

    private static void OpenPath(string path)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
                startInfo = new ProcessStartInfo("explorer.exe", path);
            else if (OperatingSystem.IsMacOS())
                startInfo = new ProcessStartInfo("open", path);
            else
                startInfo = new ProcessStartInfo("xdg-open", path);

            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
        }
        catch
        {
            // 打开目录失败不阻塞 UI
        }
    }
}
