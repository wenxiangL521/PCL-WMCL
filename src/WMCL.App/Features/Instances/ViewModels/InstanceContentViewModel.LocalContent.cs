using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Local;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Features.Instances;

public partial class InstanceContentViewModel
{
    private async Task UpdateAsync()
    {
        var request = ++_targetRequest;
        var id = SelectedInstance;
        var root = _services.Settings.GameDirectory;
        if (string.IsNullOrWhiteSpace(id)) return;

        IsLoadingContent = true;
        try
        {
            var version = await new VersionRepository(root).GetVersionAsync(id);
            if (request != _targetRequest || _services.Settings.GameDirectory != root) return;
            GameDirectory = IsolationPolicy.GetGameDirectory(
                root, id, version, _services.Settings.IsolationMode);
            _targetGameRoot = root;
            _gameVersion = MinecraftVersionUtil.GetBaseGameVersion(id, version);
            var settings = InstanceSettingsStore.Load(
                Path.Combine(root, "versions", id));
            PluginTargetDirectory = string.IsNullOrWhiteSpace(settings.PluginDirectory)
                ? Path.Combine(GameDirectory, "plugins")
                : Path.IsPathRooted(settings.PluginDirectory)
                    ? settings.PluginDirectory
                    : Path.Combine(GameDirectory, settings.PluginDirectory);
            await RefreshAsync();
            _resourceVersionLoad = LoadResourceVersionsAsync(SelectedDownloadProject);
        }
        catch (Exception ex)
        {
            if (request == _targetRequest && _services.Settings.GameDirectory == root)
                Status = "读取实例失败：" + ex.Message;
        }
        finally
        {
            IsLoadingContent = false;
            if (request == _targetRequest && _services.Settings.GameDirectory == root) UpdateDownloadCommands();
        }
    }

    private int _localRefreshRequest;
    private IReadOnlyList<InstanceContentItem> _allResourcePacks = [];
    private IReadOnlyList<InstanceContentItem> _allShaderPacks = [];
    private IReadOnlyList<InstanceContentItem> _allWorlds = [];

    [ObservableProperty]
    private string _localSearchText = "";

    partial void OnLocalSearchTextChanged(string value)
    {
        var resourcePack = SelectedResourcePack?.Name;
        var shaderPack = SelectedShaderPack?.Name;
        var world = SelectedWorld?.Name;
        ApplyLocalSearch();
        SelectedResourcePack = ResourcePacks.FirstOrDefault(item => item.Name == resourcePack)
                               ?? ResourcePacks.FirstOrDefault();
        SelectedShaderPack = ShaderPacks.FirstOrDefault(item => item.Name == shaderPack)
                             ?? ShaderPacks.FirstOrDefault();
        SelectedWorld = Worlds.FirstOrDefault(item => item.Name == world)
                        ?? Worlds.FirstOrDefault();
        OnPropertyChanged(nameof(LocalResourceSummary));
    }

    public string LocalResourceSummary =>
        $"{_allResourcePacks.Count} 个资源包 · {_allShaderPacks.Count} 个光影 · {_allWorlds.Count} 个存档 · {WorldBackups.Count} 个备份";

