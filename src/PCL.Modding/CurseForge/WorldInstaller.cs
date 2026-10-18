using System.IO.Compression;

namespace PCL.Modding.CurseForge;

/// <summary>把 CurseForge 世界 zip 解压到 saves/&lt;name&gt;，自动处理单层目录。</summary>
public static class WorldInstaller
{
    public static string Install(string zipPath, string savesDirectory, string name)
    {
        Directory.CreateDirectory(savesDirectory);
        var safeName = Sanitize(name);
        var destination = UniqueDirectory(savesDirectory, safeName);
        var temp = Path.Combine(Path.GetTempPath(), "pcl-world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, temp);
            var entries = Directory.GetFileSystemEntries(temp);
            var source = temp;
            if (entries.Length == 1 && Directory.Exists(entries[0]) &&
                !File.Exists(Path.Combine(temp, "level.dat")))
                source = entries[0];
            Directory.Move(source, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var value = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (value.Length == 0) value = "world";
        return value.Length > 80 ? value[..80] : value;
    }

    private static string UniqueDirectory(string root, string name)
    {
        var path = Path.Combine(root, name);
        var index = 2;
        while (Directory.Exists(path))
            path = Path.Combine(root, $"{name} ({index++})");
        return path;
    }
}
