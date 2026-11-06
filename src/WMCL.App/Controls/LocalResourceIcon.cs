using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;

namespace WMCL.App.Controls;

public sealed class LocalResourceIcon : Image
{
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<LocalResourceIcon, string?>(nameof(SourcePath));
    public static readonly StyledProperty<LocalResourceKind> KindProperty =
        AvaloniaProperty.Register<LocalResourceIcon, LocalResourceKind>(nameof(Kind));
    private CancellationTokenSource? _load;
    private Bitmap? _ownedBitmap;
    private bool _attached;
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public LocalResourceKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public LocalResourceIcon()
    {
        Width = 24;
        Height = 24;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
    }

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourcePathProperty || change.Property == KindProperty)
        {
            if (_attached) BeginLoad();
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs args)
    {
        if (_attached && _ownedBitmap is null) ShowFallback();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        _attached = true;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        BeginLoad();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        _attached = false;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        CancelLoad();
        Source = null;
        _ownedBitmap?.Dispose();
        _ownedBitmap = null;
        base.OnDetachedFromVisualTree(args);
    }

    private void CancelLoad()
    {
        var previous = _load;
        _load = null;
        previous?.Cancel();
    }

    private void BeginLoad()
    {
        CancelLoad();
        Source = null;
        _ownedBitmap?.Dispose();
        _ownedBitmap = null;
        ShowFallback();
        var cancellation = new CancellationTokenSource();
        _load = cancellation;
        PendingLoad = LoadAsync(SourcePath, Kind, cancellation);
    }

    private async Task LoadAsync(string? path, LocalResourceKind kind, CancellationTokenSource cancellation)
    {
        try
        {
            var bytes = await LocalResourceIconService.LoadAsync(path, kind, cancellation.Token).ConfigureAwait(false);
            if (bytes is null || cancellation.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_attached || !ReferenceEquals(_load, cancellation) || cancellation.IsCancellationRequested) return;
                try
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
                    var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
                    _ownedBitmap = width >= height
                        ? Bitmap.DecodeToWidth(stream, Math.Min(24, width))
                        : Bitmap.DecodeToHeight(stream, Math.Min(24, height));
                    Source = _ownedBitmap;
                }
                catch
                {
                    _ownedBitmap?.Dispose();
                    _ownedBitmap = null;
                    ShowFallback();
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_load, cancellation)) _load = null;
                cancellation.Dispose();
            });
        }
    }

    private void ShowFallback()
    {
        var key = Kind switch
        {
            LocalResourceKind.Mod => "WmclIconMod",
            LocalResourceKind.ResourcePack => "WmclIconInbox",
            LocalResourceKind.ShaderPack => "WmclIconStar",
            LocalResourceKind.World => "WmclIconFolderOpen",
            LocalResourceKind.Folder => "WmclIconFolderClosed",
            LocalResourceKind.File => "WmclIconFile",
            _ => "WmclIconRotateCcw"
        };
        this.TryFindResource(key, out var geometry);
        this.TryFindResource("WmclBlue3", out var brush);
        Source = new DrawingImage
        {
            Drawing = new GeometryDrawing
            {
                Geometry = geometry as Geometry ?? Geometry.Parse("M2,2 L22,2 L22,22 L2,22 Z M6,6 L6,18 L18,18 L18,6 Z"),
                Brush = brush as IBrush ?? Brushes.SlateGray
            }
        };
    }
}
