using Avalonia.Media.Imaging;

namespace WMCL.App.Services;

/// <summary>
/// 图片解码的统一入口：ViewModel 不再各自 new Bitmap 处理 data URI / 资源路径 / 本地文件。
/// 这里仍返回 Avalonia 的 Bitmap（展示层类型），但把重复的容错与缓存逻辑集中到一处。
/// </summary>
public static class ImageDecoder
{
    /// <summary>解析服务器图标：支持 <c>data:image/png;base64,...</c> 与裸 base64。</summary>
    public static Bitmap? FromFavicon(string? favicon)
    {
        if (string.IsNullOrWhiteSpace(favicon)) return null;
        try
        {
            var comma = favicon.IndexOf(',');
            var payload = comma >= 0 ? favicon[(comma + 1)..] : favicon;
            using var stream = new MemoryStream(Convert.FromBase64String(payload));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从本地文件解码；文件缺失或损坏时返回 null。</summary>
    public static Bitmap? FromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
