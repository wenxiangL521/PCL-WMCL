using System.Text.Json;
using WMCL.Core;
using WMCL.Core.Localization;

namespace WMCL.App.Services;

public sealed class AppSettings
{
    public string GameDirectory { get; set; } = WmclPaths.DefaultGameDirectory;
    public List<string> GameFolders { get; set; } = [];
    public Dictionary<string, string> GameFolderNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> FavoriteVersions { get; set; } = [];
    public List<string> HiddenVersions { get; set; } = [];
    public List<string> FavoriteDownloadComponents { get; set; } = [];
    public List<ResourceFavoriteFolder> ResourceFavoriteFolders { get; set; } = [];
    public Dictionary<string, List<string>> FavoriteVersionsByFolder { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> HiddenVersionsByFolder { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int MemoryMb { get; set; } = 4096;
    public int Width { get; set; } = 850;
    public int Height { get; set; } = 500;
    public string ExtraJvmArgs { get; set; } = "";
    public string ExtraGameArgs { get; set; } = "";
    public string PreLaunchCommand { get; set; } = "";
    public bool PreLaunchWait { get; set; } = true;
    public bool HideLauncherOnGame { get; set; }
    public string IsolationMode { get; set; } = "auto";
    public bool HintModifyInstanceWarningAcknowledged { get; set; }
    public bool HintUpdateModWarningAcknowledged { get; set; }
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
    public string UpdateManifestUrl { get; set; } = "";
    public string MicrosoftClientId { get; set; } = "";
    public string Theme { get; set; } = "light";
    public int AccentHue { get; set; } = 210;
    public int AccentSaturation { get; set; } = 85;
    public int AccentLightAdjust { get; set; } = 0;
    public string CurseForgeApiKey { get; set; } = "";
    /// <summary>UI 缩放比例（0.8~1.5，默认 1.0）。</summary>
    public double FontScale { get; set; } = 1.0;
    /// <summary>自定义快捷键配置（JSON 字符串，key=快捷键标识，value=键组合）。</summary>
    public string CustomShortcuts { get; set; } = "";
    public string Locale { get; set; } = "zh-CN";
    public bool WelcomeCompleted { get; set; }
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(WmclPaths.ConfigFile))
            {
                var fresh = new AppSettings();
                NormalizeFolders(fresh);
                return fresh;
            }
            var json = File.ReadAllText(WmclPaths.ConfigFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            NormalizeFolders(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        WmclPaths.EnsureCreated();
        NormalizeFolders(settings);
        File.WriteAllText(WmclPaths.ConfigFile, JsonSerializer.Serialize(settings, Options));
    }

    /// <summary>导出设置到指定路径（敏感字段脱敏）。</summary>
    public static string ExportToFile(AppSettings settings, string outputPath)
    {
        WmclPaths.EnsureCreated();
        NormalizeFolders(settings);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(RedactSecrets(settings), Options));
        return outputPath;
    }

    /// <summary>从指定路径导入设置，返回导入的设置对象。</summary>
    public static AppSettings ImportFromFile(string inputPath)
    {
        var json = File.ReadAllText(inputPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        NormalizeFolders(settings);
        return settings;
    }

    /// <summary>导出设置到剪贴板（JSON 文本，敏感字段脱敏）。</summary>
    public static string ExportToJson(AppSettings settings)
    {
        NormalizeFolders(settings);
        return JsonSerializer.Serialize(RedactSecrets(settings), Options);
    }

    /// <summary>从 JSON 文本导入设置。</summary>
    public static AppSettings ImportFromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        NormalizeFolders(settings);
        return settings;
    }

    /// <summary>
    /// 返回去除敏感凭据的副本。导出的配置文件常被分享或上传，
    /// 代理密码与 API Key 不应随之泄漏；导入后按需重新填写。
    /// 通过 JSON 往返克隆，避免手工枚举字段导致遗漏。
    /// </summary>
    private static AppSettings RedactSecrets(AppSettings settings)
    {
        var clone = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, Options), Options) ?? new AppSettings();
        SensitiveTextRedactor.RedactSettings(clone);
        return clone;
    }

    private static Dictionary<string, List<string>> NormalizeFolderMap(
        Dictionary<string, List<string>>? map,
        List<string> legacy,
        string activeFolder)
    {
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, ids) in map ?? [])
        {
            var full = WmclPaths.Expand(folder);
            normalized[full] = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if (normalized.Count == 0 && legacy.Count > 0)
            normalized[activeFolder] = legacy
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        return normalized;
    }

    public static void NormalizeFolders(AppSettings settings)
    {
        settings.GameDirectory = WmclPaths.Expand(settings.GameDirectory);
        settings.GameFolders ??= [];
        settings.FavoriteVersions ??= [];
        settings.HiddenVersions ??= [];
        settings.FavoriteDownloadComponents ??= [];
        settings.FavoriteVersionsByFolder = NormalizeFolderMap(
            settings.FavoriteVersionsByFolder, settings.FavoriteVersions, settings.GameDirectory);
        settings.HiddenVersionsByFolder = NormalizeFolderMap(
            settings.HiddenVersionsByFolder, settings.HiddenVersions, settings.GameDirectory);
        settings.GameFolderNames ??= new(StringComparer.OrdinalIgnoreCase);
        settings.GameFolders = settings.GameFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(WmclPaths.Expand)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!settings.GameFolders.Contains(settings.GameDirectory, StringComparer.OrdinalIgnoreCase))
            settings.GameFolders.Add(settings.GameDirectory);
    }
}
