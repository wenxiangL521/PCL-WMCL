using WMCL.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using Avalonia.Threading;

namespace WMCL.App.Features.Instances;

public partial class InstanceViewModel
{
    [RelayCommand(CanExecute = nameof(CanManageInstance))]
    private Task CloneInstanceAsync() => RelocateInstanceAsync(clone: true);

    [RelayCommand(CanExecute = nameof(CanManageInstance))]
    private Task RenameInstanceAsync() => RelocateInstanceAsync(clone: false);

    private async Task RelocateInstanceAsync(bool clone)
    {
        if (!CanManageInstance()) return;
        var id = SelectedInstance!.Id;
        var root = GameDirectory;
        var action = clone ? "克隆" : "重命名";
        IsManagingInstance = true;
        try
        {
            var name = await _prompt(action + "实例", clone ? "输入新实例名：" : "输入新的实例名：", clone ? id + "-copy" : id);
            if (string.IsNullOrWhiteSpace(name) || !clone && name == id) return;
            using var operation = _services.GameOperations.Acquire(root, action + "实例 " + id);
            if (GameDirectory == root) Status = $"正在{action}实例：{id}…";
            var target = await Task.Run(() =>
            {
                if (clone) return InstanceManager.Clone(root, id, name);
                InstanceManager.Rename(root, id, name);
                return name;
            });
            if (GameDirectory == root)
            {
                var selected = SelectedInstance?.Id == id ? target : SelectedInstance?.Id;
                await LoadAsync();
                if (GameDirectory == root)
                {
                    SelectedInstance = Instances.FirstOrDefault(version => version.Id == selected) ?? SelectedInstance;
                    Status = $"已{action}：{id} → {target}";
                }
            }
            InstancesChanged?.Invoke(this, new InstanceChangeEventArgs(root, id, target,
                clone ? InstanceChangeKind.Cloned : InstanceChangeKind.Renamed));
        }
        catch (Exception ex)
        {
            if (GameDirectory == root) Status = action + "失败：" + ex.Message;
        }
        finally { IsManagingInstance = false; }
    }

    [RelayCommand(CanExecute = nameof(CanManageInstance))]
    private async Task DeleteInstanceAsync()
    {
        if (!CanManageInstance()) return;
        var id = SelectedInstance!.Id;
        var root = GameDirectory;
        IsManagingInstance = true;
        try
        {
            var useTrash = _services.Settings.EnableTrash;
            if (!await _confirm("删除实例", $"确定删除实例 {id}？" + Environment.NewLine +
                                (useTrash ? "实例将移入回收站，可在回收站恢复。" : "版本目录会被删除，此操作不可恢复。"))) return;
            using var operation = _services.GameOperations.Acquire(root, "删除实例 " + id);
            if (useTrash)
            {
                TrashManager.MoveToTrash(root, id);
            }
            else
            {
                await Task.Run(() => InstanceManager.Delete(root, id));
            }
            if (GameDirectory == root)
            {
                var selected = SelectedInstance?.Id;
                await LoadAsync();
                if (GameDirectory == root)
                {
                    SelectedInstance = Instances.FirstOrDefault(version => version.Id == selected) ?? SelectedInstance;
                    Status = useTrash ? $"已移入回收站：{id}" : $"已删除实例：{id}";
                }
            }
            InstancesChanged?.Invoke(this, new InstanceChangeEventArgs(root, id, null, InstanceChangeKind.Deleted));
        }
        catch (Exception ex)
        {
            if (GameDirectory == root) Status = "删除失败：" + ex.Message;
        }
        finally { IsManagingInstance = false; }
    }

