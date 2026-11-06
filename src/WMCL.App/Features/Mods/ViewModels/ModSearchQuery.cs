namespace WMCL.App.Features.Mods;

/// <summary>社区资源搜索条件归一化（从 ModsViewModel 抽出，便于单测与复用）。</summary>
public readonly record struct ModSearchQuery(string Text, string GameVersion, string Loader, string Source)
{
    public static ModSearchQuery Create(string? text, string? gameVersion, string? loader, string? source) =>
        new(
            (text ?? "").Trim(),
            (gameVersion ?? "").Trim(),
            string.Equals(loader, "全部", StringComparison.Ordinal) ? "" : (loader ?? "").Trim(),
            string.IsNullOrWhiteSpace(source) ? "Modrinth" : source.Trim());

    public bool IsCurseForge => Source == "CurseForge";

    /// <summary>搜索中/结果/空结果的统一状态文案。</summary>
    public string DescribeSearching() => $"正在搜索 {Source}…";

    public string DescribeResult(int count, bool hasMore) =>
        count == 0
            ? $"没有找到匹配的资源（{Source}）"
            : hasMore ? $"{Source}：已显示 {count} 个，可继续加载" : $"{Source}：共 {count} 个";
}
