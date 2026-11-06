using Avalonia.Input;
using WMCL.App.Services;

namespace WMCL.App.Tests;

/// <summary>
/// 快捷键解析与匹配的回归测试。
/// 历史缺陷：数字走 Enum.TryParse 按枚举数值解析（"1"→Key.Cancel），
/// 导致默认的 Ctrl+1~5 全部无法命中，且 "Ctrl+3" 与 "Ctrl+Tab" 解析冲突；
/// 修饰键用严格相等比较导致 Ctrl+Shift+Tab 无法命中 Ctrl+Tab。
/// </summary>
[Trait("Kind", "Unit")]
public sealed class ShortcutParserTests
{
    [Theory]
    [InlineData("1", Key.D1)]
    [InlineData("2", Key.D2)]
    [InlineData("3", Key.D3)]
    [InlineData("5", Key.D5)]
    [InlineData("0", Key.D0)]
    [InlineData("9", Key.D9)]
    public void Parse_DigitsMapToDKeys(string digit, Key expected)
    {
        var (key, _) = ShortcutParser.Parse(digit);
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("F", Key.F)]
    [InlineData("f", Key.F)]
    [InlineData("Tab", Key.Tab)]
    [InlineData("tab", Key.Tab)]
    public void Parse_NamedKeysResolveByEnum(string name, Key expected)
    {
        var (key, _) = ShortcutParser.Parse(name);
        Assert.Equal(expected, key);
    }

    [Fact]
    public void Parse_ModifiersCombine()
    {
        var (key, mods) = ShortcutParser.Parse("Ctrl+Shift+Tab");
        Assert.Equal(Key.Tab, key);
        Assert.True(mods.HasFlag(KeyModifiers.Control));
        Assert.True(mods.HasFlag(KeyModifiers.Shift));
    }

    [Fact]
    public void Parse_Ctrl3IsNotTab()
    {
        // 历史回归："3" 曾被解析成 Key.Tab(=3)，与 Ctrl+Tab 冲突。
        var (digit, _) = ShortcutParser.Parse("Ctrl+3");
        var (tab, _) = ShortcutParser.Parse("Ctrl+Tab");
        Assert.NotEqual(digit, tab);
        Assert.Equal(Key.D3, digit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage-key-name")]
    public void Parse_InvalidInputReturnsNone(string? input)
    {
        var (key, mods) = ShortcutParser.Parse(input);
        Assert.Equal(Key.None, key);
        Assert.Equal(KeyModifiers.None, mods);
    }

    private static KeyEventArgs CreateKeyEvent(Key key, KeyModifiers modifiers)
        => new()
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers
        };

    [Fact]
    public void Matches_DigitShortcutHitsRealDigitKey()
    {
        // 历史回归：Ctrl+1 对 Key.D1（用户实际按键）必须命中。
        var e = CreateKeyEvent(Key.D1, KeyModifiers.Control);
        Assert.True(ShortcutParser.Matches(e, "Ctrl+1"));
    }

    [Fact]
    public void Matches_ExtraShiftStillMatches()
    {
        // 修饰键“必需齐全”语义：Ctrl+Shift+Tab 命中 Ctrl+Tab（方向由调用方判断）。
        var e = CreateKeyEvent(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.True(ShortcutParser.Matches(e, "Ctrl+Tab"));
    }

    [Fact]
    public void Matches_MissingModifierFails()
    {
        var e = CreateKeyEvent(Key.Tab, KeyModifiers.None);
        Assert.False(ShortcutParser.Matches(e, "Ctrl+Tab"));
    }

    [Fact]
    public void Matches_DifferentKeyFails()
    {
        var e = CreateKeyEvent(Key.D2, KeyModifiers.Control);
        Assert.False(ShortcutParser.Matches(e, "Ctrl+1"));
    }

    [Fact]
    public void ParseTable_DefaultsWhenJsonEmpty()
    {
        var table = ShortcutParser.ParseTable(null);
        Assert.Equal("Ctrl+1", table["Tab1"]);
        Assert.Equal("Ctrl+Tab", table["Tab"]);
        Assert.Equal("Ctrl+F", table["Search"]);
    }

    [Fact]
    public void ParseTable_CustomOverridesDefaults()
    {
        var json = """{"Tab1":"Ctrl+6","Search":"Ctrl+P"}""";
        var table = ShortcutParser.ParseTable(json);
        Assert.Equal("Ctrl+6", table["Tab1"]);
        Assert.Equal("Ctrl+P", table["Search"]);
        Assert.Equal("Ctrl+2", table["Tab2"]);
    }

    [Fact]
    public void ParseTable_InvalidJsonFallsBackToDefaults()
    {
        var table = ShortcutParser.ParseTable("not json{{{");
        Assert.Equal("Ctrl+1", table["Tab1"]);
    }

    [Fact]
    public void ParseTable_IgnoresUnknownKeysAndEmptyValues()
    {
        var json = """{"Unknown":"Ctrl+9","Tab1":""}""";
        var table = ShortcutParser.ParseTable(json);
        Assert.Equal("Ctrl+1", table["Tab1"]);
        Assert.False(table.ContainsKey("Unknown"));
    }
}
