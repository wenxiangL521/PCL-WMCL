using WMCL.Minecraft;

namespace WMCL.App.Services;

/// <summary>搜索页的版本过滤：显示值可为实例名，查询时透穿成真实游戏版本。</summary>
public static class SearchVersionFilter
{
    /// <summary>下拉首项：不做版本过滤。</summary>
    public const string AllLabel = "全部版本";

    /// <summary>把「显示值」解析成接口可用的游戏版本；解析不出返回空串（全部）。</summary>
    public static string Effective(VersionRepository repository, AppSettings settings, string? shown)
    {
        var text = shown?.Trim() ?? "";
        if (text.Length == 0 || text == AllLabel) return "";
        if (GameVersionOptions.LooksLikeGameVersion(text)) return text;
        return GameVersionOptions.ResolveGameVersion(repository, text, settings.GameDirectory) ?? "";
    }

    /// <summary>填充版本列表并设置默认值；releasesOnly 为 true 时不列已安装版本。</summary>
    public static async Task FillAsync(
        System.Collections.ObjectModel.ObservableCollection<string> target,
        VersionRepository repository,
        AppSettings settings,
        bool releasesOnly,
        string? current,
        Action<string?> setValue,
        bool defaultAll = false)
    {
        if (target.Count == 0)
        {
            target.Add(AllLabel);
            var options = await GameVersionOptions.LoadAsync(repository).ConfigureAwait(true);
            foreach (var version in releasesOnly ? options.Releases : options.All)
                if (!target.Contains(version)) target.Add(version);
        }
        if (defaultAll)
        {
            if (string.IsNullOrWhiteSpace(current)) setValue(AllLabel);
            return;
        }
        if (string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(settings.LastVersionId))
            setValue(settings.LastVersionId);
    }
}
