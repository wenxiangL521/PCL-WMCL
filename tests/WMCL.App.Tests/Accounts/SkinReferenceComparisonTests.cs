using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Auth;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WMCL.App.Controls;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class SkinReferenceComparisonTests
{
    [AvaloniaFact]
    public async Task RenderBothArmModelsFromTheSameOptionalReferenceTextures()
    {
        var root = Environment.GetEnvironmentVariable("WMCL_SKIN_REFERENCE_DIR");
        if (string.IsNullOrWhiteSpace(root)) return;
        using var skin = new Bitmap(Path.Combine(root, "skin.png"));
        using var cape = new Bitmap(Path.Combine(root, "cape.png"));
        var skinTexture = SkinModelRasterizer.Texture.Read(skin);
        var capeTexture = SkinModelRasterizer.Texture.Read(cape);
        var renderer = new SkinModelRasterizer();
        foreach (var slim in new[] { false, true })
            foreach (var yaw in new[] { 0, 180 })
            {
                const int width = 384, height = 512;
                var pixels = renderer.Render(skinTexture, capeTexture, SkinModelGeometry.Build(slim, false, true), width, height, yaw, 0);
                using var frame = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using (var buffer = frame.Lock())
                {
                    var row = new int[width];
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++) row[x] = unchecked((int)(pixels[y * width + x] >> 24 == 0 ? 0xfff7faff : pixels[y * width + x]));
                        Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, width);
                    }
                }
                frame.Save(Path.Combine(root, $"wmcl-{yaw}-{(slim ? "slim" : "classic")}.png"), PngBitmapEncoderOptions.Default);
            }
        var services = new AppServices(new AppSettings { AccountType = "offline" }, saveSettings: _ => { });
        var model = new LaunchViewModel(services, null, null,
            Path.Combine(WMCL.Tests.TestEnvironment.Root, "reference-account-" + Guid.NewGuid().ToString("N")),
            null, _ => Task.FromResult<string?>(null),
            getMicrosoftCapes: _ => Task.FromResult<IReadOnlyList<MicrosoftCape>>([]),
            downloadMicrosoftSkinWithInfo: _ => Task.FromResult(new MicrosoftSkinDownload(Path.Combine(root, "skin.png"), "classic")));
        model.SetMicrosoftAccount(new MicrosoftAccount("Preview", "preview", "fake", "fake", DateTimeOffset.MaxValue));
        var window = new MsSkinWindow(null, (_, _) => Task.FromResult<Bitmap?>(new Bitmap(Path.Combine(root, "cape.png")))) { DataContext = model };
        try
        {
            window.Show();
            var selectedCape = new MicrosoftCape("preview", "Builder", "https://textures.minecraft.net/texture/fixture", false);
            model.Capes.Add(selectedCape);
            model.SelectedCape = selectedCape;
            await window.CapePreviewTask;
            var preview = window.FindControl<SkinModelPreview>("CharacterPreview")!;
            Assert.Equal("slim", preview.Variant);
            preview.Yaw = 180;
            preview.Pitch = 0;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var image = window.CaptureRenderedFrame();
            Assert.NotNull(image);
            image.Save(Path.Combine(root, "wmcl-fixed-window.png"), PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }
}
