using WMCL.Core;

namespace WMCL.App.Services;

public sealed class AppSettings
{
    public string GameDirectory { get; set; } = WmclPaths.DefaultGameDirectory;
    public List<string> GameFolders { get; set; } = [];
    public Dictionary<string, string> GameFolderNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>上次选中的版本（跨目录的兜底值）。</summary>
    /// <summary>保存过的离线角色名。</summary>
    public List<string> OfflineRoles { get; set; } = [];

    public string LastVersionId { get; set; } = "";

    /// <summary>按游戏目录记录上次选中的版本。</summary>
    public Dictionary<string, string> LastVersionByFolder { get; set; } = [];

    public List<string> FavoriteVersions { get; set; } = [];
    public List<string> HiddenVersions { get; set; } = [];
    /// <summary>被隐藏的功能键，见 FeatureCatalog。</summary>
    public List<string> HiddenFeatures { get; set; } = [];
    public List<string> FavoriteDownloadComponents { get; set; } = [];
    public List<ResourceFavoriteFolder> ResourceFavoriteFolders { get; set; } = [];
    public Dictionary<string, List<string>> FavoriteVersionsByFolder { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> HiddenVersionsByFolder { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int MemoryMb { get; set; } = 4096;
    /// <summary>游戏窗口宽度（写入启动参数），不是启动器窗口尺寸。</summary>
    public int Width { get; set; } = 850;
    public int Height { get; set; } = 500;
    /// <summary>启动器窗口尺寸，默认 PCL2 的 850×500；用户拖动后会记住。</summary>
    public int LauncherWidth { get; set; } = 850;
    public int LauncherHeight { get; set; } = 500;

    /// <summary>上次窗口位置；null 表示未记录，由系统决定。</summary>
    public int? LauncherLeft { get; set; }

    public int? LauncherTop { get; set; }
    public string ExtraJvmArgs { get; set; } = "";
    public string ExtraGameArgs { get; set; } = "";
    public string PreLaunchCommand { get; set; } = "";
    public bool PreLaunchWait { get; set; } = true;
    /// <summary>旧版「游戏时隐藏启动器」，已并入 LauncherAfterLaunch，仅用于迁移。</summary>
    public bool HideLauncherOnGame { get; set; }

    /// <summary>启动游戏后启动器的去向：keep 保持打开、hide 隐藏到后台、close 关闭启动器。</summary>
    public string? LauncherAfterLaunch { get; set; }

    /// <summary>点击关闭按钮的行为：hide = 隐藏到托盘，exit = 退出程序。</summary>
    public string CloseBehavior { get; set; } = "hide";
    public string IsolationMode { get; set; } = "auto";
    public bool HintModifyInstanceWarningAcknowledged { get; set; }
    public bool HintUpdateModWarningAcknowledged { get; set; }
    public bool MinecraftPurchaseNoticeAcknowledged { get; set; }
    public string ProcessPriority { get; set; } = "normal";
    public bool HighPerformanceGpu { get; set; }
    public string GpuIndex { get; set; } = "";
    public string ProxyUrl { get; set; } = "";
    /// <summary>代理模式：none / system / custom。</summary>
    public string ProxyMode { get; set; } = "none";
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";
    public string ProxyBypassList { get; set; } = "localhost,127.0.0.1,::1";
    /// <summary>回收站自动清理天数（0=不自动清理）。</summary>
    public int TrashAutoCleanDays { get; set; } = 30;
    /// <summary>删除时移入回收站而非永久删除。默认关闭。</summary>
    public bool EnableTrash { get; set; }
    public double DownloadSpeedLimitMbps { get; set; }
    public int DownloadThreads { get; set; } = 16;
    public string EasyTierPeer { get; set; } = "";
    public bool LinkLatencyFirst { get; set; }
    public string EasyTierRoomCode { get; set; } = "";
    public string LinkBackend { get; set; } = "EasyTier";
    public string TerracottaMode { get; set; } = "创建房间";
    public string TerracottaExecutablePath { get; set; } = "";
    public string CustomLogoPath { get; set; } = "";
    public string CustomHomePagePath { get; set; } = "";
    public string MusicFolder { get; set; } = "";
    public int MusicVolume { get; set; } = 70;
    public bool MusicStart { get; set; }
    public bool MusicPauseOnGame { get; set; } = true;
    public string BackgroundImagePath { get; set; } = "";
    public string BackgroundImageFolder { get; set; } = "";
    public bool BackgroundColorOverlay { get; set; }
    public int BackgroundImageOpacity { get; set; } = 30;
    public int BackgroundImageBlur { get; set; } = 0;
    public string PlayerName { get; set; } = "Steve";
    public string AccountType { get; set; } = "";
    public string? JavaPath { get; set; }
    public string Mirror { get; set; } = "auto";
    public string Theme { get; set; } = "light";

    /// <summary>更新通道：stable / beta。</summary>
    public string UpdateChannel { get; set; } = "stable";

    /// <summary>更新模式：off / prompt / download / auto。</summary>
    public string UpdateMode { get; set; } = "prompt";

    /// <summary>界面字体族；留空表示内置鸿蒙 + 系统中文字体。</summary>
    public string UiFontFamily { get; set; } = "";
    public int AccentHue { get; set; } = 210;
    public int AccentSaturation { get; set; } = 85;
    public int AccentLightAdjust { get; set; } = 0;
    /// <summary>UI 缩放比例（0.8~1.5，默认 1.0）。</summary>
    public double FontScale { get; set; } = 1.0;
    /// <summary>窗口透明度滑块（0~100）；实际窗口不透明度映射为 40%~100%，与 PCL-ME 一致。</summary>
    public int ApplicationOpacity { get; set; } = 100;
    /// <summary>请求系统桌面背景模糊；Niri使用专用窗口规则控制。</summary>
    public bool WindowBackdropBlur { get; set; }
    /// <summary>自定义快捷键配置（JSON 字符串，key=快捷键标识，value=键组合）。</summary>
    public string CustomShortcuts { get; set; } = "";
    public string Locale { get; set; } = "zh-CN";
}

public static class SettingsService
{
    public static string? LastLoadWarning { get; private set; }

    public static AppSettings Load()
    {
        var result = new FileAppSettingsStore(WmclPaths.ConfigFile).LoadWithStatus();
        LastLoadWarning = result.Warning;
        ApplyLegacyMigrations(result.Settings);
        return result.Settings;
    }

    public static void Save(AppSettings settings)
    {
        WmclPaths.EnsureCreated();
        new FileAppSettingsStore(WmclPaths.ConfigFile).Save(settings);
    }

    /// <summary>导出设置到指定路径（敏感字段脱敏）。</summary>
    public static string ExportToFile(AppSettings settings, string outputPath)
    {
        WmclPaths.EnsureCreated();
        File.WriteAllText(outputPath, AppSettingsSerializer.ExportToJson(settings));
        return outputPath;
    }

    /// <summary>从指定路径导入设置，返回导入的设置对象。</summary>
    public static AppSettings ImportFromFile(string inputPath)
    {
        var settings = AppSettingsImportPolicy.Sanitize(
            AppSettingsSerializer.Deserialize(File.ReadAllText(inputPath)));
        ApplyLegacyMigrations(settings);
        return settings;
    }

    /// <summary>把旧版开关并入「启动后启动器」三态设置。</summary>
    public static void ApplyLegacyMigrations(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LauncherAfterLaunch))
            settings.LauncherAfterLaunch = settings.HideLauncherOnGame ? "hide" : "keep";
    }

    /// <summary>导出设置到剪贴板（JSON 文本，敏感字段脱敏）。</summary>
    public static string ExportToJson(AppSettings settings) => AppSettingsSerializer.ExportToJson(settings);

    /// <summary>从 JSON 文本导入设置。</summary>
    public static AppSettings ImportFromJson(string json) =>
        AppSettingsImportPolicy.Sanitize(AppSettingsSerializer.Deserialize(json));

    public static void NormalizeFolders(AppSettings settings) => AppSettingsNormalizer.Normalize(settings);
}
