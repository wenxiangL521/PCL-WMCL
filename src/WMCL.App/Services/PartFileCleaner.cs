using WMCL.Core.Logging;

namespace WMCL.App.Services;

/// <summary>
/// 取消路径的 .part 临时文件清理（后缀白名单，不误删用户文件）。
/// 暂停路径由调用侧按 IsPaused 分流保留续传资产（design §1.2.3：清理在调用侧而非 DownloadManager）。
/// </summary>
public static class PartFileCleaner
{
    /// <summary>删除 root 下所有 *.part 文件，返回删除数；单文件失败（占用/权限）跳过，root 不存在返回 0。</summary>
    public static int CleanPartFiles(string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;
            var removed = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    Log.Debug("清理 .part 失败：" + file + "：" + ex.Message);
                }
            }
            return removed;
        }
        catch (Exception ex)
        {
            Log.Debug("枚举 .part 失败：" + root + "：" + ex.Message);
            return 0;
        }
    }
}
