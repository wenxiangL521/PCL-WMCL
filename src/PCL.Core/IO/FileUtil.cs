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
}
