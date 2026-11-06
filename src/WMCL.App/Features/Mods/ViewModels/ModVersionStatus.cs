namespace WMCL.App.Features.Mods;

/// <summary>Mod 版本列表的状态文案（从 ModsViewModel 抽出）。</summary>
public static class ModVersionStatus
{
    public static string Describe(int count, string? filter)
    {
        if (count == 0) return "当前筛选没有版本";
        var suffix = string.IsNullOrWhiteSpace(filter) || filter == "全部" ? "" : $"（筛选：{filter}）";
        return $"共 {count} 个版本{suffix}";
    }
}
