using Avalonia;
using Avalonia.Media;
using WMCL.Core;

namespace WMCL.App.Services;

/// <summary>
/// 界面字体：内置鸿蒙只覆盖拉丁字符，中文回退到系统中文字体（系统字体带 hinting，小字号更清晰）。
/// 对照 PCL-ME 的做法：内嵌 HarmonyOS Sans，后面挂系统中文字体链。
/// </summary>
public static class UiFontService
{
    public const string BundledLatin = "avares://WMCL.App/Assets/Fonts#HarmonyOS Sans";

    public static readonly string[] ChineseFallbacks =
    [
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Source Han Sans CN",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "PingFang SC",
        "Hiragino Sans GB",
        "WenQuanYi Micro Hei",
        "sans-serif"
    ];

    public const string DefaultLabel = "默认（鸿蒙 + 系统中文）";

    /// <summary>完整字体链：内置拉丁 + 系统中文回退。</summary>
    public static string DefaultChain => string.Join(", ", new[] { BundledLatin }.Concat(ChineseFallbacks));

    /// <summary>用户自选字体优先，否则用默认链。</summary>
    public static string Resolve(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.UiFontFamily)
            ? DefaultChain
            : settings.UiFontFamily + ", " + string.Join(", ", ChineseFallbacks);

    public static void Apply(string chain)
    {
        if (Application.Current is { } app)
            app.Resources["WmclUiFontFamily"] = new FontFamily(chain);
    }

    public static void Apply(AppSettings settings) => Apply(Resolve(settings));

    /// <summary>系统已安装字体（去重排序），用于个性化里的自选列表。</summary>
    public static IReadOnlyList<string> InstalledFonts()
    {
        try
        {
            return FontManager.Current.SystemFonts
                .Select(font => font.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
