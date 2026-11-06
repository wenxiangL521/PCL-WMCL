using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using WMCL.App.Services;
using Xunit.Abstractions;

namespace WMCL.App.Tests;

/// <summary>
/// 二期（ui-pcl2-polish）L4 扫描断言：三色标渐变、页面背景亮度、Color4 公式、
/// 灰阶/维度 token、圆角与字号档外字面量、字符图标清零。
/// 组 0 先落红色骨架自证；组 1~4 逐项转绿。
/// </summary>
[Trait("Kind", "Headless")]
public sealed class PolishStyleTests
{
    private readonly ITestOutputHelper _output;

    public PolishStyleTests(ITestOutputHelper output) => _output = output;

    private static string? TryLocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WMCL.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    private static IEnumerable<ResourceDictionary> EnumerateDictionaries(ResourceDictionary dictionary)
    {
        yield return dictionary;
        foreach (var merged in dictionary.MergedDictionaries)
            if (merged is ResourceDictionary concrete)
                foreach (var nested in EnumerateDictionaries(concrete))
                    yield return nested;
    }

    private static List<ResourceDictionary> AllDictionaries()
    {
        if (Application.Current?.Resources is not ResourceDictionary root) return [];
        return EnumerateDictionaries(root).ToList();
    }

    private static HashSet<string> KeysOf(ResourceDictionary dictionary) =>
        [.. dictionary.Keys.Select(key => key.ToString() ?? "")];

    private static ResourceDictionary? FindThemeDictionary(ThemeVariant variant)
    {
        foreach (var dictionary in AllDictionaries())
            if (dictionary.ThemeDictionaries is { Count: > 0 } themes &&
                themes.TryGetValue(variant, out var provider) &&
                provider is ResourceDictionary theme)
                return theme;
        return null;
    }

    /// <summary>从全字典（含双主题）查找矢量几何资源。</summary>
    internal static StreamGeometry? FindIconGeometry(string key)
    {
        foreach (var dictionary in AllDictionaries())
        {
            if (dictionary.TryGetResource(key, null, out var value) && value is StreamGeometry geometry)
                return geometry;
            if (dictionary.ThemeDictionaries is not { Count: > 0 } themes) continue;
            foreach (var theme in themes.Values.OfType<ResourceDictionary>())
                if (theme.TryGetResource(key, null, out var themed) && themed is StreamGeometry themedGeometry)
                    return themedGeometry;
        }
        return null;
    }

