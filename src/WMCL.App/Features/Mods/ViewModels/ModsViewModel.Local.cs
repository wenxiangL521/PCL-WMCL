using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Local;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modrinth;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.Features.Mods;

public partial class ModsViewModel
{
    /// <summary>扫描 Mod 目录会逐个打开 jar，放到后台线程，避免切页时卡住界面。</summary>
    private async Task RefreshLocalModsAsync()
    {
        var directory = ModsDirectory;
        var mods = await Task.Run(() => LocalModService.List(directory)).ConfigureAwait(true);
        if (!string.Equals(directory, ModsDirectory, StringComparison.Ordinal)) return;
        ApplyLocalMods(mods);
        _loadedRoot = _services.Settings.GameDirectory;
        _loadedInstance = SelectedModInstance;
    }

    [RelayCommand]
    private void RefreshLocalMods()
    {
        ApplyLocalMods(LocalModService.List(ModsDirectory));
    }

    private void ApplyLocalMods(IReadOnlyList<LocalMod> mods)
    {
        // 整体替换，避免几百次集合变更通知
        LocalMods.Clear();
        LocalModsController.Apply(mods);
        ApplyLocalModFilter();
        SelectedLocalMod = LocalModsView.FirstOrDefault();
        var enabled = LocalMods.Count(m => m.IsEnabled);
        LocalModStatus = LocalMods.Count == 0
            ? "该实例 mods 目录为空"
            : LocalModsView.Count == LocalMods.Count
                ? $"{LocalMods.Count} 个 Mod，已启用 {enabled} 个"
                : $"显示 {LocalModsView.Count}/{LocalMods.Count} 个 Mod，已启用 {enabled} 个";
    }

    public async Task SelectModInstanceAsync(string instance)
    {
        if (!ModInstances.Contains(instance)) return;
        SelectedModInstance = instance;
        await UpdateModTargetAsync();
    }

    private readonly Func<string, string, Task<bool>> _confirmLocalDelete;

    /// <summary>Batch enable/disable and delete for multi-selection in Mod management.</summary>
    public void SetLocalModsEnabled(IReadOnlyList<LocalMod> mods, bool enabled)
    {
        if (mods.Count == 0) return;
        var root = ModTargetRoot;
        var directory = ModsDirectory;
        var instance = SelectedModInstance;
        try
        {
            var selected = ValidateLocalSelection(mods, directory);
            using var operation = _services.GameOperations.Acquire(root, "批量切换 Mod 状态");
            foreach (var mod in selected) LocalModService.SetEnabled(directory, mod, enabled);
            if (ModTargetRoot != root || ModsDirectory != directory || SelectedModInstance != instance) return;
            RefreshLocalMods();
            LocalModStatus = $"已{(enabled ? "启用" : "禁用")} {selected.Length} 个 Mod";
        }
        catch (Exception ex) { LocalModStatus = "批量操作失败：" + ex.Message; }
    }

    public async Task DeleteLocalModsAsync(IReadOnlyList<LocalMod> mods)
    {
        if (mods.Count == 0) return;
        var root = ModTargetRoot;
        var directory = ModsDirectory;
        var instance = SelectedModInstance;
        try
        {
            var selected = ValidateLocalSelection(mods, directory);
            if (!await _confirmLocalDelete("删除 Mod", $"确定删除选中的 {selected.Length} 个 Mod？" + Environment.NewLine + "此操作不可恢复。")) return;
            if (ModTargetRoot != root || ModsDirectory != directory || SelectedModInstance != instance)
            {
                LocalModStatus = "实例已切换，未执行原选择的删除。";
                return;
            }
            selected = ValidateLocalSelection(selected, directory);
            using var operation = _services.GameOperations.Acquire(root, "批量删除 Mod");
            foreach (var mod in selected) LocalModService.Delete(mod);
            RefreshLocalMods();
            LocalModStatus = $"已删除 {selected.Length} 个 Mod";
        }
        catch (Exception ex) { LocalModStatus = "删除失败：" + ex.Message; }
    }

