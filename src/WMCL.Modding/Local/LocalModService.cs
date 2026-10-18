namespace WMCL.Modding.Local;

/// <summary>
/// 本地 Mod 文件。对照 PCL2 的 LocalResourceFile：
/// 禁用时把 foo.jar 改名为 foo.jar.disabled（若已存在则用 .old），启用时改回 foo.jar。
/// </summary>
public sealed record LocalMod(
    string FilePath,
    string FileName,
    string EnabledName,
    bool IsEnabled,
    long Size,
    string? UpdateVersion = null)
{
    public string DisplayName => EnabledName;
    public string StateText => IsEnabled ? "已启用" : "已禁用";
    public string UpdateText => UpdateVersion is null ? "" : $"可更新 → {UpdateVersion}";
    public string SizeText => Size switch
    {
        >= 1024 * 1024 => $"{Size / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{Size / 1024d:0.0} KB",
        _ => $"{Size} B"
    };
}

public static class LocalModService
{
    private const string DisabledSuffix = ".disabled";
    private const string OldSuffix = ".old";

    public static IReadOnlyList<LocalMod> List(string modsDirectory)
    {
        if (!Directory.Exists(modsDirectory)) return [];

        return Directory.EnumerateFiles(modsDirectory)
            .Where(IsModFile)
            .Select(file =>
            {
                var fileName = Path.GetFileName(file);
                var (enabledName, enabled) = ParseName(fileName);
                return new LocalMod(
                    file,
                    fileName,
                    enabledName,
                    enabled,
                    new FileInfo(file).Length);
            })
            .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SetEnabled(string modsDirectory, LocalMod mod, bool enabled)
    {
        if (mod.IsEnabled == enabled) return;

        var current = Path.Combine(modsDirectory, mod.FileName);
        if (!File.Exists(current))
            throw new FileNotFoundException("Mod 文件不存在。", current);

        if (enabled)
        {
            var target = Path.Combine(modsDirectory, mod.EnabledName);
            if (File.Exists(target) && !string.Equals(target, current, StringComparison.Ordinal))
                throw new IOException($"已存在同名 Mod：{mod.EnabledName}");
            File.Move(current, target);
            return;
        }

        var disabled = Path.Combine(modsDirectory, mod.EnabledName + DisabledSuffix);
        if (File.Exists(disabled))
            disabled = Path.Combine(modsDirectory, mod.EnabledName + OldSuffix);
        if (File.Exists(disabled))
            throw new IOException($"无法禁用：{mod.EnabledName}.disabled / .old 已存在");

        File.Move(current, disabled);
    }

    public static void Delete(LocalMod mod)
    {
        if (File.Exists(mod.FilePath)) File.Delete(mod.FilePath);
    }

    private static bool IsModFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".jar" + DisabledSuffix, StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".jar" + OldSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static (string EnabledName, bool IsEnabled) ParseName(string fileName)
    {
        if (fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            return (fileName[..^DisabledSuffix.Length], false);
        if (fileName.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
            return (fileName[..^OldSuffix.Length], false);
        return (fileName, true);
    }
}
