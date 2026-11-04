namespace WMCL.App.ViewModels.Content;

/// <summary>资源版本筛选后的选中回退与状态文案（从 InstanceContentViewModel 抽出）。</summary>
public static class ContentVersionPicker
{
    /// <summary>优先保留原选中（按引用），否则选第一条。</summary>
    public static T? Pick<T>(IReadOnlyList<T> visible, T? previous) where T : class =>
        previous is not null && visible.Contains(previous) ? previous : visible.FirstOrDefault();

    public static string Describe(int count, string? filter)
    {
        if (count == 0) return "当前筛选没有版本";
        var suffix = string.IsNullOrWhiteSpace(filter) || filter == "全部" ? "" : $"（筛选：{filter}）";
        return $"共 {count} 个版本{suffix}";
    }
}
