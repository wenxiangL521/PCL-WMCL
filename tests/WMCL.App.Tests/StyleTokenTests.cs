using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit.Abstractions;

namespace WMCL.App.Tests;

/// <summary>
/// L4 样式 token 扫描断言（design §4.2-B1/B2/B3）：
/// B1 悬空资源键为零、B2 本期 token 双主题齐备、B3 axaml 内联颜色字面量为零（白名单豁免除外）。
/// 定义键直接遍历 ResourceDictionary 结构（自身 + MergedDictionaries + ThemeDictionaries），
/// 不依赖运行时资源解析顺序，避免 ThemeService 顶层覆写干扰主题字典断言。
/// </summary>
public sealed class StyleTokenTests
{
    private readonly ITestOutputHelper _output;

    public StyleTokenTests(ITestOutputHelper output) => _output = output;

    private static string? TryLocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WMCL.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    private static IEnumerable<(string File, int Line, string Key)> CollectReferencedKeys(string repoRoot)
    {
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            var text = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty,
                RegexOptions.Singleline);
            foreach (Match match in Regex.Matches(text, @"\{DynamicResource\s+([A-Za-z0-9_]+)\}"))
                yield return (file, 1 + text.AsSpan(0, match.Index).Count('\n'), match.Groups[1].Value);
        }
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

    [AvaloniaFact]
    public void NoDanglingResourceKeys()
    {
        var repoRoot = TryLocateRepoRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未定位到仓库根（WMCL.slnx），跳过扫描。");
            return;
        }

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dictionary in AllDictionaries())
        {
            defined.UnionWith(KeysOf(dictionary));
            if (dictionary.ThemeDictionaries is { Count: > 0 } themes)
                foreach (var theme in themes.Values.OfType<ResourceDictionary>())
                    defined.UnionWith(KeysOf(theme));
        }

        var dangling = CollectReferencedKeys(repoRoot)
            .Where(reference => !reference.Key.StartsWith("System", StringComparison.Ordinal))
            .Where(reference => !defined.Contains(reference.Key))
            .ToList();

        foreach (var (file, line, key) in dangling)
            _output.WriteLine($"悬空键 {key} @ {Path.GetFileName(file)}:{line}");
        Assert.Empty(dangling);
    }

    [AvaloniaFact]
    public void TokenDefinedInBothThemes()
    {
        string[] themeTokens =
        [
            "WmclTextStrong", "WmclTextDisabled", "WmclDivider",
            "WmclGray6", "WmclGray7", "WmclGray8", "WmclSuccess", "WmclPageFlat",
            "WmclOverlayButtonBase", "WmclOverlayCapsule", "WmclOverlayCapsuleHover",
            "WmclOverlayTitleText", "WmclOverlayTaskProgress",
            "WmclShadowCard", "WmclShadowTaskBall", "WmclShadowWindow", "WmclShadowTooltip",
            "WmclScrollbarThumb", "WmclScrollbarThumbHover", "WmclSliderThumb",
            "WmclTitleCloseHover", "WmclTitleGradient"
        ];

        var light = FindThemeDictionary(ThemeVariant.Light);
        var dark = FindThemeDictionary(ThemeVariant.Dark);
        Assert.NotNull(light);
        Assert.NotNull(dark);
        var lightKeys = KeysOf(light!);
        var darkKeys = KeysOf(dark!);

        var missing = new List<string>();
        foreach (var key in themeTokens)
        {
            if (!lightKeys.Contains(key)) missing.Add(key + " @Light");
            if (!darkKeys.Contains(key)) missing.Add(key + " @Dark");
        }

        // WmclWindowCorner 为主题无关顶层资源。
        var topLevelKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dictionary in AllDictionaries())
            topLevelKeys.UnionWith(KeysOf(dictionary));
        if (!topLevelKeys.Contains("WmclWindowCorner"))
            missing.Add("WmclWindowCorner @顶层");

        foreach (var item in missing)
            _output.WriteLine("缺失 token：" + item);
        Assert.Empty(missing);
    }

    [Fact]
    public void NoInlineColorLiterals()
    {
        var repoRoot = TryLocateRepoRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未定位到仓库根（WMCL.slnx），跳过扫描。");
            return;
        }

        var whitelistFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            // token 定义处：色值字面量的唯一合法来源
            "WmclColors.axaml"
        };
        // 按"文件名 + 色值"登记豁免（行号会随后续编辑漂移，不作锚点）。
        var whitelistValues = new HashSet<(string File, string Value)>
        {
            // 纯白 #FFFFFF：设计内保留（标题按钮前景等），非叠层语义
            ("MainWindow.axaml", "#FFFFFF"),
            // ToolTip 文案中的十六进制示例字符串，非样式取值
            ("MsSkinWindow.axaml", "#FF555555")
        };

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (whitelistFiles.Contains(name)) continue;
            var text = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty,
                RegexOptions.Singleline);
            foreach (Match match in Regex.Matches(text, @"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b"))
            {
                if (whitelistValues.Contains((name, match.Value.ToUpperInvariant()))) continue;
                var line = 1 + text.AsSpan(0, match.Index).Count('\n');
                hits.Add($"{name}:{line} {match.Value}");
            }
        }

        foreach (var hit in hits)
            _output.WriteLine("字面量：" + hit);
        Assert.Empty(hits);
    }
}