    private LocalMod[] ValidateLocalSelection(IReadOnlyList<LocalMod> mods, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("尚未准备好实例 Mod 目录。");
        var fullDirectory = Path.GetFullPath(directory);
        var selected = mods.DistinctBy(mod => mod.FilePath, StringComparer.Ordinal).ToArray();
        foreach (var mod in selected)
        {
            var path = Path.GetFullPath(mod.FilePath);
            if (!LocalMods.Any(current => current.FilePath == mod.FilePath) ||
                Path.GetDirectoryName(path) != fullDirectory || !File.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("选择已经失效，或文件不属于当前实例，请刷新后重新选择。");
        }
        return selected;
    }

    public event Func<ResourceUpdateReviewViewModel, Task>? UpdateReviewRequested;

    [RelayCommand]
    private async Task CheckModUpdatesAsync()
    {
        if (IsCheckingUpdates) return;
        var target = CaptureModUpdateTarget();
        if (target is null)
        {
            UpdateStatus = "请选择带 Fabric/Forge 的实例";
            return;
        }

        InvalidateModUpdates();
        using var cancellation = new CancellationTokenSource();
        _modUpdateCheckCancellation = cancellation;
        IsCheckingUpdates = true;
        UpdateStatus = "正在检查 Mod 更新…";
        try
        {
            var check = await _services.ModUpdates.CheckDetailedAsync(
                target.Directory, target.Minecraft, target.Loader, ct: cancellation.Token);
            var updates = check.Updates;
            if (!IsCurrentModUpdateTarget(target) || cancellation.IsCancellationRequested) return;
            ValidateModUpdatePaths(target, updates);
            _modUpdatesTarget = target;
            _modUpdates = updates.ToDictionary(
                update => update.FilePath, StringComparer.OrdinalIgnoreCase);

            var selectedPath = SelectedLocalMod?.FilePath;
            for (var index = 0; index < LocalMods.Count; index++)
            {
                var mod = LocalMods[index];
                var update = _modUpdates.GetValueOrDefault(mod.FilePath);
                LocalMods[index] = mod with { UpdateVersion = update?.Latest.VersionNumber };
            }
            ApplyLocalModFilter();

            SelectedLocalMod = LocalMods.FirstOrDefault(mod =>
                                  string.Equals(mod.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                              ?? LocalMods.FirstOrDefault();
            UpdateStatus = check.Summary;
            UpdateModUpdateCommands();
            if (IsInstanceManagement)
            {
                var rows = updates.Select(update => new ResourceUpdateReviewItem(
                    LocalMods.FirstOrDefault(mod => mod.FilePath == update.FilePath)?.DisplayName ?? Path.GetFileName(update.FilePath),
                    update.CurrentVersion, update.Latest.VersionNumber, "Modrinth · " + target.Minecraft + " · " + target.Loader,
                    async ct =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!IsCurrentModUpdateTarget(target)) throw new InvalidOperationException("实例已切换，请重新检查更新。");
                        ModUpdateProgress = 0;
                        ModUpdateTaskStatus = "尚未提交更新，请先完成风险确认。";
                        await ApplyModUpdatesAsync(target, [update], ct);
                        ct.ThrowIfCancellationRequested();
                        if (ModUpdateProgress < 100 || ModUpdateTaskStatus.StartsWith("更新失败", StringComparison.Ordinal))
                            throw new InvalidOperationException(ModUpdateTaskStatus.Length == 0 ? "更新未提交。" : ModUpdateTaskStatus);
                    })).ToArray();
                var review = new ResourceUpdateReviewViewModel("Mod", rows, () =>
                    ModsDirectory == target.Directory && ModTargetRoot == target.Root && SelectedModInstance == target.Instance)
                { Status = check.Summary + "。仅列出当前实例的 Mod；关闭窗口不会更新。" };
                IsCheckingUpdates = false;
                if (UpdateReviewRequested is { } show) await show(review);
                else await DialogService.ShowResourceUpdatesAsync(review);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentModUpdateTarget(target)) UpdateStatus = "检查更新失败：" + ex.Message;
        }
        finally
        {
            _modUpdateCheckCancellation = null;
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllModsAsync()
    {
        if (IsCheckingUpdates || _modUpdates.Count == 0 ||
            _modUpdatesTarget is not { } target || !IsCurrentModUpdateTarget(target)) return;
        await ApplyModUpdatesAsync(target, _modUpdates.Values.ToArray());
    }

    [RelayCommand]
    private async Task UpdateSelectedModAsync()
    {
        if (IsCheckingUpdates || _modUpdatesTarget is not { } target || !IsCurrentModUpdateTarget(target) ||
            SelectedLocalMod is null ||
            !_modUpdates.TryGetValue(SelectedLocalMod.FilePath, out var update))
            return;

        await ApplyModUpdatesAsync(target, [update]);
    }

    [RelayCommand]
    private void ToggleLocalMod()
    {
        if (SelectedLocalMod is null) return;
        try
        {
            using var operation = _services.GameOperations.Acquire(ModTargetRoot, "切换 Mod 状态");
            var target = !SelectedLocalMod.IsEnabled;
            LocalModService.SetEnabled(ModsDirectory, SelectedLocalMod, target);
            LocalModStatus = $"{SelectedLocalMod.DisplayName} 已{(target ? "启用" : "禁用")}";
            RefreshLocalMods();
        }
        catch (Exception ex)
        {
            LocalModStatus = "操作失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteLocalModAsync()
    {
        if (SelectedLocalMod is null) return;
        var root = ModTargetRoot;
        var selected = SelectedLocalMod;
        if (!await DialogService.ConfirmAsync(
                "删除 Mod",
                $"确定删除 {SelectedLocalMod.DisplayName}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除 Mod");
            var name = selected.DisplayName;
            LocalModService.Delete(selected);
            LocalModStatus = $"已删除 {name}";
            RefreshLocalMods();
        }
        catch (Exception ex)
        {
            LocalModStatus = "删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        var directory = string.IsNullOrWhiteSpace(ModsDirectory)
            ? Path.Combine(_services.Settings.GameDirectory, "mods")
            : ModsDirectory;
        Directory.CreateDirectory(directory);
        OpenPath(directory);
    }
}
