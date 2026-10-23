using System.Text.Json;

namespace WMCL.Minecraft;

/// <summary>
/// 每实例设置（对照 PCL2 的 Sources.Instance）：为空时回落到全局设置。
/// 保存于 versions/&lt;id&gt;/WMCL/instance.json。
/// </summary>
public sealed class InstanceSettings
{
    public int? MemoryMb { get; set; }
    public string? JavaPath { get; set; }
    public string ExtraJvmArgs { get; set; } = "";
    public string ExtraGameArgs { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string IsolationMode { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>PCL2 DisplayType：0 自动、1 隐藏、2 可安装 Mod、3 常规、4 不常用、5 愚人节。</summary>
    public int VersionCardType { get; set; }
    public string LogoPath { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string Server { get; set; } = "";
    public int? Port { get; set; }
    /// <summary>该实例使用的第三方认证服务器；为空时使用启动页全局账号。</summary>
    public string AuthServer { get; set; } = "";
    public string AuthServerName { get; set; } = "";
    public string PluginDirectory { get; set; } = "";
    public string PreLaunchCommand { get; set; } = "";
    public bool PreLaunchWait { get; set; } = true;
}

public static class InstanceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string PathFor(string versionDirectory) =>
        Path.Combine(versionDirectory, "WMCL", "instance.json");

    private static string LegacyPathFor(string versionDirectory) =>
        Path.Combine(versionDirectory, "PCL", "instance.json");

    public static InstanceSettings Load(string versionDirectory)
    {
        try
        {
            var file = PathFor(versionDirectory);
            if (!File.Exists(file)) file = LegacyPathFor(versionDirectory);
            var settings = File.Exists(file)
                ? JsonSerializer.Deserialize<InstanceSettings>(File.ReadAllText(file), JsonOptions)
                  ?? new InstanceSettings()
                : new InstanceSettings();
            PclSetupIni.Apply(settings, versionDirectory);
            return settings;
        }
        catch
        {
            return new InstanceSettings();
        }
    }

    public static void Save(string versionDirectory, InstanceSettings settings)
    {
        var file = PathFor(versionDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(settings, JsonOptions));
        PclSetupIni.Write(settings, versionDirectory);
    }

    public static void Reset(string versionDirectory)
    {
        foreach (var file in new[] { PathFor(versionDirectory), LegacyPathFor(versionDirectory) })
            if (File.Exists(file)) File.Delete(file);
        PclSetupIni.Reset(versionDirectory);
    }
}