    private static IEnumerable<(string File, int Line, string Value)> ScanAttribute(
        string repoRoot, string attribute)
    {
        var pattern = new Regex($"{attribute}=\"([0-9.]+)\"", RegexOptions.Compiled);
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            var text = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty,
                RegexOptions.Singleline);
            foreach (Match match in pattern.Matches(text))
                yield return (Path.GetFileName(file), 1 + text.AsSpan(0, match.Index).Count('\n'),
                    match.Groups[1].Value);
        }
    }

    internal static List<(string File, int Line, string Value)> ScanCornerLiterals(string repoRoot)
    {
        var whitelistFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "WmclColors.axaml", "WmclDimensions.axaml", "WmclTheme.axaml"
        };
        var whitelistValues = new HashSet<(string File, string Value)>
        {
            // Slider Thumb 圆形语义（半径 = 直径/2）
            ("WmclTheme.axaml", "8"),
            // LaunchView 日志框直角语义
            ("LaunchView.axaml", "0")
        };
        return ScanAttribute(repoRoot, "CornerRadius")
            .Where(hit => !whitelistFiles.Contains(hit.File))
            .Where(hit => !whitelistValues.Contains((hit.File, hit.Value)))
            .ToList();
    }

    internal static List<(string File, int Line, string Value)> ScanFontSizeLiterals(string repoRoot)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "11", "12", "13", "13.5", "16", "18", "20", "24", "30", "36"
        };
        return ScanAttribute(repoRoot, "FontSize")
            .Where(hit => !allowed.Contains(hit.Value))
            .ToList();
    }

    internal static List<(string File, int Line, char Symbol)> ScanCharacterIcons(string repoRoot)
    {
        // 只匹配"属性值/字符串字面量恰为一个图标字符"的场景，避免误伤文案中的省略号等普通标点。
        const string symbols = "⚙↗⋯↺⟳▶↓◎…";
        var textAttribute = new Regex($"\"([{symbols}]+)\"", RegexOptions.Compiled);
        var csAssignment = new Regex($"NavItem\\(\"([{symbols}]+)\"", RegexOptions.Compiled);
        var hits = new List<(string, int, char)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"),
                     "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".axaml") || f.EndsWith(".cs")))
        {
            var text = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty,
                RegexOptions.Singleline);
            var pattern = file.EndsWith(".axaml") ? textAttribute : csAssignment;
            foreach (Match match in pattern.Matches(text))
                hits.Add((Path.GetFileName(file),
                    1 + text.AsSpan(0, match.Index).Count('\n'),
                    match.Groups[1].Value[0]));
        }
        return hits;
    }

    [AvaloniaFact]
    public void TitleGradientHasThreeSymmetricStops()
    {
        var light = FindThemeDictionary(ThemeVariant.Light);
        Assert.NotNull(light);
        Assert.True(light!.TryGetResource("WmclTitleGradient", null, out var value));
        var gradient = Assert.IsType<LinearGradientBrush>(value);
        Assert.Equal(3, gradient.GradientStops.Count);
        Assert.Equal(ThemeService.Hsl(210, 85, 48), gradient.GradientStops[0].Color);
        Assert.Equal(ThemeService.Hsl(210, 85, 54), gradient.GradientStops[1].Color);
        Assert.Equal(ThemeService.Hsl(210, 85, 48), gradient.GradientStops[2].Color);
    }

    [AvaloniaFact]
    public void PageBackgroundUsesLightness91()
    {
        var light = FindThemeDictionary(ThemeVariant.Light);
        Assert.NotNull(light);
        Assert.True(light!.TryGetResource("WmclPage", null, out var value));
        var gradient = Assert.IsType<LinearGradientBrush>(value);
        Assert.Equal(3, gradient.GradientStops.Count);
        Assert.Equal(ThemeService.Hsl(195, 68, 91), gradient.GradientStops[0].Color);
        Assert.Equal(ThemeService.Hsl(210, 68, 91), gradient.GradientStops[1].Color);
        Assert.Equal(ThemeService.Hsl(225, 68, 91), gradient.GradientStops[2].Color);
    }

    [AvaloniaFact]
    public void ThemeService_Color4Uses65PlusAdjust()
    {
        ThemeService.SetWindowsOverrideForTests(false);
        try
        {
            ThemeService.Apply("light", 130, 65, 12);
            var blue4 = Assert.IsType<SolidColorBrush>(Application.Current!.Resources["WmclBlue4"]);
            Assert.Equal(ThemeService.Hsl(130, 65, 77), blue4.Color);
        }
        finally
        {
            ThemeService.SetWindowsOverrideForTests(null);
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public void GrayTokensExistInBothThemes()
    {
        string[] keys = ["WmclGray6", "WmclGray7", "WmclGray8", "WmclSuccess", "WmclPageFlat"];
        var light = FindThemeDictionary(ThemeVariant.Light);
        var dark = FindThemeDictionary(ThemeVariant.Dark);
        Assert.NotNull(light);
        Assert.NotNull(dark);
        var lightKeys = KeysOf(light!);
        var darkKeys = KeysOf(dark!);
        var missing = new List<string>();
        foreach (var key in keys)
        {
            if (!lightKeys.Contains(key)) missing.Add(key + " @Light");
            if (!darkKeys.Contains(key)) missing.Add(key + " @Dark");
        }
        foreach (var item in missing) _output.WriteLine("缺失：" + item);
        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void DimensionTokensMatchSpec()
    {
        var expected = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["WmclCornerSmall"] = new CornerRadius(2),
            ["WmclCornerButton"] = new CornerRadius(3),
            ["WmclCornerCard"] = new CornerRadius(5),
            ["WmclCornerListItem"] = new CornerRadius(6),
            ["WmclCornerWindow"] = new CornerRadius(8),
            ["WmclCornerCapsule"] = new CornerRadius(13.5),
            ["WmclCornerCircle"] = new CornerRadius(20),
            ["WmclCornerAvatar"] = new CornerRadius(32),
            ["WmclPagePadding"] = new Thickness(20, 15, 20, 10)
        };
        var missing = new List<string>();
        foreach (var (key, value) in expected)
        {
            var found = false;
            foreach (var dictionary in AllDictionaries())
                if (dictionary.TryGetResource(key, null, out var actual))
                {
                    if (!Equals(actual, value)) missing.Add($"{key} 值不匹配：{actual}");
                    found = true;
                    break;
                }
            if (!found) missing.Add(key + " 缺失");
        }
        foreach (var item in missing) _output.WriteLine(item);
        Assert.Empty(missing);
    }

    [Fact]
    public void NoOutOfBandCornerLiterals()
    {
        var repoRoot = TryLocateRepoRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未定位到仓库根，跳过扫描。");
            return;
        }
        var hits = ScanCornerLiterals(repoRoot);
        foreach (var (file, line, value) in hits)
            _output.WriteLine($"圆角字面量 {file}:{line} = {value}");
        Assert.Empty(hits);
    }

    [Fact]
    public void NoOutOfBandFontSizeLiterals()
    {
        var repoRoot = TryLocateRepoRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未定位到仓库根，跳过扫描。");
            return;
        }
        var hits = ScanFontSizeLiterals(repoRoot);
        foreach (var (file, line, value) in hits)
            _output.WriteLine($"字号越档 {file}:{line} = {value}");
        Assert.Empty(hits);
    }

    [Fact]
    public void NoCharacterIconsRemain()
    {
        var repoRoot = TryLocateRepoRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未定位到仓库根，跳过扫描。");
            return;
        }
        var hits = ScanCharacterIcons(repoRoot);
        foreach (var (file, line, symbol) in hits)
            _output.WriteLine($"字符图标 {file}:{line} = {symbol}");
        Assert.Empty(hits);
    }
}




