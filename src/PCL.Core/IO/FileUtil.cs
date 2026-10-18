using System.Security.Cryptography;

namespace PCL.Core.IO;

public static class FileUtil
{
    public static async Task<string> Sha1Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static string Sha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA1.HashData(stream));
    }

    public static bool Exists(string path) => File.Exists(path);

    public static void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>先写临时文件再原子替换，避免半文件。</summary>
    public static void MoveReplace(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: true);
    }

    public static long GetLength(string path) => File.Exists(path) ? new FileInfo(path).Length : -1;

    public static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>把不可信文件名限制为单段文件名，去掉目录/相对路径。</summary>
    public static string SafeFileName(string name)
    {
        var value = name.Replace('\\', '/').Trim();
        var file = Path.GetFileName(value);
        if (string.IsNullOrWhiteSpace(file) || file is "." or ".." ||
            file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("非法的文件名：" + name);
        return file;
    }

    /// <summary>把不可信相对路径限制在 root 下，阻止 ../ 与绝对路径逃逸。</summary>
    public static string SafeChildPath(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root);
        var raw = relative.Replace('\\', '/');
        if (raw.StartsWith('/') || (raw.Length >= 2 && raw[1] == ':'))
            throw new InvalidDataException("不允许绝对路径：" + relative);
        var normalized = raw.TrimStart('/');
        if (normalized.Split('/').Contains(".."))
            throw new InvalidDataException("非法的相对路径：" + relative);
        var full = Path.GetFullPath(Path.Combine(rootFull, normalized));
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("路径超出目标目录：" + relative);
        return full;
    }
}
