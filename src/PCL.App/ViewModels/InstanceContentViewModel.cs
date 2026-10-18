using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Minecraft;
using PCL.Modding.Local;

namespace PCL.App.ViewModels;

public partial class InstanceContentViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public InstanceContentViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<string> Instances { get; } = [];
    public ObservableCollection<InstanceContentItem> ResourcePacks { get; } = [];
    public ObservableCollection<InstanceContentItem> ShaderPacks { get; } = [];
    public ObservableCollection<InstanceContentItem> Worlds { get; } = [];

    [ObservableProperty]
    private string? _selectedInstance;

    [ObservableProperty]
    private InstanceContentItem? _selectedResourcePack;

    [ObservableProperty]
    private InstanceContentItem? _selectedShaderPack;

    [ObservableProperty]
    private InstanceContentItem? _selectedWorld;

    [ObservableProperty]
    private bool _hasResourcePack;

    [ObservableProperty]
    private bool _hasShaderPack;

    [ObservableProperty]
    private bool _hasWorld;

    [ObservableProperty]
    private string _gameDirectory = "";

    [ObservableProperty]
    private string _status = "选择实例以查看资源包、光影和存档";

    public async Task LoadAsync()
    {
        var installed = await Task.Run(() => _services.Repository.ListInstalled());
        Instances.Clear();
        foreach (var version in installed) Instances.Add(version.Id);

        if (SelectedInstance is null || !Instances.Contains(SelectedInstance))
            SelectedInstance = Instances.FirstOrDefault(id =>
                                 id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
                             ?? Instances.FirstOrDefault();

        if (SelectedInstance is null)
            Status = "还没有已安装实例";
        else
            await UpdateAsync();
    }

    partial void OnSelectedInstanceChanged(string? value) => _ = UpdateAsync();

    partial void OnSelectedResourcePackChanged(InstanceContentItem? value) =>
        HasResourcePack = value is not null;

    partial void OnSelectedShaderPackChanged(InstanceContentItem? value) =>
        HasShaderPack = value is not null;

    partial void OnSelectedWorldChanged(InstanceContentItem? value) =>
        HasWorld = value is not null;

    private async Task UpdateAsync()
    {
        var id = SelectedInstance;
        if (string.IsNullOrWhiteSpace(id)) return;

        try
        {
            var version = await _services.Repository.GetVersionAsync(id);
            GameDirectory = MinecraftVersionUtil.GetGameDirectory(
                _services.Settings.GameDirectory, id, version);
            Refresh();
        }
        catch (Exception ex)
        {
            Status = "读取实例失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        Fill(ResourcePacks, InstanceContentKind.ResourcePack);
        Fill(ShaderPacks, InstanceContentKind.ShaderPack);
        Fill(Worlds, InstanceContentKind.World);

        SelectedResourcePack = ResourcePacks.FirstOrDefault();
        SelectedShaderPack = ShaderPacks.FirstOrDefault();
        SelectedWorld = Worlds.FirstOrDefault();

        Status = $"{ResourcePacks.Count} 个资源包 · {ShaderPacks.Count} 个光影 · {Worlds.Count} 个存档";
    }

    [RelayCommand]
    private void DeleteResourcePack()
    {
        if (SelectedResourcePack is null) return;
        try
        {
            var name = SelectedResourcePack.Name;
            InstanceContentService.Delete(SelectedResourcePack);
            Refresh();
            Status = $"已删除资源包 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除资源包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void DeleteShaderPack()
    {
        if (SelectedShaderPack is null) return;
        try
        {
            var name = SelectedShaderPack.Name;
            InstanceContentService.Delete(SelectedShaderPack);
            Refresh();
            Status = $"已删除光影 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除光影失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void DeleteWorld()
    {
        if (SelectedWorld is null) return;
        try
        {
            var name = SelectedWorld.Name;
            InstanceContentService.Delete(SelectedWorld);
            Refresh();
            Status = $"已删除存档 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除存档失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void BackupWorld()
    {
        if (SelectedWorld is null) return;
        try
        {
            var destination = Path.Combine(
                PclPaths.DataDirectory, "backups", "worlds");
            var file = InstanceContentService.BackupWorld(SelectedWorld, destination);
            Status = $"存档已备份：{file}";
        }
        catch (Exception ex)
        {
            Status = "备份存档失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenResourcePacks() => OpenFolder(InstanceContentKind.ResourcePack);

    [RelayCommand]
    private void OpenShaderPacks() => OpenFolder(InstanceContentKind.ShaderPack);

    [RelayCommand]
    private void OpenWorlds() => OpenFolder(InstanceContentKind.World);

    private void Fill(
        ObservableCollection<InstanceContentItem> target,
        InstanceContentKind kind)
    {
        target.Clear();
        foreach (var item in InstanceContentService.List(GameDirectory, kind))
            target.Add(item);
    }

    private void OpenFolder(InstanceContentKind kind)
    {
        var path = Path.Combine(GameDirectory, InstanceContentService.FolderName(kind));
        Directory.CreateDirectory(path);
        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", path)
                : OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("open", path)
                    : new ProcessStartInfo("xdg-open", path);
            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
        }
        catch
        {
            // ignore
        }
    }
}
