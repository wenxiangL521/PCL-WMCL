using System.Buffers.Binary;
using System.Net;
using Avalonia.Media.Imaging;
using WMCL.Launch.Auth;

namespace WMCL.App.Services.Infrastructure;

internal sealed class SkinPreviewLoader(HttpClient? http = null)
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(8)
    })
    { Timeout = TimeSpan.FromSeconds(15) };

    internal async Task<Bitmap?> LoadCapeAsync(MicrosoftCape cape, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cape.Id) || string.IsNullOrWhiteSpace(cape.Url)) return null;
        if (!Uri.TryCreate(cape.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || uri.IdnHost != "textures.minecraft.net" ||
            !uri.IsDefaultPort || uri.UserInfo.Length > 0 || !uri.AbsolutePath.StartsWith("/texture/", StringComparison.Ordinal))
            throw new InvalidDataException("披风纹理地址不是官方 Minecraft 纹理服务。");
        var address = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
        using var response = await (http ?? Client).GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 4 * 1024 * 1024)
            throw new InvalidDataException("披风纹理文件过大。");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("披风纹理文件过大。");
            bytes.Write(buffer, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        var png = bytes.GetBuffer().AsSpan(0, (int)bytes.Length);
        if (png.Length < 24 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !png.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("披风纹理不是有效PNG。");
        var width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        if (width < 64 || width > 1024 || width % 64 != 0 || height != width / 2)
            throw new InvalidDataException("披风纹理尺寸必须是64×32或等比例大小。");
        bytes.Position = 0;
        var bitmap = new Bitmap(bytes);
        if (bitmap.PixelSize.Width < 64 || bitmap.PixelSize.Width > 1024 ||
            bitmap.PixelSize.Width % 64 != 0 || bitmap.PixelSize.Height != bitmap.PixelSize.Width / 2)
        {
            bitmap.Dispose();
            throw new InvalidDataException("披风纹理尺寸必须是64×32或等比例大小。");
        }
        return bitmap;
    }
}
