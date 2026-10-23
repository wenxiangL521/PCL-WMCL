using System.IO.Compression;
using WMCL.Core.IO;

namespace WMCL.Modding.CurseForge;

/// <summary>把 CurseForge 世界 zip 解压到 saves/&lt;name&gt;，自动处理单层目录。</summary>
public static class WorldInstaller
{
    public static string Install(string zipPath, string savesDirectory, string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(savesDirectory);
        var safeName = FileUtil.SafeFileName(Sanitize(name).Replace('\\', '_'));
        var destination = UniqueDirectory(savesDirectory, safeName);
        var temp = Path.Combine(savesDirectory, ".wmcl-world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var buffer = new byte[81920];
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new InvalidDataException("存档压缩包包含符号链接：" + entry.FullName);
                var path = FileUtil.SafeChildPath(temp, entry.FullName.TrimEnd('/', '\\'));
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var sourceStream = entry.Open();
                using var destinationStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                int count;
                while ((count = sourceStream.Read(buffer)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    destinationStream.Write(buffer, 0, count);
                }
            }
            var worlds = Directory.GetFiles(temp, "level.dat", SearchOption.AllDirectories);
            if (worlds.Length != 1)
                throw new InvalidDataException(worlds.Length == 0 ? "压缩包没有包含有效的存档目录（level.dat）。" : "压缩包包含多个存档，请分别导入。");
            var source = Path.GetDirectoryName(worlds[0])!;
            ct.ThrowIfCancellationRequested();
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
        while (Directory.Exists(path) || File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            path = Path.Combine(root, $"{name} ({index++})");
        return path;
    }
}
