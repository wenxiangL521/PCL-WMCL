namespace WMCL.App.ViewModels.Launching;

/// <summary>启动按钮的文案与副标题（从 LaunchViewModel 抽出）。</summary>
public static class LaunchButtonText
{
    public static string Title(bool isLoading, bool hasInstalled) =>
        isLoading ? "正在加载" : hasInstalled ? "启动游戏" : "下载游戏";

    public static string Subtitle(bool isLoading, bool hasInstalled, string? selectedVersion) =>
        isLoading ? "正在加载版本列表，请稍候"
        : hasInstalled ? (string.IsNullOrWhiteSpace(selectedVersion) ? "未选择版本" : selectedVersion!)
        : "未找到可用的游戏版本";
}
