using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class ScreenshotViewTests
{
    [AvaloniaFact]
    public void ListsAndPreviewsScreenshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-shot-view-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "screenshots");
        Directory.CreateDirectory(directory);
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(Path.Combine(directory, "shot.png"), png);
        try
        {
            var view = new ScreenshotView();
            var window = new Window { Content = view, Width = 800, Height = 500 };
            window.Show();
            try
            {
                view.Load(root);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, view.FindControl<ListBox>("ShotList")!.ItemCount);
                Assert.Contains("共 1 张截图", view.FindControl<TextBlock>("StatusText")!.Text);
                Assert.NotNull(view.FindControl<Image>("PreviewImage")!.Source);
            }
            finally { window.Close(); }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
