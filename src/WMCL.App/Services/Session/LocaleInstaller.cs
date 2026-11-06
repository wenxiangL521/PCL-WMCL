using WMCL.Core;

namespace WMCL.App.Services;

/// <summary>
/// 启动时把嵌入的本地化资源复制到数据目录，供 Localizer 加载。
/// 这样无需在 csproj 里做特殊处理，也支持用户自定义翻译。
/// </summary>
internal static class LocaleInstaller
{
    /// <summary>嵌入资源名中的目录前缀（&lt;程序集名&gt;.Assets.Locales.）。</summary>
    private static readonly string ResourcePrefix =
        typeof(LocaleInstaller).Assembly.GetName().Name + ".Assets.Locales.";

    public static void EnsureInstalled()
    {
        var targetDir = Path.Combine(WmclPaths.DataDirectory, "locales");
        var embedded = typeof(LocaleInstaller).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".yaml"))
            .ToList();
        if (embedded.Count == 0) return;

        Directory.CreateDirectory(targetDir);
        // 清理历史版本写出的错误命名文件（当时把完整资源名当成了文件名）。
        foreach (var stale in Directory.EnumerateFiles(targetDir, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(stale).Contains("Assets.Locales.", StringComparison.Ordinal))
                try { File.Delete(stale); } catch { /* 清理失败不影响安装 */ }
        }
        foreach (var name in embedded)
        {
            // 资源名格式：<程序集名>.Assets.Locales.<locale>.yaml。
            // 注意：清单资源名以点分层，不能用 Path.GetFileNameWithoutExtension 截取
            // （它只识别目录分隔符，会把整个资源名当成“文件名”）。
            var localeName = name[ResourcePrefix.Length..];
            if (localeName.Length == 0 || localeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                continue;
            var target = Path.Combine(targetDir, localeName);
            try
            {
                if (!File.Exists(target))
                {
                    using var stream = typeof(LocaleInstaller).Assembly.GetManifestResourceStream(name);
                    if (stream is null) continue;
                    using var reader = new StreamReader(stream);
                    File.WriteAllText(target, reader.ReadToEnd());
                }
            }
            catch
            {
                // 写入失败不影响启动，下次重试
            }
        }
    }
}
