using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class LocalResourceIconTests
{
    [AvaloniaTheory]
    [InlineData("fabric-string")]
    [InlineData("fabric-map")]
    [InlineData("forge")]
    [InlineData("neoforge")]
    [InlineData("legacy")]
    [InlineData("legacy-list")]
    [InlineData("pack-zip")]
    [InlineData("shader-pack")]
    [InlineData("shader-icon")]
    [InlineData("pack-directory")]
    [InlineData("world-directory")]
    [InlineData("world-backup")]
    public async Task LocalPngIsRenderedWithItsActualPixelsAndOwnedPerAttachment(string format)
    {
        using var files = new LocalIconTestFiles();
        var png = LocalIconTestFiles.Png();
        var kind = LocalResourceKind.Mod;
        string path;
        if (format.EndsWith("directory"))
        {
            kind = format.StartsWith("world") ? LocalResourceKind.World : LocalResourceKind.ResourcePack;
            path = files.DirectoryWithIcon("resource", kind == LocalResourceKind.World ? "icon.png" : "pack.png", png);
        }
        else
        {
            var entries = new List<(string Name, byte[] Bytes)>();
            switch (format)
            {
                case "fabric-string": entries.Add(("fabric.mod.json", Encoding.UTF8.GetBytes("{\"icon\":\"assets/icon.png\"}"))); break;
                case "fabric-map": entries.Add(("fabric.mod.json", Encoding.UTF8.GetBytes("{\"icon\":{\"128\":\"absent.png\",\"24\":\"assets/icon.png\"}}"))); break;
                case "forge": entries.Add(("META-INF/mods.toml", Encoding.UTF8.GetBytes("[[mods]]\nlogoFile = \"assets/icon.png\" # logo"))); break;
                case "neoforge": entries.Add(("META-INF/neoforge.mods.toml", Encoding.UTF8.GetBytes("[[mods]]\nlogoFile = 'assets/icon.png'"))); break;
                case "legacy": entries.Add(("mcmod.info", Encoding.UTF8.GetBytes("[{\"logoFile\":\"assets/icon.png\"}]"))); break;
                case "legacy-list": entries.Add(("mcmod.info", Encoding.UTF8.GetBytes("{\"modList\":[{\"logoFile\":\"assets/icon.png\"}]}"))); break;
                case "pack-zip": kind = LocalResourceKind.ResourcePack; break;
                case "shader-pack":
                case "shader-icon": kind = LocalResourceKind.ShaderPack; break;
                case "world-backup": kind = LocalResourceKind.WorldBackup; break;
            }
            var entryName = kind == LocalResourceKind.Mod ? "assets/icon.png" :
                format is "shader-icon" or "world-backup" ? "icon.png" : "pack.png";
            entries.Add((entryName, png));
            path = files.Archive("resource.jar.disabled", entries.ToArray());
        }
        var icon = new LocalResourceIcon { SourcePath = path, Kind = kind };
        Assert.Null(icon.Source);
        var window = new Window { Content = icon, Width = 80, Height = 80 };
        try
        {
            window.Show();
            await icon.PendingLoad;
            var bitmap = Assert.IsType<Bitmap>(icon.Source);
            Assert.Equal(new PixelSize(24, 24), bitmap.PixelSize);
            using (var pixels = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul))
            using (var buffer = pixels.Lock())
            {
                bitmap.CopyPixels(buffer);
                Assert.Equal(220, Marshal.ReadByte(buffer.Address, 2));
                Assert.Equal(30, Marshal.ReadByte(buffer.Address, 1));
                Assert.Equal(60, Marshal.ReadByte(buffer.Address));
            }
            window.Content = null;
            Assert.Null(icon.Source);
            window.Content = icon;
            await icon.PendingLoad;
            Assert.IsType<Bitmap>(icon.Source);
            Assert.NotSame(bitmap, icon.Source);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("missing")]
    [InlineData("http")]
    [InlineData("unsafe-parent")]
    [InlineData("unsafe-absolute")]
    [InlineData("unsafe-windows")]
    [InlineData("unsafe-backslash")]
    [InlineData("zip-link")]
    [InlineData("oversize")]
    [InlineData("dimensions")]
    [InlineData("negative-dimensions")]
    [InlineData("corrupt")]
    [InlineData("invalid-archive")]
    [InlineData("metadata-bomb")]
    [InlineData("metadata-corrupt")]
    public async Task UnsafeOrMalformedLocalIconsAlwaysKeepNonemptyFallback(string failure)
    {
        using var files = new LocalIconTestFiles();
        var png = LocalIconTestFiles.Png();
        var name = failure switch
        {
            "metadata-bomb" or "metadata-corrupt" => "art/custom-icon.png",
            "unsafe-parent" => "../outside.png",
            "unsafe-absolute" => "/icon.png",
            "unsafe-windows" => "C:/icon.png",
            "unsafe-backslash" => "assets\\icon.png",
            _ => "icon.png"
        };
        if (failure == "oversize") Array.Resize(ref png, LocalResourceIconService.MaxIconBytes + 1);
        if (failure is "dimensions" or "negative-dimensions")
            BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), failure == "dimensions" ? 100000 : -1);
        if (failure == "corrupt") Array.Resize(ref png, 24);
        var metadata = failure == "metadata-bomb" ? new byte[129 * 1024] :
            failure == "metadata-corrupt" ? Encoding.UTF8.GetBytes("{") :
            JsonSerializer.SerializeToUtf8Bytes(new { icon = name });
        var path = failure == "http" ? "https://127.0.0.1:1/icon.png" :
            failure == "missing" ? Path.Combine(files.Root, "missing.jar") :
            files.Archive("bad.jar", ("fabric.mod.json", metadata), (name, png));
        if (failure == "zip-link")
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            archive.GetEntry(name)!.ExternalAttributes = 0xa1ff << 16;
        }
        if (failure == "invalid-archive") File.WriteAllText(path, "not an archive");
        var icon = new LocalResourceIcon { SourcePath = path, Kind = LocalResourceKind.Mod };
        var window = new Window { Content = icon, Width = 80, Height = 80 };
        try
        {
            window.Show();
            await icon.PendingLoad;
            var fallback = Assert.IsType<DrawingImage>(icon.Source);
            Assert.NotNull(Assert.IsType<GeometryDrawing>(fallback.Drawing).Geometry);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task UndeclaredAndLeadingSlashIconsAreResolvedFromConventionalPaths()
    {
        using var files = new LocalIconTestFiles();
        var png = LocalIconTestFiles.Png();
        // NeoForge 包不写 logoFile，只把图标放在 assets/<modid>/icon.png
        var neo = files.Archive("neo.jar",
            ("META-INF/neoforge.mods.toml", Encoding.UTF8.GetBytes("modLoader=\"javafml\"\n[[mods]]\nmodId=\"sample\"\n")),
            ("assets/sample/icon.png", png));
        // 旧版 mcmod.info 的 logoFile 常带前导斜杠
        var legacy = files.Archive("legacy.jar",
            ("mcmod.info", Encoding.UTF8.GetBytes("{\"modList\":[{\"modid\":\"mt\",\"logoFile\":\"/mousetweaks_logo.png\"}]}")),
            ("mousetweaks_logo.png", png));
        var rootPack = files.Archive("pack.jar", ("pack.png", png));
        foreach (var path in new[] { neo, legacy, rootPack })
        {
            var icon = new LocalResourceIcon { SourcePath = path, Kind = LocalResourceKind.Mod };
            var window = new Window { Content = icon, Width = 80, Height = 80 };
            try
            {
                window.Show();
                await icon.PendingLoad;
                Assert.IsType<Bitmap>(icon.Source);
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task DirectoryIconsAndArchivePathsNeverFollowSymbolicLinks()
    {
        if (OperatingSystem.IsWindows()) return;
        using var files = new LocalIconTestFiles();
        var original = files.DirectoryWithIcon("original", "pack.png", LocalIconTestFiles.Png());
        var linkedDirectory = Path.Combine(files.Root, "linked");
        Directory.CreateSymbolicLink(linkedDirectory, original);
        Assert.Null(await LocalResourceIconService.LoadAsync(linkedDirectory, LocalResourceKind.ResourcePack, default));
        var linkedIconDirectory = Path.Combine(files.Root, "linked-icon");
        Directory.CreateDirectory(linkedIconDirectory);
        File.CreateSymbolicLink(Path.Combine(linkedIconDirectory, "pack.png"), Path.Combine(original, "pack.png"));
        Assert.Null(await LocalResourceIconService.LoadAsync(linkedIconDirectory, LocalResourceKind.ResourcePack, default));
        var archive = files.Archive("original.zip", ("pack.png", LocalIconTestFiles.Png()));
        var linkedArchive = Path.Combine(files.Root, "linked.zip");
        File.CreateSymbolicLink(linkedArchive, archive);
        Assert.Null(await LocalResourceIconService.LoadAsync(linkedArchive, LocalResourceKind.ResourcePack, default));
    }

    [AvaloniaFact]
    public async Task CacheReusesBytesButEvictsBoundedlyAndRecognizesFileChanges()
    {
        using var files = new LocalIconTestFiles();
        var png = LocalIconTestFiles.Png();
        var first = files.Archive("first.zip", ("pack.png", png));
        var original = await LocalResourceIconService.LoadAsync(first, LocalResourceKind.ResourcePack, default);
        Assert.NotNull(original);
        Assert.Same(original, await LocalResourceIconService.LoadAsync(first, LocalResourceKind.ResourcePack, default));
        for (var index = 0; index < 140; index++)
        {
            var path = files.Archive($"cached-{index}.zip", ("pack.png", png));
            Assert.NotNull(await LocalResourceIconService.LoadAsync(path, LocalResourceKind.ResourcePack, default));
        }
        Assert.NotSame(original, await LocalResourceIconService.LoadAsync(first, LocalResourceKind.ResourcePack, default));
        File.WriteAllText(first, "changed");
        Assert.Null(await LocalResourceIconService.LoadAsync(first, LocalResourceKind.ResourcePack, default));
    }

    [AvaloniaFact]
    public async Task RecycledIconCannotPublishAStaleBitmapAndFallbacksDistinguishKinds()
    {
        using var files = new LocalIconTestFiles();
        var path = files.Archive("good.zip", ("pack.png", LocalIconTestFiles.Png()));
        var icon = new LocalResourceIcon { SourcePath = path, Kind = LocalResourceKind.ResourcePack };
        var window = new Window { Content = icon, Width = 80, Height = 80 };
        try
        {
            window.Show();
            var previous = icon.PendingLoad;
            icon.SourcePath = Path.Combine(files.Root, "absent.zip");
            await Task.WhenAll(previous, icon.PendingLoad);
            var geometry = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(icon.Source).Drawing).Geometry;
            icon.Kind = LocalResourceKind.ShaderPack;
            await icon.PendingLoad;
            var shaderGeometry = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(icon.Source).Drawing).Geometry;
            Assert.NotSame(geometry, shaderGeometry);
        }
        finally { window.Close(); }
    }
}

internal sealed class LocalIconTestFiles : IDisposable
{
    public string Root { get; } = Path.Combine(WMCL.Tests.TestEnvironment.Root, "compact-icons-" + Guid.NewGuid().ToString("N"));
    public LocalIconTestFiles() => Directory.CreateDirectory(Root);

    public string DirectoryWithIcon(string name, string iconName, byte[] png)
    {
        var directory = Path.Combine(Root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, iconName), png);
        return directory;
    }

    public string Archive(string name, params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(Root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var stream = archive.CreateEntry(entry.Name).Open();
            stream.Write(entry.Bytes);
        }
        return path;
    }

    public static byte[] Png()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(24, 24), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[buffer.RowBytes * 24];
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = 60;
                pixels[offset + 1] = 30;
                pixels[offset + 2] = 220;
                pixels[offset + 3] = 255;
            }
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }
        using var output = new MemoryStream();
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
        return output.ToArray();
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(Root);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(WMCL.Tests.TestEnvironment.Root));
        if (Path.GetDirectoryName(resolved) != parent || !Path.GetFileName(resolved).StartsWith("compact-icons-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fixture directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
