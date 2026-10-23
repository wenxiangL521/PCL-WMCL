using System.Text;

namespace WMCL.Minecraft;

/// <summary>
/// PCL2 的每实例 PCL/Setup.ini 互操作。PCL2 使用 Key:Value 行（不是 INI 节），
/// 这里保留未知键，避免破坏 PCL2 的 State/Info/版本缓存。
/// </summary>
public static class PclSetupIni
{
    public static string PathFor(string versionDirectory) =>
        Path.Combine(versionDirectory, "PCL", "Setup.ini");

    public static IReadOnlyDictionary<string, string> Read(string versionDirectory)
    {
        var file = PathFor(versionDirectory);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(file)) return values;
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var index = line.IndexOf(':');
            if (index <= 0) index = line.IndexOf('=');
            if (index <= 0) continue;
            var key = line[..index].Trim();
            if (key.Length == 0) continue;
            values[key] = line[(index + 1)..].Trim();
        }
        return values;
    }

    public static void Apply(InstanceSettings settings, string versionDirectory)
    {
        var values = Read(versionDirectory);
        if (values.TryGetValue("DisplayType", out var displayType) &&
            int.TryParse(displayType, out var cardType) && cardType is >= 0 and <= 5)
            settings.VersionCardType = cardType;
        if (values.TryGetValue("CustomInfo", out var customInfo) &&
            !string.IsNullOrWhiteSpace(customInfo))
            settings.Description = customInfo;
        if (values.TryGetValue("LogoCustom", out var logoCustom) &&
            bool.TryParse(logoCustom, out var custom) && custom &&
            values.TryGetValue("Logo", out var logo))
        {
            var normalized = logo.Replace('\\', '/');
            if (normalized.EndsWith("PCL/Logo.png", StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(versionDirectory, "PCL", "Logo.png");
                if (File.Exists(path)) settings.LogoPath = path;
            }
        }
    }

    public static void Write(InstanceSettings settings, string versionDirectory)
    {
        var values = new Dictionary<string, string>(Read(versionDirectory), StringComparer.OrdinalIgnoreCase);
        values["DisplayType"] = settings.VersionCardType.ToString();
        values["CustomInfo"] = settings.Description ?? "";
        if (!string.IsNullOrWhiteSpace(settings.LogoPath) && File.Exists(settings.LogoPath))
        {
            var target = Path.Combine(versionDirectory, "PCL", "Logo.png");
            if (!string.Equals(Path.GetFullPath(settings.LogoPath), Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(settings.LogoPath, target, true);
            }
            values["Logo"] = "PCL\\Logo.png";
            values["LogoCustom"] = "True";
        }
        else
        {
            values["Logo"] = "";
            values["LogoCustom"] = "False";
        }
        WriteValues(versionDirectory, values);
    }

    public static void Reset(string versionDirectory)
    {
        var values = new Dictionary<string, string>(Read(versionDirectory), StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "DisplayType", "CustomInfo", "Logo", "LogoCustom" })
            values.Remove(key);
        WriteValues(versionDirectory, values);
    }

    private static void WriteValues(string versionDirectory, IReadOnlyDictionary<string, string> values)
    {
        var file = PathFor(versionDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var builder = new StringBuilder();
        foreach (var pair in values)
            builder.Append(pair.Key).Append(':').Append(pair.Value).Append("\r\n");
        File.WriteAllText(file, builder.ToString(), new UTF8Encoding(false));
    }
}
