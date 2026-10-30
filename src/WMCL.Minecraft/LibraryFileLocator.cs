namespace WMCL.Minecraft;

/// <summary>
/// 库文件定位。Windows 上打包的整合包经常把 Maven 文件名写成不同大小写
/// （例如坐标是 fishmodloader，实际文件是 FishModLoader-v3.4.2.jar），
/// Linux 文件系统区分大小写，直接按坐标拼路径会找不到 jar。
/// </summary>
public static class LibraryFileLocator
{
    /// <summary>返回实际存在的文件路径；精确路径不存在时按文件名忽略大小写查找同目录文件。</summary>
    public static string Resolve(string expectedPath)
    {
        if (string.IsNullOrEmpty(expectedPath)) return expectedPath;
        if (File.Exists(expectedPath)) return expectedPath;

        var directory = Path.GetDirectoryName(expectedPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return expectedPath;

        var fileName = Path.GetFileName(expectedPath);
        try
        {
            foreach (var candidate in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 权限/占用问题按原路径返回，由后续安装校验给出明确错误。
        }

        return expectedPath;
    }
}
