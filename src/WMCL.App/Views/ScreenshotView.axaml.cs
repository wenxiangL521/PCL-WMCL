using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using WMCL.App.Services;

namespace WMCL.App.Views;

public partial class ScreenshotView : UserControl
{
    private string _directory = "";
    private Bitmap? _preview;

    public ScreenshotView()
    {
        InitializeComponent();
    }

    public void Load(string instanceDirectory)
    {
        _directory = Path.Combine(instanceDirectory, "screenshots");
        Refresh();
    }

    private void Refresh()
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".webp"
        };
        var entries = Directory.Exists(_directory)
            ? Directory.EnumerateFiles(_directory)
                .Where(path => extensions.Contains(Path.GetExtension(path)))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => new ScreenshotEntry(Path.GetFileName(path), path))
                .ToList()
            : [];
        ShotList.ItemsSource = entries;
        StatusText.Text = entries.Count == 0
            ? $"目录为空：{_directory}"
            : $"共 {entries.Count} 张截图 · {_directory}";
        ShotList.SelectedItem = entries.FirstOrDefault();
        UpdatePreview();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        _preview?.Dispose();
        _preview = null;
        PreviewImage.Source = null;
        EmptyState.IsVisible = true;
        if (ShotList.SelectedItem is not ScreenshotEntry entry) return;
        try
        {
            _preview = new Bitmap(entry.Path);
            PreviewImage.Source = _preview;
            EmptyState.IsVisible = false;
        }
        catch
        {
            StatusText.Text = "无法加载截图：" + entry.Name;
        }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_directory);
        OpenPath(_directory);
    }

    private void OnOpenImage(object? sender, RoutedEventArgs e)
    {
        if (ShotList.SelectedItem is ScreenshotEntry entry) OpenPath(entry.Path);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (ShotList.SelectedItem is not ScreenshotEntry entry) return;
        if (!await DialogService.ConfirmAsync(
                "删除截图",
                $"确定删除 {entry.Name}？" + Environment.NewLine + "此操作不可恢复。"))
            return;
        try
        {
            File.Delete(entry.Path);
            Refresh();
            StatusText.Text = $"已删除 {entry.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "删除失败：" + ex.Message;
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // 打开失败不阻塞页面
        }
    }
}
