namespace WMCL.App.Services;

public sealed record FeatureDefinition(string Key, string Group, string Label);

/// <summary>可隐藏的功能清单。键值会写入设置，重命名会导致旧配置失效，因此保持稳定。</summary>
public static class FeatureCatalog
{
    public const string Download = "top-download";
    public const string Link = "top-link";
    public const string Settings = "top-settings";
    public const string Tools = "top-more";
    public const string HomeNews = "launch-news";

    public const string SettingsLaunch = "settings-launch";
    public const string SettingsJava = "settings-java";
    public const string SettingsGames = "settings-games";
    public const string SettingsLink = "settings-link";
    public const string SettingsLog = "settings-log";
    public const string SettingsUpdate = "settings-update";
    public const string SettingsUi = "settings-ui";
    public const string SettingsOther = "settings-other";

    public const string Help = "help";
    public const string About = "about";
    public const string Toolbox = "toolbox";
    public const string Feedback = "feedback";
    public const string Vote = "vote";

    public const string InstanceOverview = "instance-overview";
    public const string InstanceSettings = "instance-settings";
    public const string InstanceModify = "instance-modify";
    public const string InstanceMods = "instance-mods";
    public const string InstanceExport = "instance-export";
    public const string InstanceServer = "instance-server";
    public const string InstanceResourcePacks = "instance-resourcepacks";
    public const string InstanceSaves = "instance-saves";
    public const string InstanceShaders = "instance-shaders";
    public const string InstanceScreenshots = "instance-screenshots";

    public const string ModUpdate = "feature-mod-update";
    public const string FeatureHiding = "feature-hiding";

    public const string HomeGroup = "顶栏页面";
    public const string LaunchGroup = "启动页";
    public const string SettingsGroup = "设置页的子页";
    public const string ToolsGroup = "更多页的子页";
    public const string InstanceGroup = "实例设置页的子页";
    public const string SpecificGroup = "特定功能";

    public static IReadOnlyList<FeatureDefinition> All { get; } =
    [
        new(Download, HomeGroup, "下载"),
        new(Link, HomeGroup, "联机"),
        new(Settings, HomeGroup, "设置"),
        new(Tools, HomeGroup, "更多"),
        new(HomeNews, LaunchGroup, "Minecraft 新闻"),

        new(SettingsLaunch, SettingsGroup, "启动"),
        new(SettingsJava, SettingsGroup, "Java"),
        new(SettingsGames, SettingsGroup, "游戏管理"),
        new(SettingsLink, SettingsGroup, "联机设置"),
        new(SettingsLog, SettingsGroup, "日志"),
        new(SettingsUpdate, SettingsGroup, "更新"),
        new(SettingsUi, SettingsGroup, "个性化"),
        new(SettingsOther, SettingsGroup, "其他"),

        new(Help, ToolsGroup, "帮助"),
        new(About, ToolsGroup, "关于与鸣谢"),
        new(Toolbox, ToolsGroup, "百宝箱"),
        new(Feedback, ToolsGroup, "反馈"),
        new(Vote, ToolsGroup, "新功能投票"),

        new(InstanceOverview, InstanceGroup, "概览"),
        new(InstanceSettings, InstanceGroup, "设置"),
        new(InstanceModify, InstanceGroup, "修改"),
        new(InstanceMods, InstanceGroup, "Mod 管理"),
        new(InstanceExport, InstanceGroup, "导出"),
        new(InstanceServer, InstanceGroup, "服务器"),
        new(InstanceResourcePacks, InstanceGroup, "资源包"),
        new(InstanceSaves, InstanceGroup, "存档"),
        new(InstanceShaders, InstanceGroup, "光影"),
        new(InstanceScreenshots, InstanceGroup, "截图"),

        new(ModUpdate, SpecificGroup, "Mod 更新"),
        new(FeatureHiding, SpecificGroup, "功能隐藏")
    ];

    public static IReadOnlyList<string> Groups { get; } =
        [HomeGroup, LaunchGroup, SettingsGroup, ToolsGroup, InstanceGroup, SpecificGroup];
}

/// <summary>功能隐藏状态。F12 按住时临时全部显示，不写回设置。</summary>
public sealed class FeatureVisibility(AppSettings settings, Action save)
{
    private bool _temporarilyRevealed;

    public event EventHandler? Changed;

    public bool IsTemporarilyRevealed => _temporarilyRevealed;

    public bool IsHidden(string key) => settings.HiddenFeatures.Contains(key, StringComparer.Ordinal);

    public bool IsVisible(string key) => !IsHidden(key) || _temporarilyRevealed;

    public void SetHidden(string key, bool hidden)
    {
        bool changed;
        if (hidden)
        {
            if (IsHidden(key)) return;
            settings.HiddenFeatures.Add(key);
            changed = true;
        }
        else
        {
            changed = settings.HiddenFeatures.RemoveAll(item =>
                string.Equals(item, key, StringComparison.Ordinal)) > 0;
        }
        if (!changed) return;
        save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RevealTemporarily(bool reveal)
    {
        if (_temporarilyRevealed == reveal) return;
        _temporarilyRevealed = reveal;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>只保留仍然可见的条目；隐藏就是隐藏，不再回退成完整列表。</summary>
    public IReadOnlyList<T> Filter<T>(IReadOnlyList<T> items, Func<T, string> key) where T : class =>
        items.Where(item => IsVisible(key(item))).ToArray();
}
