using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

public sealed class SkinModelVariantFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "skin-variant-flow-" + Guid.NewGuid().ToString("N"));
    private string SkinFile => Path.Combine(_root, "slim.png");

    [AvaloniaTheory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void TransparentUnusedArmStripsIdentifyTheModelAtEveryTextureScale(int scale, bool slim)
    {
        using var bitmap = CreateSkin(scale, slim);
        Assert.Equal(slim ? "slim" : "classic", SkinModelDetector.Detect(bitmap));
        Assert.Equal("classic", SkinModelDetector.Resolve(bitmap, "classic"));
        Assert.Equal("slim", SkinModelDetector.Resolve(bitmap, "slim"));
    }

    [AvaloniaFact]
    public void LegacySkinNeverUsesModernSlimStrips()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        Assert.Equal("classic", SkinModelDetector.Detect(bitmap));
    }

    [AvaloniaFact]
    public async Task AutoPreviewAndUploadUseTheSameDetectedModelEvenWhenRemoteMetadataSaysClassic()
    {
        string? uploadedVariant = null;
        var model = Model((_, _, variant) => { uploadedVariant = variant; return Task.CompletedTask; });
        var window = new MsSkinWindow(model);
        try
        {
            window.Show();
            await PumpAsync();
            Assert.Equal("classic", model.CurrentSkinVariant);
            Assert.Equal("auto", model.SelectedSkinVariant);
            var preview = window.FindControl<SkinModelPreview>("CharacterPreview")!;
            Assert.Equal("slim", preview.Variant);
            Assert.Contains("细臂", window.FindControl<TextBlock>("SkinModelHintText")!.Text);
            await model.UploadMicrosoftSkinAsync(SkinFile);
            Assert.Equal("slim", uploadedVariant);
            Assert.Equal("slim", preview.Variant);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ModelSelectorChangesExistingAccountPreviewWithoutPickingALocalFile()
    {
        var model = Model((_, _, _) => Task.CompletedTask);
        var window = new MsSkinWindow(model);
        try
        {
            window.Show();
            await PumpAsync();
            var preview = window.FindControl<SkinModelPreview>("CharacterPreview")!;
            Assert.Equal("slim", preview.Variant);
            model.SelectedSkinVariant = "classic";
            Assert.Equal("classic", preview.Variant);
            model.SelectedSkinVariant = "slim";
            Assert.Equal("slim", preview.Variant);
            model.SelectedSkinVariant = "auto";
            Assert.Equal("slim", preview.Variant);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AcceptingAnUnchangedSlimEditPreservesModelThroughLocalPreviewAndUpload()
    {
        var uploads = 0;
        string? uploadedVariant = null;
        var model = Model((_, _, variant) => { uploads++; uploadedVariant = variant; return Task.CompletedTask; });
        var window = new MsSkinWindow(model);
        try
        {
            window.Show();
            await PumpAsync();
            var edit = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "编辑皮肤"));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpAsync();
            var editor = Assert.IsType<SkinEditorWindow>(Assert.Single(window.OwnedWindows));
            Assert.Equal("slim", editor.FindControl<SkinModelPreview>("EditModelPreview")!.Variant);
            editor.FindControl<Button>("AcceptEditButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SkinEditorTask;
            Assert.Equal("slim", model.SelectedSkinVariant);
            Assert.Equal("slim", window.FindControl<SkinModelPreview>("CharacterPreview")!.Variant);
            Assert.Equal(0, uploads);
            window.FindControl<Button>("MsConfirmUploadButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SkinApplicationTask;
            Assert.Equal(1, uploads);
            Assert.Equal("slim", uploadedVariant);
        }
        finally { window.Close(); }
    }

    private LaunchViewModel Model(Func<MicrosoftAccount, string, string, Task> upload)
    {
        Directory.CreateDirectory(_root);
        using (var skin = CreateSkin(1, true)) skin.Save(SkinFile);
        var model = new LaunchViewModel(new AppServices(new AppSettings { AccountType = "offline" }, saveSettings: _ => { }),
            null, null, Path.Combine(_root, "accounts"), null, _ => Task.FromResult<string?>(null),
            getMicrosoftCapes: _ => Task.FromResult<IReadOnlyList<MicrosoftCape>>([]), uploadMicrosoftSkin: upload,
            downloadMicrosoftSkinWithInfo: _ => Task.FromResult(new MicrosoftSkinDownload(SkinFile, "classic")));
        model.SetMicrosoftAccount(new MicrosoftAccount("VariantPlayer", "uuid", "token", "refresh", DateTimeOffset.MaxValue));
        return model;
    }

    internal static WriteableBitmap CreateSkin(int scale, bool slim)
    {
        var bitmap = new WriteableBitmap(new PixelSize(64 * scale, 64 * scale), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var pixels = bitmap.Lock();
        for (var y = 0; y < 64 * scale; y++)
            for (var x = 0; x < 64 * scale; x++)
                Marshal.WriteInt32(pixels.Address, y * pixels.RowBytes + x * 4, unchecked((int)0xff6090c0));
        if (slim)
        {
            (int X, int Y, int W, int H)[] regions = [(50, 16, 2, 4), (54, 20, 2, 12), (42, 48, 2, 4), (46, 52, 2, 12)];
            foreach (var region in regions)
                for (var y = region.Y * scale; y < (region.Y + region.H) * scale; y++)
                    for (var x = region.X * scale; x < (region.X + region.W) * scale; x++)
                        Marshal.WriteInt32(pixels.Address, y * pixels.RowBytes + x * 4, 0);
        }
        return bitmap;
    }
    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 4; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
