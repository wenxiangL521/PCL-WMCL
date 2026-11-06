namespace WMCL.App.Services;

/// <summary>底部任务栏显示状态。</summary>
public readonly record struct TaskBarSnapshot(bool Active, double Progress, string Text)
{
    public string PercentText => !Active ? "" : Progress > 0 ? $"{Progress:0}%" : "…";
}

/// <summary>
/// 从各页面的忙碌状态推导任务栏显示内容。纯函数，便于单测，
/// 也把这条「谁在忙」的优先级规则从 MainWindowViewModel 里移出来。
/// </summary>
public static class TaskBarState
{
    public static TaskBarSnapshot From(
        bool downloadBusy, bool downloadCanCancel, double downloadProgress,
        bool modsBusy, bool modsCanCancel, double modsProgress,
        bool installingForgelike, double forgelikeProgress,
        bool optiFineBusy, double optiFineProgress,
        bool combinedBusy, double combinedProgress,
        bool installingMod, double modProgress,
        bool favoritesInstalling, double favoritesProgress,
        bool modpackBusy, double modpackProgress,
        bool contentDownloadBusy, double contentProgress,
        bool importingInstance,
        bool launchCheckingModpack, bool launchCheckingFiles, bool launchBusy, double launchProgress)
    {
        if (downloadBusy && downloadCanCancel) return new(true, downloadProgress, "下载/安装 Minecraft");
        if (modsBusy && modsCanCancel) return new(true, modsProgress, "安装 Fabric");
        if (installingForgelike) return new(true, forgelikeProgress, "安装 Forge/NeoForge");
        if (optiFineBusy) return new(true, optiFineProgress, "安装 OptiFine");
        if (combinedBusy) return new(true, combinedProgress, "组合安装");
        if (installingMod) return new(true, modProgress, "安装 Mod 与必需前置");
        if (favoritesInstalling) return new(true, favoritesProgress, "批量安装收藏");
        if (modpackBusy) return new(true, modpackProgress, "安装整合包");
        if (contentDownloadBusy) return new(true, contentProgress, "下载资源");
        if (importingInstance) return new(true, 0, "导入实例");
        if (launchCheckingModpack || launchCheckingFiles || (launchBusy && launchProgress > 0))
        {
            var text = launchCheckingModpack ? "启动前检查整合包"
                : launchCheckingFiles ? "启动前校验游戏文件" : "安装/启动版本";
            return new(true, launchProgress, text);
        }
        return new(false, 0, "");
    }
}