    [RelayCommand]
    private async Task ExportModpackAsync()
    {
        if (SelectedInstance is null)
        {
            Status = "没有选中实例，无法导出整合包。";
            return;
        }
        var id = SelectedInstance.Id;
        var root = GameDirectory;
        var target = CurrentExportTarget;
        Status = $"正在导出整合包 {id}…";
        try
        {
            IReadOnlySet<string>? included = null;
            if (UseExportFileSelection)
            {
                await ExportFilesPreparationTask;
                if (_exportFilesTarget != target) await RefreshExportFilesAsync();
                if (_exportFilesTarget != target || target != CurrentExportTarget)
                    throw new InvalidOperationException("导出文件选择未准备好或实例已切换，请刷新文件清单。");
                var version = await new VersionRepository(root).GetLocalVersionAsync(id);
                var directory = IsolationPolicy.GetGameDirectory(root, id, version, _services.Settings.IsolationMode);
                included = BuildExportWhitelist(directory);
                if (included.Count == 0) throw new InvalidOperationException("没有勾选任何文件，已取消导出。");
            }
            var options = BuildModpackExportOptions(included);
            using var operation = _services.GameOperations.Acquire(root, "导出整合包");
            var format = ExportFormat == "mcbpack (Mcbbs)"
                ? ModpackExporter.ExportFormat.Mcbbs
                : ModpackExporter.ExportFormat.Modrinth;
            var output = string.IsNullOrWhiteSpace(ExportPath) ? null : ExportPath.Trim();
            var result = await ModpackExporter.ExportAsync(
                root, id,
                outputPath: output,
                isolationMode: _services.Settings.IsolationMode,
                format: format, options: options);
            if (target == CurrentExportTarget) Status = $"已导出 {result.FileCount} 个文件：{result.ArchivePath}";
        }
        catch (Exception ex)
        {
            if (target == CurrentExportTarget) Status = "导出整合包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportInstanceAsync()
    {
        if (IsImporting || string.IsNullOrWhiteSpace(ImportPath)) return;
        var root = GameDirectory;

        IsImporting = true;
        Status = "正在导入实例…";
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "导入实例");
            var result = await InstanceImporter.ImportAsync(
                root, ImportPath.Trim());
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
            using var operation = _services.GameOperations.Acquire(GameDirectory, "导出实例");
            var result = await InstanceExporter.ExportAsync(
                GameDirectory, SelectedInstance.Id,
                new InstanceExportOptions(
                    ExportMods, ExportConfigs, ExportResourcePacks,
                    ExportShaderPacks, ExportSaves, ExportWmcl, ExportOptions),
                ExportPath.Trim().Length > 0 ? ExportPath.Trim() : null,
                isolationMode: _services.Settings.IsolationMode);
            Status = $"已导出 {result.FileCount} 个文件：{result.ArchivePath}";
        }
        catch (Exception ex)
        {
            Status = "导出失败：" + ex.Message;
        }
    }

    partial void OnSelectedCategoryFilterChanged(string value) => ApplyInstanceFilter();

    private void EnsureCategoryOptions(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || category == "未分类") return;
        if (!InstanceCategoryOptions.Contains(category)) InstanceCategoryOptions.Add(category);
        if (!CategoryOptions.Contains(category)) CategoryOptions.Add(category);
    }

    private void SortInstances()
    {
        var ordered = _allInstances
            .OrderByDescending(item => item.IsFavorite)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _allInstances.Clear();
        _allInstances.AddRange(ordered);
        ApplyInstanceFilter();
    }

    private void ApplyInstanceFilter()
    {
        var selected = SelectedInstance?.Id;
        var filter = SelectedCategoryFilter;
        Instances.Clear();
        foreach (var item in _allInstances)
        {
            var category = string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category;
            if (filter != "全部" && category != filter) continue;
            Instances.Add(item);
        }
        OnPropertyChanged(nameof(HasInstances));
        SelectedInstance = Instances.FirstOrDefault(item => item.Id == selected)
                          ?? Instances.FirstOrDefault();
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
            ExternalLauncher.Open(path);
        }
        catch
        {
            // 打开目录失败不阻塞 UI
        }
    }
}
