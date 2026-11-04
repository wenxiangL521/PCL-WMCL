using WMCL.Core;
namespace WMCL.Infrastructure;

/// <summary>
/// 实例回收站管理器。删除实例时移入回收站，支持恢复和永久删除。
/// 存放于 DataDirectory/trash/。
/// </summary>
public static class TrashManager
{
    private const string MetaFileName = "trash.json";

    public static string TrashDirectory =>
        Path.Combine(WmclPaths.DataDirectory, "trash");

    /// <summary>获取回收站中的实例列表。</summary>
    public static List<TrashEntry> ListEntries()
    {
        var result = new List<TrashEntry>();
        if (!Directory.Exists(TrashDirectory)) return result;

        foreach (var dir in Directory.EnumerateDirectories(TrashDirectory))
        {
            var info = new DirectoryInfo(dir);
            var meta = ReadMeta(dir);
            var versionId = meta?.VersionId ?? Path.GetFileName(dir);
            // 元数据不可靠时回退目录名；两者都不可用作文件名时跳过该条目。
            if (!IsValidVersionId(versionId)) continue;
            result.Add(new TrashEntry(
                versionId,
                dir,
                meta?.DeletedAt ?? info.LastWriteTime,
                GetSize(dir)));
        }
        return result.OrderByDescending(e => e.DeletedAt).ToList();
    }

    /// <summary>把实例目录移入回收站。</summary>
    public static void MoveToTrash(string gameRoot, string versionId)
    {
        if (!IsValidVersionId(versionId))
            throw new ArgumentException("版本 ID 不能包含路径分隔符。", nameof(versionId));
        var source = Path.Combine(gameRoot, "versions", versionId);
        if (!Directory.Exists(source)) return;

        Directory.CreateDirectory(TrashDirectory);
        // 同一秒内重复删除同名实例（删除→恢复→再删除）时，秒级时间戳目标已存在；
        // 冲突时追加序号。Directory.Move 不支持跨文件系统，游戏目录在其他挂载点时
        // 会抛 IOException，由调用方提示用户改用直接删除。
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(TrashDirectory, $"{versionId}-{timestamp}");
        for (var attempt = 2; Directory.Exists(target); attempt++)
            target = Path.Combine(TrashDirectory, $"{versionId}-{timestamp}-{attempt}");
        Directory.Move(source, target);

        // 写入元数据
        File.WriteAllText(
            Path.Combine(target, MetaFileName),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                versionId,
                originalPath = source,
                deletedAt = DateTime.Now.ToString("o")
            }));
    }

    /// <summary>从回收站恢复实例到指定游戏根目录。</summary>
    public static void Restore(string trashPath, string gameRoot)
    {
        var versionId = ReadMeta(trashPath)?.VersionId ?? Path.GetFileName(trashPath);
        if (!IsValidVersionId(versionId))
            throw new InvalidOperationException("回收站元数据中的版本 ID 无效，无法恢复。");

        var target = Path.Combine(gameRoot, "versions", versionId);
        if (Directory.Exists(target))
            throw new InvalidOperationException($"目标实例 {versionId} 已存在。");

        Directory.CreateDirectory(Path.Combine(gameRoot, "versions"));
        // 恢复时移除回收站元数据文件，避免残留在实例目录中。
        try { File.Delete(Path.Combine(trashPath, MetaFileName)); }
        catch (IOException) { /* 删除失败不阻止恢复，残留文件无害 */ }
        Directory.Move(trashPath, target);
    }

    /// <summary>永久删除回收站中的实例。</summary>
    public static void PermanentDelete(string trashPath)
    {
        if (Directory.Exists(trashPath))
            Directory.Delete(trashPath, true);
    }

    /// <summary>清空回收站。</summary>
    public static void ClearAll()
    {
        if (!Directory.Exists(TrashDirectory)) return;
        foreach (var dir in Directory.EnumerateDirectories(TrashDirectory))
            Directory.Delete(dir, true);
    }

    /// <summary>自动清理超过指定天数的回收站条目。</summary>
    public static int AutoClean(int days)
    {
        if (days <= 0) return 0;
        var cutoff = DateTime.Now.AddDays(-days);
        var cleaned = 0;
        foreach (var entry in ListEntries())
        {
            if (entry.DeletedAt < cutoff)
            {
                PermanentDelete(entry.Path);
                cleaned++;
            }
        }
        return cleaned;
    }

    /// <summary>版本 ID 必须是单段文件名（拒绝路径分隔符等注入）。</summary>
    private static bool IsValidVersionId(string? versionId) =>
        !string.IsNullOrWhiteSpace(versionId) &&
        versionId.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, .. Path.GetInvalidFileNameChars()]) < 0;

    private sealed record TrashMeta(string VersionId, DateTime DeletedAt);

    private static TrashMeta? ReadMeta(string trashPath)
    {
        var metaPath = Path.Combine(trashPath, MetaFileName);
        if (!File.Exists(metaPath)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("versionId", out var versionId) ||
                !root.TryGetProperty("deletedAt", out var deletedAt))
                return null;
            var id = versionId.GetString();
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (!DateTime.TryParse(deletedAt.GetString(), out var at)) return null;
            return new TrashMeta(id, at);
        }
        catch
        {
            return null;
        }
    }

    private static long GetSize(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>回收站条目。</summary>
public sealed record TrashEntry(string VersionId, string Path, DateTime DeletedAt, long Size);
