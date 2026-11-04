namespace WMCL.App.ViewModels.Instances;

/// <summary>版本卡类型（PCL2 的版本分类）在索引与文案间的映射。</summary>
public static class VersionCardType
{
    public static readonly string[] Options =
    [
        "自动", "从版本列表中隐藏", "可安装 Mod 的版本", "常规版本", "不常用版本", "愚人节版本"
    ];

    public const int MaxIndex = 5;

    /// <summary>把可能越界的索引夹到合法范围。</summary>
    public static int Clamp(int index) => Math.Clamp(index, 0, MaxIndex);

    /// <summary>索引 → 文案；越界按「自动」处理。</summary>
    public static string Label(int index) => Options[Clamp(index)];

    /// <summary>文案 → 索引；未知按「自动」处理。</summary>
    public static int IndexOf(string? label) => Math.Max(0, Array.IndexOf(Options, label ?? ""));
}
