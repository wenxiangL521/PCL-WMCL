using System.Text.Json;
using PCL.Core;

namespace PCL.App.Services;

public sealed class AppSettings
{
    public string GameDirectory { get; set; } = PclPaths.DefaultGameDirectory;
    public int MemoryMb { get; set; } = 4096;
    public string PlayerName { get; set; } = "Steve";
    public string? JavaPath { get; set; }
    public string Mirror { get; set; } = "auto";
    public string UpdateManifestUrl { get; set; } = "";
    public string MicrosoftClientId { get; set; } = "";
    public string Theme { get; set; } = "light";
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
            if (!File.Exists(PclPaths.ConfigFile)) return new AppSettings();
            var json = File.ReadAllText(PclPaths.ConfigFile);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        PclPaths.EnsureCreated();
        File.WriteAllText(PclPaths.ConfigFile, JsonSerializer.Serialize(settings, Options));
    }
}
