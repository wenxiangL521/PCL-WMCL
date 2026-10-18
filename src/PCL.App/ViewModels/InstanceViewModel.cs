using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Minecraft;
using PCL.Modding.Modpack;

namespace PCL.App.ViewModels;

public partial class InstanceViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public InstanceViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<VersionItem> Instances { get; } = [];
    public event EventHandler? InstancesChanged;
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

    [ObservableProperty]
    private bool _useGlobalWindow = true;

    [ObservableProperty]
    private decimal _instanceWidth = 854;

    [ObservableProperty]
    private decimal _instanceHeight = 480;

    [ObservableProperty]
    private string _instanceServer = "";

    [ObservableProperty]
    private decimal _instanceServerPort = 25565;

    [ObservableProperty]
    private string _instanceAuthServer = "";

    [ObservableProperty]
    private string _instanceAuthServerName = "";

    [ObservableProperty]
    private string _instancePreLaunchCommand = "";

    [ObservableProperty]
    private bool _instancePreLaunchWait = true;

    public bool CanEditWindow => !UseGlobalWindow;

    partial void OnUseGlobalWindowChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditWindow));

    [ObservableProperty]
    private bool _exportMods = true;

    [ObservableProperty]
    private bool _exportConfigs = true;

    [ObservableProperty]
    private bool _exportResourcePacks = true;

    [ObservableProperty]
    private bool _exportShaderPacks = true;

    [ObservableProperty]
    private bool _exportSaves = true;

    [ObservableProperty]
    private bool _exportPcl = true;

    [ObservableProperty]
    private bool _exportOptions = true;

    [ObservableProperty]
    private string _importPath = "";

    [ObservableProperty]
    private bool _isImporting;

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
        UseGlobalWindow = settings.Width is null && settings.Height is null;
        InstanceWidth = settings.Width ?? _services.Settings.Width;
        InstanceHeight = settings.Height ?? _services.Settings.Height;
        InstanceServer = settings.Server;
        InstanceServerPort = settings.Port ?? 25565;
        InstanceAuthServer = settings.AuthServer;
        InstanceAuthServerName = settings.AuthServerName;
        InstancePreLaunchCommand = settings.PreLaunchCommand;
        InstancePreLaunchWait = settings.PreLaunchWait;
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
                ExtraGameArgs = InstanceGameArgs.Trim(),
                Width = UseGlobalWindow ? null : (int)InstanceWidth,
                Height = UseGlobalWindow ? null : (int)InstanceHeight,
                Server = InstanceServer.Trim(),
                Port = string.IsNullOrWhiteSpace(InstanceServer)
                    ? null
                    : (int)InstanceServerPort,
                AuthServer = InstanceAuthServer.Trim(),
                AuthServerName = InstanceAuthServerName.Trim(),
                PreLaunchCommand = InstancePreLaunchCommand.Trim(),
                PreLaunchWait = InstancePreLaunchWait
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
    private async Task CloneInstanceAsync()
    {
        if (SelectedInstance is null) return;
        var name = await DialogService.PromptAsync(
            "克隆实例", "输入新实例名：", SelectedInstance.Id + "-copy");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var id = InstanceManager.Clone(GameDirectory, SelectedInstance.Id, name);
            await LoadAsync();
            SelectedInstance = Instances.FirstOrDefault(version =>
                string.Equals(version.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            Status = $"已克隆为：{id}";
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "克隆失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RenameInstanceAsync()
    {
        if (SelectedInstance is null) return;
        var name = await DialogService.PromptAsync(
            "重命名实例", "输入新的实例名：", SelectedInstance.Id);
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, SelectedInstance.Id, StringComparison.Ordinal))
            return;

        try
        {
            var oldId = SelectedInstance.Id;
            InstanceManager.Rename(GameDirectory, oldId, name);
            await LoadAsync();
            SelectedInstance = Instances.FirstOrDefault(version =>
                string.Equals(version.Id, name, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            Status = $"已重命名：{oldId} → {name}";
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "重命名失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteInstanceAsync()
    {
        if (SelectedInstance is null) return;
        var id = SelectedInstance.Id;
        if (!await DialogService.ConfirmAsync(
                "删除实例",
                $"确定删除实例 {id}？" + Environment.NewLine +
                "版本目录会被删除，此操作不可恢复。"))
            return;

        try
        {
            InstanceManager.Delete(GameDirectory, id);
            await LoadAsync();
            Status = $"已删除实例：{id}";
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportModpackAsync()
    {
        if (SelectedInstance is null) return;
        Status = $"正在导出整合包 {SelectedInstance.Id}…";
        try
        {
            var result = await ModpackExporter.ExportAsync(
                GameDirectory, SelectedInstance.Id);
            Status = $"已导出 {result.FileCount} 个文件：{result.ArchivePath}";
        }
        catch (Exception ex)
        {
            Status = "导出整合包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportInstanceAsync()
    {
        if (IsImporting || string.IsNullOrWhiteSpace(ImportPath)) return;

        IsImporting = true;
        Status = "正在导入实例…";
        try
        {
            var result = await InstanceImporter.ImportAsync(
                GameDirectory, ImportPath.Trim());
            await LoadAsync();
            SelectedInstance = Instances.FirstOrDefault(
                version => string.Equals(version.Id, result.Id, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            Status = $"已导入 {result.FileCount} 个文件：{result.VersionDirectory}";
        }
        catch (Exception ex)
        {
            Status = "导入失败：" + ex.Message;
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task ExportInstanceAsync()
    {
        if (SelectedInstance is null) return;
        Status = $"正在导出 {SelectedInstance.Id}…";
        try
        {
            var result = await InstanceExporter.ExportAsync(
                GameDirectory, SelectedInstance.Id,
                new InstanceExportOptions(
                    ExportMods, ExportConfigs, ExportResourcePacks,
                    ExportShaderPacks, ExportSaves, ExportPcl, ExportOptions));
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
