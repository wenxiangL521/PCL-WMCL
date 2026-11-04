using WMCL.Modding.Local;

namespace WMCL.App.ViewModels.Mods;

/// <summary>本地 Mod 列表的名称/文件名/ID/详情过滤（从 ModsViewModel 抽出，便于单测）。</summary>
public static class LocalModFilter
{
    public static IReadOnlyList<LocalMod> Apply(IEnumerable<LocalMod> mods, string? filter)
    {
        var text = (filter ?? "").Trim();
        if (text.Length == 0) return mods.ToArray();
        return mods.Where(mod => Matches(mod, text)).ToArray();
    }

    public static bool Matches(LocalMod mod, string filter) =>
        mod.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        mod.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        (mod.ModId?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
        mod.DetailText.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>状态行文案：全部 / 过滤后 / 空目录。</summary>
    public static string Describe(int total, int shown, int enabled) =>
        total == 0 ? "该实例 mods 目录为空"
        : shown == total ? $"{total} 个 Mod，已启用 {enabled} 个"
        : $"显示 {shown}/{total} 个 Mod，已启用 {enabled} 个";
}
