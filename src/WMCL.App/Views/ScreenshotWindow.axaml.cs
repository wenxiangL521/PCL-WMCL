using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using WMCL.App.Animation;
using WMCL.App.Services;

namespace WMCL.App.Views;

public sealed record ScreenshotEntry(string Name, string Path)
{
    public string SizeText
    {
        get
        {
            try
            {
                var size = new FileInfo(Path).Length;
                return size >= 1024 * 1024
                    ? $"{size / 1024.0 / 1024.0:0.0} MB"
                    : $"{size / 1024.0:0.0} KB";
            }
            catch
            {
                return "";
            }
        }
    }
}

public partial class ScreenshotWindow : Window
{
    private readonly string _directory;
    private Bitmap? _preview;

    public ScreenshotWindow()
    {
        InitializeComponent();
        _directory = "";
    }

    public ScreenshotWindow(string instanceDirectory) : this()
    {
        _directory = System.IO.Path.Combine(instanceDirectory, "screenshots");
        Opened += async (_, _) =>
        {
            await ModalMotion.AnimateInAsync(this);
            Refresh();
        };
    }

    private void Refresh()
    {
        var entries = new List<ScreenshotEntry>();
        if (Directory.Exists(_directory))
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".webp"
            };
            entries = Directory.EnumerateFiles(_directory)
                .Where(path => extensions.Contains(System.IO.Path.GetExtension(path)))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => new ScreenshotEntry(System.IO.Path.GetFileName(path), path))
                .ToList();
        }

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
        if (ShotList.SelectedItem is ScreenshotEntry entry)
            OpenPath(entry.Path);
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

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private static void OpenPath(string path)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? path : "xdg-open",
                Arguments = OperatingSystem.IsWindows() ? "" : path,
                UseShellExecute = OperatingSystem.IsWindows()
            };
            Process.Start(start);
        }
        catch
        {
            // 打开失败不阻塞窗口
        }
    }
}
