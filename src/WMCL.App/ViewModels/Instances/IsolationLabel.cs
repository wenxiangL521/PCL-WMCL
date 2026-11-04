using WMCL.Minecraft;

namespace WMCL.App.ViewModels.Instances;

/// <summary>隔离模式在设置值与界面文案之间的双向映射（从 InstanceViewModel 抽出）。</summary>
public static class IsolationLabel
{
    public const string Isolated = "隔离";
    public const string Global = "不隔离";
    public const string FollowGlobal = "跟随全局";

    public static string ToLabel(string? mode) => mode switch
    {
        IsolationPolicy.Isolated => Isolated,
        IsolationPolicy.Global => Global,
        _ => FollowGlobal
    };

    public static string ToMode(string? label) => label switch
    {
        Isolated => IsolationPolicy.Isolated,
        Global => IsolationPolicy.Global,
        _ => ""
    };
}
