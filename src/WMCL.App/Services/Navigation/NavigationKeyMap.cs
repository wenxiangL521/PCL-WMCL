namespace WMCL.App.Services.Navigation;

/// <summary>导航目标对应的顶部标签。</summary>
public enum NavTarget
{
    Launch,
    Download,
    Link,
    Settings,
    More
}

/// <summary>把「导航 key」映射为顶部标签与子页 key，供 <c>MainWindowViewModel</c> 使用。</summary>
public static class NavigationKeyMap
{
    public static (NavTarget Target, string? SubKey) Resolve(string key) => key switch
    {
        "download" or "loaders" => (NavTarget.Download, "download"),
        "cleanroom" => (NavTarget.Download, "cleanroom"),
        "labymod" => (NavTarget.Download, "labymod"),
        "mods" or "modpack" or "favorites" or "content-resource" or "content-shader"
            or "content-world" or "content-datapack" => (NavTarget.Download, key),
        "instance" => (NavTarget.More, "instance"),
        "content" => (NavTarget.Download, "content-resource"),
        "settings" => (NavTarget.Settings, "settings-launch"),
        "settings-launch" or "settings-games" or "settings-log" or "settings-update"
            or "settings-java" or "settings-link" or "settings-ui" or "settings-other"
            => (NavTarget.Settings, key),
        "about" or "help" or "toolbox" or "feedback" or "vote" => (NavTarget.More, key),
        "more" => (NavTarget.More, "help"),
        "link" => (NavTarget.Link, null),
        _ => (NavTarget.Launch, null)
    };
}
