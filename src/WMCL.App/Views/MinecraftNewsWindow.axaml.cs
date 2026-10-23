using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.Services;

namespace WMCL.App.Views;

public partial class MinecraftNewsWindow : Window
{
    private readonly MinecraftNewsService _service;
    private readonly ObservableCollection<MinecraftNewsItem> _items = [];
    private CancellationTokenSource? _cts;
    private int _page = 1;

    public MinecraftNewsWindow() : this(null)
    {
    }

    public MinecraftNewsWindow(MinecraftNewsService? service)
    {
        InitializeComponent();
        _service = service ?? new MinecraftNewsService();
        NewsItemsControl.ItemsSource = _items;
        Opened += async (_, _) =>
        {
            _page = 1;
            await LoadAsync();
        };
        Closed += (_, _) =>
        {
            Interlocked.Exchange(ref _cts, null)?.Cancel();
        };
    }

    private async Task LoadAsync()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _cts, cts)?.Cancel();
        StatusText.Text = "正在加载 Minecraft 新闻…";
        try
        {
            var items = await _service.GetAsync(_page, cts.Token);
            if (_page == 1) _items.Clear();
            foreach (var item in items) _items.Add(item);
            EmptyState.IsVisible = _items.Count == 0;
            LoadMoreButton.IsVisible = items.Count >= 24;
            StatusText.Text = _items.Count == 0 ? "" : $"第 {_page} 页 · 共 {_items.Count} 条新闻。";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _items.Clear();
            EmptyState.IsVisible = true;
            StatusText.Text = "加载新闻失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _cts, null, cts), cts))
                cts.Dispose();
        }
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        _page = 1;
        await LoadAsync();
    }

    private async void OnLoadMore(object? sender, RoutedEventArgs e)
    {
        if (_cts is { IsCancellationRequested: false }) return;
        _page++;
        await LoadAsync();
    }

    private void OnCardClick(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (sender is Control { DataContext: MinecraftNewsItem item })
            OpenNews(item);
    }

    private void OnReadMore(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MinecraftNewsItem item })
            OpenNews(item);
    }

    private void OpenNews(MinecraftNewsItem item)
    {
        if (!MinecraftNewsService.IsSafeNewsLink(item.Url))
        {
            StatusText.Text = "已拦截非官方新闻链接：" + item.Url;
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开新闻失败：" + ex.Message;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
