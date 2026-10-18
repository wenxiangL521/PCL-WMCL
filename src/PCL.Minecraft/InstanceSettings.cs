using System.Text.Json;

namespace PCL.Minecraft;

/// <summary>
/// 每实例设置（对照 PCL 的 Sources.Instance）：为空时回落到全局设置。
/// 保存于 versions/&lt;id&gt;/PCL/instance.json。
/// </summary>
public sealed class InstanceSettings
{
    public int? MemoryMb { get; set; }
    public string? JavaPath { get; set; }
    public string ExtraJvmArgs { get; set; } = "";
    public string ExtraGameArgs { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string Server { get; set; } = "";
    public int? Port { get; set; }
}

public static class InstanceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string PathFor(string versionDirectory) =>
        Path.Combine(versionDirectory, "PCL", "instance.json");

    public static InstanceSettings Load(string versionDirectory)
    {
        try
        {
            var file = PathFor(versionDirectory);
            if (!File.Exists(file)) return new InstanceSettings();
            return JsonSerializer.Deserialize<InstanceSettings>(
                       File.ReadAllText(file), JsonOptions)
                   ?? new InstanceSettings();
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
    }

    public static void Reset(string versionDirectory)
    {
        var file = PathFor(versionDirectory);
        if (File.Exists(file)) File.Delete(file);
    }
}
