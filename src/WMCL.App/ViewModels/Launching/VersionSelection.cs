namespace WMCL.App.ViewModels.Launching;

/// <summary>版本列表的可见性与选中回退规则（从 LaunchViewModel 抽出）。</summary>
public static class VersionSelection
{
    /// <summary>错误版本与隐藏版本不参与默认选中。</summary>
    public static bool IsSelectable(VersionItem item) =>
        item.Category != "error" && !item.IsHidden;

    /// <summary>优先保留原选中项；否则选第一个可安装/可见版本。</summary>
    public static VersionItem? Pick(IReadOnlyList<VersionItem> installed, string? previousId)
    {
        if (!string.IsNullOrWhiteSpace(previousId))
        {
            var kept = installed.FirstOrDefault(version =>
                version.Id == previousId && version.Category != "error");
            if (kept is not null) return kept;
        }
        return installed.FirstOrDefault(IsSelectable);
    }

    public static IReadOnlyList<VersionItem> Installed(IEnumerable<VersionItem> all) =>
        all.Where(item => item.IsInstalled).ToArray();
}
