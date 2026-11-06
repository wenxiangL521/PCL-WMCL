using System.Net.Http;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WMCL.App.Controls;

public sealed class RemoteImage : Image
{
    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(Url));

    private CancellationTokenSource? _loadCts;

    static RemoteImage() =>
        UrlProperty.Changed.AddClassHandler<RemoteImage>((image, _) => image.LoadAsync());

    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Interlocked.Exchange(ref _loadCts, null)?.Cancel();
    }

    private void LoadAsync() => _ = LoadAsyncCore();

    private async Task LoadAsyncCore()
    {
        var url = Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            Source = null;
            return;
        }
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _loadCts, cts)?.Cancel();
        try
        {
            var bytes = await WMCL.App.Services.RemoteImageCache.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (bytes is null) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!string.Equals(Url, url, StringComparison.Ordinal)) return;
                using var stream = new MemoryStream(bytes, writable: false);
                Source = new Bitmap(stream);
            });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.Equals(Url, url, StringComparison.Ordinal)) Source = null;
            });
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, cts), cts))
                cts.Dispose();
        }
    }
}