    private void ApplyLocalSearch()
    {
        var filter = LocalSearchText.Trim();
        bool Match(InstanceContentItem item) =>
            filter.Length == 0 || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        ApplyItems(ResourcePacks, _allResourcePacks.Where(Match).ToArray());
        ApplyItems(ShaderPacks, _allShaderPacks.Where(Match).ToArray());
        ApplyItems(Worlds, _allWorlds.Where(Match).ToArray());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var request = ++_localRefreshRequest;
        var directory = GameDirectory;
        var prevResourcePack = SelectedResourcePack?.Name;
        var prevShaderPack = SelectedShaderPack?.Name;
        var prevWorld = SelectedWorld?.Name;
        var prevDataPackWorld = SelectedDataPackWorld;
        var prevBackup = SelectedWorldBackup?.Path;
        var backupDirectory = Path.Combine(WmclPaths.DataDirectory, "backups", "worlds");

        var packs = await Task.Run(() => InstanceContentService.List(directory, InstanceContentKind.ResourcePack));
        var shaders = await Task.Run(() => InstanceContentService.List(directory, InstanceContentKind.ShaderPack));
        var worlds = await Task.Run(() => InstanceContentService.List(directory, InstanceContentKind.World));
        var dataWorlds = await Task.Run(() =>
        {
            var saves = Path.Combine(directory, "saves");
            return Directory.Exists(saves)
                ? Directory.EnumerateDirectories(saves)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .Select(Path.GetFileName)
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .ToArray()
                : [];
        });
        var backups = await Task.Run(() =>
        {
            if (!Directory.Exists(backupDirectory)) return [];
            return Directory.EnumerateFiles(backupDirectory, "*.zip")
                .OrderByDescending(File.GetLastWriteTime)
                .Select(path =>
                {
                    var file = new FileInfo(path);
                    return new WorldBackupItem(file.Name, path, file.Length, file.LastWriteTime);
                })
                .ToArray();
        });

        if (request != _localRefreshRequest || directory != GameDirectory) return;

        _allResourcePacks = packs;
        _allShaderPacks = shaders;
        _allWorlds = worlds;
        ApplyLocalSearch();
        SelectedResourcePack = ResourcePacks.FirstOrDefault(item => item.Name == prevResourcePack)
                               ?? ResourcePacks.FirstOrDefault();
        SelectedShaderPack = ShaderPacks.FirstOrDefault(item => item.Name == prevShaderPack)
                             ?? ShaderPacks.FirstOrDefault();
        SelectedWorld = Worlds.FirstOrDefault(item => item.Name == prevWorld)
                        ?? Worlds.FirstOrDefault();

        DataPackWorlds.Clear();
        foreach (var name in dataWorlds) DataPackWorlds.Add(name);
        SelectedDataPackWorld = DataPackWorlds.FirstOrDefault(name => name == prevDataPackWorld)
                                ?? DataPackWorlds.FirstOrDefault();

        WorldBackups.Clear();
        foreach (var backup in backups) WorldBackups.Add(backup);
        SelectedWorldBackup = WorldBackups.FirstOrDefault(item => item.Path == prevBackup)
                              ?? WorldBackups.FirstOrDefault();

        OnPropertyChanged(nameof(LocalResourceSummary));
        Status = LocalResourceSummary;
    }

