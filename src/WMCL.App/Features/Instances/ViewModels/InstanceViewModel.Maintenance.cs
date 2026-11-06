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
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RepairManifestAsync(CancellationToken ct)
    {
        if (SelectedInstance is null || !CanRepairManifest) return;
        var id = SelectedInstance.Id;
        var root = GameDirectory;
        var installer = new VersionInstaller(new VersionRepository(root));
        var mirror = _services.Mirror;
        void Report(string message)
        {
            if (SelectedInstance?.Id == id && GameDirectory == root) ManifestStatus = message;
        }
        IsRepairingManifest = true;
        Report("正在修复受管文件…");
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "修复实例 " + id);
            var managedProgress = new Progress<DownloadProgress>(update =>
            {
                if (IsRepairingManifest) Report($"修复受管文件 {(update.Percent ?? 0) * 100:0}% {update.CurrentFile}");
            });
            await ManagedFileRepair.RepairAsync(root, id, managedProgress, ct);
            Report("正在修复基础文件…");
            var progress = new Progress<JobProgress>(update =>
            {
                if (IsRepairingManifest) Report($"{update.Stage} {(update.Percent ?? 0) * 100:0}%");
            });
            await installer.InstallAsync(
                id,
                new InstallOptions
                {
                    Mirror = mirror,
                    MaxConcurrency = Math.Clamp(_services.Settings.DownloadThreads, 1, 64),
                    VerifyExisting = true
                },
                progress, ct);
            try
            {
                var overrideCount = await OverridesRepair.RepairAsync(
                    root, id, ct);
                if (overrideCount > 0)
                    Report($"已重新解压 {overrideCount} 个 overrides 文件");
            }
            catch (FileNotFoundException)
            {
                Report("原始整合包已不存在，跳过 overrides 修复");
            }
            catch (InvalidOperationException)
            {
                // 非整合包/旧清单没有 overrides 信息
            }
            var result = await InstallManifestStore.VerifyAsync(root, id, ct);
            Report(result.IsHealthy
                ? $"修复完成，清单正常：{result.Checked} 个受管文件"
                  + (result.PreservedSaves > 0 ? $"，保留 {result.PreservedSaves} 个已有存档文件" : "")
                : $"修复完成，但仍有异常：缺失 {result.Missing}，损坏 {result.Corrupted}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Report("已取消文件修复");
        }
        catch (Exception ex)
        {
            Report("修复失败：" + ex.Message);
        }
        finally
        {
            IsRepairingManifest = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanUpdateModpackContent))]
    private async Task UpdateModpackContentAsync(CancellationToken ct)
    {
        if (SelectedInstance is null || !CanUpdateModpackContent()) return;
        var id = SelectedInstance.Id;
        var root = GameDirectory;
        IsUpdatingModpack = true;
        ManifestStatus = "正在检查整合包内容更新…";
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "更新整合包 " + id);
            var progress = new Progress<JobProgress>(update =>
            {
                if (IsUpdatingModpack && SelectedInstance?.Id == id && GameDirectory == root)
                    ManifestStatus = $"{update.Stage} {(update.Percent ?? 0) * 100:0}% {update.Detail}";
            });
            var updater = _modpackUpdater ?? _services.CreateModpackUpdater(root);
            var result = await updater.UpdateAsync(id, progress, ct);
            if (SelectedInstance?.Id == id && GameDirectory == root)
                ManifestStatus = $"整合包内容更新完成：下载 {result.Downloaded}，删除 {result.Deleted}，保留用户修改 {result.Preserved.Count}" +
                                 (string.IsNullOrWhiteSpace(result.Version) ? "" : $"，版本 {result.Version}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (SelectedInstance?.Id == id && GameDirectory == root) ManifestStatus = "整合包更新已取消";
        }
        catch (Exception ex)
        {
            if (SelectedInstance?.Id == id && GameDirectory == root) ManifestStatus = "整合包更新失败：" + ex.Message;
        }
        finally { IsUpdatingModpack = false; }
    }

    [RelayCommand]
    private async Task VerifyManifestAsync()
    {
        if (SelectedInstance is null) return;
        var id = SelectedInstance.Id;
        var root = GameDirectory;
        ManifestStatus = "正在校验安装清单…";
        try
        {
            var result = await InstallManifestStore.VerifyAsync(root, id);
            if (SelectedInstance?.Id != id || GameDirectory != root) return;
            ManifestStatus = result.Checked == 0 && result.Problems.Count > 0
                ? result.Problems[0]
                : result.IsHealthy
                    ? $"清单正常：{result.Checked} 个受管文件"
                      + (result.PreservedSaves > 0 ? $"，已有存档文件 {result.PreservedSaves} 个（仅检查是否存在）" : "")
                    : $"清单异常：缺失 {result.Missing}，损坏 {result.Corrupted}\n"
                      + string.Join(Environment.NewLine, result.Problems.Take(5));
        }
        catch (Exception ex)
        {
            if (SelectedInstance?.Id == id && GameDirectory == root) ManifestStatus = "校验失败：" + ex.Message;
        }
    }
}
