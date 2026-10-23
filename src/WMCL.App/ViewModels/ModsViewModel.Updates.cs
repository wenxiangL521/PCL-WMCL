using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Core.Jobs;
using WMCL.Modding.Modrinth;

namespace WMCL.App.ViewModels;

public partial class ModsViewModel
{
    private readonly Func<string, string, Task<bool>> _confirmModUpdate;
    private CancellationTokenSource? _modUpdateCancellation;
    public bool IsUpdatingMods => _modUpdateCancellation is not null;
    public event EventHandler<TaskEntry>? ModUpdateTaskFinished;

    [ObservableProperty]
    private string _modUpdateTaskTitle = "Mod 更新";
    [ObservableProperty]
    private string _modUpdateTaskStatus = "";
    [ObservableProperty]
    private string _modUpdateProgressText = "";
    [ObservableProperty]
    private double _modUpdateProgress;

    private async Task ApplyModUpdatesAsync(ModUpdateTarget target, IReadOnlyList<ModUpdateInfo> updates)
    {
        IsCheckingUpdates = true;
        var started = false;
        var outcome = "失败";
        using var cancellation = new CancellationTokenSource();
        try
        {
            if (!_services.Settings.HintUpdateModWarningAcknowledged || updates.Count >= 15)
            {
                var accepted = await _confirmModUpdate("Mod 更新警告",
                    "新版本 Mod 可能不兼容旧存档或者其他 Mod，这可能导致游戏崩溃，甚至永久损坏存档！" + Environment.NewLine +
                    "如果你在游玩整合包，请千万不要自行更新 Mod！" + Environment.NewLine + Environment.NewLine +
                    "在更新前，请先备份存档，并检查 Mod 的更新日志。" + Environment.NewLine +
                    "如果更新后出现问题，你也可以在回收站找回更新前的 Mod。");
                if (!IsCurrentModUpdateTarget(target)) return;
                if (!accepted)
                {
                    UpdateStatus = "已取消 Mod 更新";
                    return;
                }
                var acknowledged = _services.Settings.HintUpdateModWarningAcknowledged;
                _services.Settings.HintUpdateModWarningAcknowledged = true;
                try { _services.SaveSettings(); }
                catch
                {
                    _services.Settings.HintUpdateModWarningAcknowledged = acknowledged;
                    throw;
                }
            }
            if (!IsCurrentModUpdateTarget(target)) return;
            using var operation = _services.GameOperations.Acquire(target.Root, "更新 Mod");
            ValidateModUpdatePaths(target, updates);
            ModUpdateTaskTitle = "Mod 更新：" + target.Instance;
            ModUpdateTaskStatus = $"正在更新 {updates.Count} 个 Mod 及其必需前置…";
            ModUpdateProgressText = "正在检查必需前置及原文件";
            ModUpdateProgress = 0;
            UpdateStatus = ModUpdateTaskStatus;
            _modUpdateCancellation = cancellation;
            started = true;
            OnPropertyChanged(nameof(IsUpdatingMods));
            var progress = new Progress<JobProgress>(value =>
            {
                if (!ReferenceEquals(_modUpdateCancellation, cancellation)) return;
                ModUpdateProgress = value.Stage == "应用资源文件" ? 85 + 15 * (value.Percent ?? 0) : 85 * (value.Percent ?? 0);
                ModUpdateProgressText = FormatProgress(value);
            });
            var result = await _services.ModUpdates.ApplyAsync(updates, target.Directory, target.Minecraft, target.Loader, progress, cancellation.Token);
            ModUpdateProgress = 100;
            ModUpdateTaskStatus = $"已更新 {updates.Count} 个所选 Mod 及其必需前置，旧版 Mod 已移入回收站";
            outcome = "完成";
            if (result.RecoveryDirectory is { } recovery)
            {
                ModUpdateTaskStatus = $"已更新 {updates.Count} 个所选 Mod 及其必需前置；{result.RecoveryWarning}。原始备份保留于：{recovery}";
                outcome = "完成（备份保留）";
            }
            if (IsCurrentModUpdateTarget(target))
            {
                InvalidateModUpdates();
                RefreshLocalMods();
                UpdateStatus = ModUpdateTaskStatus + "，请重新检查更新";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = "已取消";
            ModUpdateTaskStatus = "已取消 Mod 更新";
            if (IsCurrentModUpdateTarget(target)) UpdateStatus = ModUpdateTaskStatus;
        }
        catch (Exception exception)
        {
            ModUpdateTaskStatus = "更新失败：" + exception.Message;
            if (IsCurrentModUpdateTarget(target)) UpdateStatus = ModUpdateTaskStatus;
        }
        finally
        {
            _modUpdateCancellation = null;
            OnPropertyChanged(nameof(IsUpdatingMods));
            IsCheckingUpdates = false;
            UpdateModUpdateCommands();
            if (started)
                ModUpdateTaskFinished?.Invoke(this, new TaskEntry(ModUpdateTaskTitle, ModUpdateTaskStatus, ModUpdateProgress, outcome, null));
        }
    }

    [RelayCommand]
    private void CancelModUpdates() => _modUpdateCancellation?.Cancel();

    private sealed record ModUpdateTarget(string Root, string Instance, string Directory,
        string Minecraft, string Loader, int Request);

    private ModUpdateTarget? _modUpdatesTarget;
    private CancellationTokenSource? _modUpdateCheckCancellation;

    private ModUpdateTarget? CaptureModUpdateTarget() =>
        string.IsNullOrWhiteSpace(SelectedModInstance) || string.IsNullOrWhiteSpace(ModsDirectory) ||
        string.IsNullOrWhiteSpace(_modGameVersion) || _modLoader == "vanilla" ||
        _modTargetRoot != _services.Settings.GameDirectory
            ? null
            : new(ModTargetRoot, SelectedModInstance, ModsDirectory, _modGameVersion, _modLoader, _modTargetRequest);

    private bool IsCurrentModUpdateTarget(ModUpdateTarget target) =>
        target == CaptureModUpdateTarget();

    private void InvalidateModUpdates()
    {
        _modUpdateCheckCancellation?.Cancel();
        _modUpdatesTarget = null;
        _modUpdates.Clear();
        UpdateStatus = "";
        for (var index = 0; index < LocalMods.Count; index++)
            if (LocalMods[index].UpdateVersion is not null)
                LocalMods[index] = LocalMods[index] with { UpdateVersion = null };
        UpdateModUpdateCommands();
    }

    private void UpdateModUpdateCommands()
    {
        var available = !IsCheckingUpdates && _modUpdatesTarget is { } target && IsCurrentModUpdateTarget(target);
        CanUpdateAllMods = available && _modUpdates.Count > 0;
        CanUpdateSelectedMod = available && SelectedLocalMod is { } mod && _modUpdates.ContainsKey(mod.FilePath);
    }

    private static void ValidateModUpdatePaths(ModUpdateTarget target, IEnumerable<ModUpdateInfo> updates)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Directory));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var update in updates)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(update.FilePath));
            if (!string.Equals(directory, parent, comparison))
                throw new InvalidOperationException("Mod 更新文件不属于检查时选定的实例目录，请重新检查更新。");
        }
    }
}