    private static void ApplyItems(
        ObservableCollection<InstanceContentItem> target,
        IReadOnlyList<InstanceContentItem> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    [RelayCommand]
    private async Task RestoreWorldBackupAsync()
    {
        if (SelectedWorldBackup is null) return;
        var root = TargetGameRoot;
        var directory = GameDirectory;
        var selected = SelectedWorldBackup;
        if (!await DialogService.ConfirmAsync(
                "恢复存档备份",
                $"确定从 {SelectedWorldBackup.Name} 恢复存档？" + Environment.NewLine +
                "会新建一个存档目录，不覆盖现有存档。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "恢复存档备份");
            var baseName = Path.GetFileNameWithoutExtension(selected.Name);
            var stamp = System.Text.RegularExpressions.Regex.Match(
                baseName, @"-\d{8}-\d{6}$");
            if (stamp.Success)
                baseName = baseName[..^stamp.Length];
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "RestoredWorld";

            var saves = Path.Combine(directory, "saves");
            var targetName = baseName;
            var suffix = 2;
            while (Directory.Exists(Path.Combine(saves, targetName)))
                targetName = $"{baseName}-restored{suffix++}";

            var item = InstanceContentService.Import(
                directory, InstanceContentKind.World,
                selected.Path, targetName);
            _ = RefreshAsync();
            Status = "已恢复存档：" + item.Name;
        }
        catch (Exception ex)
        {
            Status = "恢复存档失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenWorldBackupFolder()
    {
        var path = Path.Combine(WmclPaths.DataDirectory, "backups", "worlds");
        Directory.CreateDirectory(path);
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // 打开失败不阻塞页面
        }
    }

    [RelayCommand]
    private void ToggleResourcePack()
    {
        if (SelectedResourcePack is null) return;
        var root = TargetGameRoot;
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "切换资源包状态");
            var enabled = !SelectedResourcePack.IsEnabled;
            InstanceContentService.SetResourcePackEnabled(root, SelectedResourcePack, enabled);
            _ = RefreshAsync();
            Status = $"已{(enabled ? "启用" : "禁用")}资源包 {SelectedResourcePack.Name}";
        }
        catch (Exception ex)
        {
            Status = "切换资源包状态失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteResourcePackAsync()
    {
        if (SelectedResourcePack is null) return;
        var root = TargetGameRoot;
        var selected = SelectedResourcePack;
        if (!await DialogService.ConfirmAsync(
                "删除资源包",
                $"确定删除 {SelectedResourcePack.Name}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除资源包");
            var name = selected.Name;
            InstanceContentService.Delete(selected);
            _ = RefreshAsync();
            Status = $"已删除资源包 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除资源包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteShaderPackAsync()
    {
        if (SelectedShaderPack is null) return;
        var root = TargetGameRoot;
        var selected = SelectedShaderPack;
        if (!await DialogService.ConfirmAsync(
                "删除光影",
                $"确定删除 {SelectedShaderPack.Name}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除光影");
            var name = selected.Name;
            InstanceContentService.Delete(selected);
            _ = RefreshAsync();
            Status = $"已删除光影 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除光影失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteWorldAsync()
    {
        if (SelectedWorld is null) return;
        var root = TargetGameRoot;
        var selected = SelectedWorld;
        if (!await DialogService.ConfirmAsync(
                "删除存档",
                $"确定删除存档 {SelectedWorld.Name}？" + Environment.NewLine +
                "此操作不可恢复，建议先备份。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除存档");
            var name = selected.Name;
            InstanceContentService.Delete(selected);
            _ = RefreshAsync();
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
            using var operation = _services.GameOperations.Acquire(TargetGameRoot, "备份存档");
            var destination = Path.Combine(
                WmclPaths.DataDirectory, "backups", "worlds");
            var file = InstanceContentService.BackupWorld(SelectedWorld, destination);
            _ = RefreshAsync();
            Status = $"存档已备份：{file}";
        }
        catch (Exception ex)
        {
            Status = "备份存档失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void ImportContent()
    {
        if (string.IsNullOrWhiteSpace(ImportPath) || string.IsNullOrWhiteSpace(GameDirectory)) return;
        var kind = SelectedImportType switch
        {
            "光影" => InstanceContentKind.ShaderPack,
            "存档" => InstanceContentKind.World,
            _ => InstanceContentKind.ResourcePack
        };

        try
        {
            using var operation = _services.GameOperations.Acquire(TargetGameRoot, "导入资源");
            var item = InstanceContentService.Import(GameDirectory, kind, ImportPath.Trim());
            _ = RefreshAsync();
            Status = $"已导入{SelectedImportType}：{item.Name}";
        }
        catch (Exception ex)
        {
            Status = "导入失败：" + ex.Message;
        }
    }

    public void SetResourcePacksEnabled(IReadOnlyList<InstanceContentItem> items, bool enabled)
    {
        if (items.Count == 0) return;
        var root = TargetGameRoot;
        try
        {
            var selected = ValidateContentSelection(InstanceContentKind.ResourcePack, items, GameDirectory);
            using var operation = _services.GameOperations.Acquire(root, "批量切换资源包");
            foreach (var item in selected) InstanceContentService.SetResourcePackEnabled(GameDirectory, item, enabled);
            _ = RefreshAsync();
            Status = $"已{(enabled ? "启用" : "禁用")} {items.Count} 个资源包";
        }
        catch (Exception ex) { Status = "批量切换资源包失败：" + ex.Message; }
    }

    public async Task DeleteInstanceContentAsync(InstanceContentKind kind, IReadOnlyList<InstanceContentItem> items)
    {
        if (items.Count == 0) return;
        var label = kind switch
        {
            InstanceContentKind.ResourcePack => "资源包",
            InstanceContentKind.ShaderPack => "光影",
            _ => "存档"
        };
        var root = TargetGameRoot;
        var directory = GameDirectory;
        var instance = SelectedInstance;
        try
        {
            var selected = ValidateContentSelection(kind, items, directory);
            if (!await _confirmContentDelete($"删除{label}", $"确定删除选中的 {selected.Length} 个{label}？" + Environment.NewLine + "此操作不可恢复。")) return;
            if (root != TargetGameRoot || directory != GameDirectory || instance != SelectedInstance)
            { Status = "实例已切换，未执行原选择的删除。"; return; }
            selected = ValidateContentSelection(kind, selected, directory);
            using var operation = _services.GameOperations.Acquire(root, $"删除{label}");
            foreach (var item in selected) InstanceContentService.Delete(item);
            _ = RefreshAsync();
            Status = $"已删除 {selected.Length} 个{label}";
        }
        catch (Exception ex) { Status = $"删除{label}失败：" + ex.Message; }
    }

    private InstanceContentItem[] ValidateContentSelection(InstanceContentKind kind, IReadOnlyList<InstanceContentItem> items, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("尚未准备好实例目录。");
        var expected = Path.GetFullPath(Path.Combine(directory, InstanceContentService.FolderName(kind)));
        var current = kind switch { InstanceContentKind.ResourcePack => ResourcePacks, InstanceContentKind.ShaderPack => ShaderPacks, _ => Worlds };
        var selected = items.DistinctBy(item => item.FullPath, StringComparer.Ordinal).ToArray();
        foreach (var item in selected)
            if (item.Kind != kind || !current.Any(existing => existing.FullPath == item.FullPath) ||
                Path.GetDirectoryName(Path.GetFullPath(item.FullPath)) != expected || !Path.Exists(item.FullPath) ||
                (File.GetAttributes(item.FullPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("选择已失效或不属于当前实例，请刷新后重新选择。");
        return selected;
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
            ExternalLauncher.Open(path);
        }
        catch
        {
            // ignore
        }
    }
}
