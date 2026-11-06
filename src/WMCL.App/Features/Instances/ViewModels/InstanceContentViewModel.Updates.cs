using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Modding.Resources;

namespace WMCL.App.Features.Instances;

public partial class InstanceContentViewModel
{
    public event Func<ResourceUpdateReviewViewModel, Task>? UpdateReviewRequested;
    [ObservableProperty]
    private bool _isCheckingContentUpdates;

    [RelayCommand]
    private async Task CheckContentUpdatesAsync(CancellationToken ct)
    {
        if (IsCheckingContentUpdates || string.IsNullOrWhiteSpace(GameDirectory)) return;
        var directory = GameDirectory;
        var instance = SelectedInstance;
        var root = TargetGameRoot;
        var request = _targetRequest;
        var label = ContentTypeLabel;
        var type = ShowWorlds ? "world" : ShowShaders ? "shader" : "resourcepack";
        var game = _gameVersion;
        bool Current() => request == _targetRequest && directory == GameDirectory && root == TargetGameRoot &&
                          instance == SelectedInstance && label == ContentTypeLabel;
        IsCheckingContentUpdates = true;
        try
        {
            ResourceUpdateReviewViewModel review;
            if (type == "world")
            {
                review = new ResourceUpdateReviewViewModel(label, [], Current)
                { Status = "存档含玩家游玩数据，无法安全按发布版本原地替换。请在下方完整下载页选择新版并作为新存档安装，原存档不会覆盖。" };
            }
            else
            {
                var archives = Path.Combine(directory, type == "shader" ? "shaderpacks" : "resourcepacks");
                var service = new ResourceArchiveUpdateService(_services.Modrinth);
                Status = $"正在检查{label}更新…";
                var result = await service.CheckAsync(archives, game, type, ct);
                if (!Current()) return;
                var rows = result.Updates.Select(update => new ResourceUpdateReviewItem(Path.GetFileName(update.FilePath),
                    update.CurrentVersion, update.Latest.VersionNumber, $"Modrinth · Minecraft {game}", async token =>
                    {
                        if (!Current()) throw new InvalidOperationException("目标已改变，请重新检查更新。");
                        using var operation = _services.GameOperations.Acquire(root, $"更新{label}");
                        var applied = await service.ApplyAsync(update, archives, game, type, token);
                        if (Current())
                        {
                            Refresh();
                            Status = applied.RecoveryDirectory is null ? $"{label}已更新。" : $"{label}已更新，原文件备份保留：{applied.RecoveryDirectory}";
                        }
                    })).ToArray();
                var summary = result.Items.Count == 0 ? "没有可检查的归档文件（解压目录不自动替换）。"
                    : result.Summary;
                review = new ResourceUpdateReviewViewModel(label, rows, Current)
                { Status = summary + "。仅列出已识别的当前资源类型；来源未知项不会替换。" };
                Status = summary;
            }
            if (!Current()) return;
            if (UpdateReviewRequested is { } show) await show(review);
            else await DialogService.ShowResourceUpdatesAsync(review);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { if (Current()) Status = "已取消检查更新。"; }
        catch (Exception exception) { if (Current()) Status = "检查更新失败：" + exception.Message; }
        finally { IsCheckingContentUpdates = false; }
    }
}
