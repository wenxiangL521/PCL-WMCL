using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class SkinEditorWindow : Window
{
    private Bitmap? _preview;
    private DispatcherTimer? _previewTimer;
    private bool _closed;
    private bool _syncingBrush;
    public SkinEditorViewModel Editor { get; } = new();

    public SkinEditorWindow()
    {
        InitializeComponent();
        DataContext = Editor;
        Editor.SelectedColor = Color.FromRgb(85, 85, 85);
        SyncBrushControls();
        Editor.PropertyChanged += OnEditorChanged;
        BrushPalette.PropertyChanged += (_, args) =>
        {
            if (args.Property != Controls.SkinColorPalette.SelectedColorProperty || _syncingBrush) return;
            Editor.IsEraser = false;
            Editor.SelectedColor = BrushPalette.SelectedColor;
        };
        PaletteButton.Flyout!.Closed += (_, _) => BrushPalette.RememberColor(Editor.SelectedColor);
        LargePixelCanvas.ColorPicked += (_, args) =>
        {
            BrushPalette.RememberColor(args.Color);
            SyncBrushControls();
            EditorActionStatus.Text = "已从画布吸色，像素未修改。";
        };
        LargePixelCanvas.PropertyChanged += (_, args) =>
        {
            if (args.Property == Controls.SkinPixelEditor.ZoomProperty) UpdateZoomLabel();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            Editor.PropertyChanged -= OnEditorChanged;
            _previewTimer?.Stop();
            EditModelPreview.Skin = null;
            _preview?.Dispose();
        };
        UpdateZoomLabel();
    }

    public SkinEditorWindow(Bitmap source, string variant) : this()
    {
        Editor.LoadSkin(source);
        UpdateZoomLabel();
        EditModelPreview.Variant = variant;
        UpdatePreview();
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        if (args.PropertyName == nameof(SkinEditorViewModel.SelectedColor)) { SyncBrushControls(); return; }
        if (args.PropertyName != nameof(SkinEditorViewModel.PixelsVersion) || _previewTimer is not null) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _previewTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _previewTimer = null;
            if (!_closed) UpdatePreview();
        };
        timer.Start();
    }

    private void UpdatePreview()
    {
        var bitmap = Editor.ToBitmap();
        EditModelPreview.Skin = null;
        _preview?.Dispose();
        _preview = bitmap;
        EditModelPreview.Skin = bitmap;
    }
    private void UpdateZoomLabel() => ZoomLabel.Text = $"缩放 {LargePixelCanvas.Zoom:0.##}× · {Editor.Width}×{Editor.Height} 像素";
    private void OnBrushSize(object? sender, SelectionChangedEventArgs e)
    {
        if (BrushSizeBox is null) return;
        Editor.BrushSize = Math.Clamp(BrushSizeBox.SelectedIndex + 1, 1, 4);
    }
    private void SyncBrushControls()
    {
        _syncingBrush = true;
        BrushPalette.SelectedColor = Editor.SelectedColor;
        ColorBox.Text = Editor.SelectedColor.ToString();
        CurrentBrushSwatch.Background = new SolidColorBrush(Editor.SelectedColor);
        _syncingBrush = false;
    }

    private void OnColorChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingBrush) return;
        if (ColorBox is not null && Color.TryParse(ColorBox.Text, out var color)) Editor.SelectedColor = color;
    }
    private void OnFill(object? sender, RoutedEventArgs e) => Editor.FillAtLast();
    private void OnMirrorH(object? sender, RoutedEventArgs e) => Editor.MirrorLeftToRight();
    private void OnMirrorV(object? sender, RoutedEventArgs e) => Editor.MirrorTopToBottom();
    private void OnClear(object? sender, RoutedEventArgs e) => Editor.Clear();
    private void OnZoomIn(object? sender, RoutedEventArgs e) => LargePixelCanvas.ZoomAt(new Point(LargePixelCanvas.Bounds.Width / 2, LargePixelCanvas.Bounds.Height / 2), 1.5);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => LargePixelCanvas.ZoomAt(new Point(LargePixelCanvas.Bounds.Width / 2, LargePixelCanvas.Bounds.Height / 2), 1 / 1.5);
    private void OnFit(object? sender, RoutedEventArgs e) { LargePixelCanvas.ResetView(); UpdateZoomLabel(); }
    private void OnFront(object? sender, RoutedEventArgs e) { EditModelPreview.ResetView(); EditModelPreview.Yaw = 0; }
    private void OnBack(object? sender, RoutedEventArgs e) { EditModelPreview.ResetView(); EditModelPreview.Yaw = 180; }
    private void OnMaximize(object? sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnAccept(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!StorageProvider.CanSave) throw new InvalidOperationException("系统保存选择器不可用，可先应用到预览后再上传。");
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存编辑后的皮肤",
                SuggestedFileName = "edited-skin.png",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
            });
            if (file is null || _closed) return;
            using var bitmap = Editor.ToBitmap();
            if (bitmap is null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            EditorActionStatus.Text = "已保存PNG，账号皮肤未更改。";
        }
        catch (Exception exception) { if (!_closed) EditorActionStatus.Text = "保存失败：" + exception.Message; }
    }
}
