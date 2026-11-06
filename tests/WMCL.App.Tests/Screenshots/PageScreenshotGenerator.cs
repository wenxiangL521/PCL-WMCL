using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using Xunit;
using Avalonia.Media.Imaging;

namespace WMCL.App.Tests;

/// <summary>
/// 页面截图生成器（L1 人工验证的自动化替代）：用真实 headless 渲染逐页截帧，
/// 输出 PNG 供人工/多模态评审与 PCL2 对比。运行方式：
/// dotnet test tests/WMCL.App.Tests/WMCL.App.Tests.csproj -c Release --filter PageScreenshotGenerator
/// </summary>
[Trait("Kind", "Headless")]
public sealed class PageScreenshotGenerator
{
    private readonly ITestOutputHelper _output;

    public PageScreenshotGenerator(ITestOutputHelper output) => _output = output;

    private static readonly string[] PageKeys =
    [
        "launch", "download", "cleanroom", "labymod", "mods", "modpack", "favorites",
        "content-resource", "content-shader", "content-world", "content-datapack",
        "instance", "settings", "link", "more", "about", "help", "update",
        "logs", "toolbox", "feedback", "gamemanage"
    ];

    [AvaloniaFact]
    public void CaptureAllPages()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "wmcl-shots-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var oldXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", dataDir);
        // 页面切换动画基于真实时间（Stopwatch）驱动，headless 快速循环会截到混合帧；归零动画保证截帧干净。
        var oldReduceMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var outDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outDir);
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                ThemeService.Apply(theme);
                var settings = new AppSettings { GameDirectory = Path.Combine(dataDir, "game") };
                var viewModel = new MainWindowViewModel(settings);
                var window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 850,
                    Height = 500
                };
                window.Show();
                Settle();

                foreach (var key in PageKeys)
                {
                    viewModel.NavigateTo(key);
                    // 页面切换遮罩收起（本页加载完成）后再截帧，避免截到加载态。
                    var enterStart = Environment.TickCount64;
                    while (viewModel.IsPageEntering && Environment.TickCount64 - enterStart < 5000)
                    {
                        Thread.Sleep(10);
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    }
                    // 页面切换后充分推进渲染与异步加载，避免捕获上一页残留合成帧。
                    Settle();
                    var frame = window.CaptureRenderedFrame();
                    var path = Path.Combine(outDir, $"{theme}-{key}.png");
                    if (frame is null)
                    {
                        _output.WriteLine("NULL " + path);
                        continue;
                    }
                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    _output.WriteLine("OK " + path);

                    // 滚动到底部再补一帧，覆盖长列表页面的底部区域。
                    var scrolled = false;
                    foreach (var scrollViewer in window.GetVisualDescendants().OfType<ScrollViewer>())
                    {
                        // Avalonia 12 下 Extent/Viewport 在捕获时可能尚未更新；用 ScrollBarMaximum 兜底，
                        // 两者都表明没有可滚动内容时才跳过。
                        if (scrollViewer.Extent.Height <= scrollViewer.Viewport.Height &&
                            scrollViewer.ScrollBarMaximum.Y <= 0.5) continue;
                        scrollViewer.Offset = new Vector(scrollViewer.Offset.X,
                            scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                        scrolled = true;
                    }
                    if (!scrolled) continue;
                    Settle();
                    var scrolledFrame = window.CaptureRenderedFrame();
                    if (scrolledFrame is null) continue;
                    var scrolledPath = Path.Combine(outDir, $"{theme}-{key}-scrolled.png");
                    scrolledFrame.Save(scrolledPath, PngBitmapEncoderOptions.Default);
                    _output.WriteLine("OK " + scrolledPath);
                }

                window.Close();
                viewModel.Dispose();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldXdg);
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", oldReduceMotion);
            try
            {
                Directory.Delete(dataDir, true);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 输出目录：WMCL_SHOT_DIR 环境变量优先，未设置时落到仓库根目录的 screenshots/。
    /// 该目录纳入版本管理：修改界面后需重新生成并提交 PNG，保证 GitHub 上看到的截图与代码一致。
    /// </summary>
    private static string ResolveOutputDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("WMCL_SHOT_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WMCL.slnx")))
            dir = dir.Parent;
        var repoRoot = dir?.FullName ?? Directory.GetCurrentDirectory();
        return Path.Combine(repoRoot, "screenshots");
    }

    /// <summary>推进 UI 队列与渲染帧（含真实时间等待），给异步加载与布局留出时间。</summary>
    private static void Settle()
    {
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }
}
