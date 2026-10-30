using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class SkinEditorViewModelTests
{
    private static Bitmap CreateBitmap(int width, int height) =>
        new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);

    [AvaloniaFact]
    public void Constructor_HasDefaultColor()
    {
        var vm = new SkinEditorViewModel();
        Assert.False(vm.HasSkin);
        Assert.Equal(new Color(0x55, 0x55, 0x55, 0xFF), vm.SelectedColor);
    }

    [AvaloniaFact]
    public void LoadSkin_Null_SetsHasSkinFalse()
    {
        var vm = new SkinEditorViewModel();
        vm.LoadSkin(null);
        Assert.False(vm.HasSkin);
        Assert.Empty(vm.Pixels);
        Assert.Equal(0, vm.Width);
        Assert.Equal(0, vm.Height);
    }

    [AvaloniaFact]
    public void LoadSkin_ValidBitmap_LoadsPixels()
    {
        var vm = new SkinEditorViewModel();
        using var bmp = CreateBitmap(64, 32);
        vm.LoadSkin(bmp);
        Assert.True(vm.HasSkin);
        Assert.Equal(64, vm.Width);
        Assert.Equal(32, vm.Height);
        Assert.Equal(64 * 32, vm.Pixels.Length);
        Assert.All(vm.Pixels, p => Assert.Null(p));
    }

    [AvaloniaFact]
    public void PaintPixel_WithinBounds_SetsPixel()
    {
        var vm = Load(64, 32);
        vm.PaintPixel(10, 10);
        Assert.NotNull(vm.Pixels[10 * 64 + 10]);
        Assert.Equal(10, vm.LastX);
        Assert.Equal(10, vm.LastY);
    }

    [AvaloniaFact]
    public void PaintPixel_OutOfBounds_NoOp()
    {
        var vm = Load(64, 32);
        vm.PaintPixel(-1, 0);
        vm.PaintPixel(64, 0);
        vm.PaintPixel(0, 32);
        Assert.Null(vm.Pixels[0]);
    }

    [AvaloniaFact]
    public void PaintPixel_Eraser_SetsNull()
    {
        var vm = Load(64, 32);
        vm.IsEraser = true;
        vm.PaintPixel(5, 5);
        Assert.Null(vm.Pixels[5 * 64 + 5]);
    }

    [AvaloniaFact]
    public void PaintPixel_BrushSize2_Paints4Pixels()
    {
        var vm = Load(64, 32);
        vm.BrushSize = 2;
        vm.PaintPixel(10, 10);
        Assert.NotNull(vm.Pixels[10 * 64 + 10]);
        Assert.NotNull(vm.Pixels[10 * 64 + 11]);
        Assert.NotNull(vm.Pixels[11 * 64 + 10]);
        Assert.NotNull(vm.Pixels[11 * 64 + 11]);
    }

    [AvaloniaFact]
    public void Fill_FromCenter_FillsConnectedRegion()
    {
        var vm = Load(10, 10);
        vm.Fill(5, 5);
        Assert.NotNull(vm.Pixels[5 * 10 + 5]);
        Assert.NotNull(vm.Pixels[0]);
    }

    [AvaloniaFact]
    public void Fill_AlreadySameColor_NoChange()
    {
        var vm = Load(10, 10);
        var target = new Color(255, 0, 0, 255);
        for (var i = 0; i < vm.Pixels.Length; i++)
            vm.Pixels[i] = target;
        vm.SelectedColor = target;
        vm.Fill(5, 5);
        Assert.Equal(target, vm.Pixels[5 * 10 + 5]);
    }

    [AvaloniaFact]
    public void MirrorLeftToRight_MirrorsPixels()
    {
        var vm = Load(8, 4);
        vm.Pixels[0] = new Color(255, 0, 0, 255);
        vm.MirrorLeftToRight();
        Assert.Equal(vm.Pixels[0], vm.Pixels[7]);
    }

    [AvaloniaFact]
    public void MirrorTopToBottom_MirrorsPixels()
    {
        var vm = Load(8, 4);
        vm.Pixels[0] = new Color(255, 0, 0, 255);
        vm.MirrorTopToBottom();
        Assert.Equal(vm.Pixels[0], vm.Pixels[3 * 8]);
    }

    [AvaloniaFact]
    public void Clear_SetsAllPixelsNull()
    {
        var vm = Load(10, 1);
        for (var i = 0; i < vm.Pixels.Length; i++)
            vm.Pixels[i] = new Color(255, 0, 0, 255);
        vm.Clear();
        Assert.All(vm.Pixels, p => Assert.Null(p));
    }

    [AvaloniaFact]
    public void Undo_RestoresPreviousState()
    {
        var vm = Load(10, 1);
        vm.Pixels[0] = new Color(255, 0, 0, 255);
        vm.Clear();
        Assert.True(vm.CanUndo);
        vm.UndoCommand.Execute(null);
        Assert.NotNull(vm.Pixels[0]);
    }

    [AvaloniaFact]
    public void Redo_RestoresClearedState()
    {
        var vm = Load(10, 1);
        vm.Pixels[0] = new Color(255, 0, 0, 255);
        vm.Clear();
        vm.UndoCommand.Execute(null);
        Assert.True(vm.CanRedo);
        vm.RedoCommand.Execute(null);
        Assert.Null(vm.Pixels[0]);
    }

    [AvaloniaFact]
    public void ToBitmap_WithoutSkin_ReturnsNull()
    {
        var vm = new SkinEditorViewModel();
        Assert.Null(vm.ToBitmap());
    }

    [AvaloniaFact]
    public void ToggleEraser_TogglesIsEraser()
    {
        var vm = Load(64, 32);
        Assert.False(vm.IsEraser);
        vm.ToggleEraserCommand.Execute(null);
        Assert.True(vm.IsEraser);
        vm.ToggleEraserCommand.Execute(null);
        Assert.False(vm.IsEraser);
    }

    [AvaloniaFact]
    public void FillAtLast_WithoutPainting_UsesCenter()
    {
        var vm = Load(10, 10);
        vm.FillAtLast();
        Assert.NotNull(vm.Pixels[5 * 10 + 5]);
    }

    [AvaloniaFact]
    public void ToBitmap_WithPixels_ReturnsBitmap()
    {
        var vm = Load(64, 32);
        using var bmp = vm.ToBitmap();
        Assert.NotNull(bmp);
        Assert.Equal(64, bmp!.PixelSize.Width);
        Assert.Equal(32, bmp.PixelSize.Height);
    }

    private static SkinEditorViewModel Load(int width, int height)
    {
        var vm = new SkinEditorViewModel();
        using var bmp = CreateBitmap(width, height);
        vm.LoadSkin(bmp);
        return vm;
    }
}
