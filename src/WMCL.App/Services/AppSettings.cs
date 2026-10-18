using System.Text.Json;
using WMCL.Core;

namespace WMCL.App.Services;

public sealed class AppSettings
{
    public string GameDirectory { get; set; } = WmclPaths.DefaultGameDirectory;
    public List<string> GameFolders { get; set; } = [];
    public Dictionary<string, string> GameFolderNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> FavoriteVersions { get; set; } = [];
    public List<string> HiddenVersions { get; set; } = [];
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
    public string ProcessPriority { get; set; } = "normal";
    public bool HighPerformanceGpu { get; set; }
    public string ProxyUrl { get; set; } = "";
    public double DownloadSpeedLimitMbps { get; set; }
    public int DownloadThreads { get; set; } = 16;
    public string EasyTierPeer { get; set; } = "";
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
    public string? JavaPath { get; set; }
    public string Mirror { get; set; } = "auto";
    public string UpdateManifestUrl { get; set; } = "";
    public string MicrosoftClientId { get; set; } = "";
    public string Theme { get; set; } = "light";
    public int AccentHue { get; set; } = 210;
    public int AccentSaturation { get; set; } = 85;
    public int AccentLightAdjust { get; set; } = 0;
    public string CurseForgeApiKey { get; set; } = "";
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
