using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WMCL.App.Controls;
using WMCL.App.Features.Versions;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Minecraft;
using WMCL.UI.Controls;

namespace WMCL.App.Tests;

/// <summary>
/// 控件画廊生成器：逐个渲染自定义控件（能取到真实数据上下文的用真实数据），输出 PNG 供人工评审。
/// 运行：dotnet test tests/WMCL.App.Tests -c Release --filter ControlGalleryGenerator
/// 输出：screenshots/controls/*.png + manifest.txt（name|ok|note）
/// </summary>
[Trait("Kind", "Headless")]
public sealed class ControlGalleryGenerator
{
    private sealed class GalleryContext
    {
        public required MainWindowViewModel ViewModel { get; init; }
        public required Bitmap Skin { get; init; }
        public required string AssetsRoot { get; init; }
    }

    private sealed record Entry(string Name, Func<GalleryContext, Control> Create, string Note);

    private static readonly Entry[] Entries =
    [
        new("WmclFormRow", _ => new WmclFormRow { Content = new TextBlock { Text = "表单项内容" } }, "设置页表单行"),
        new("WmclCardHeader", _ => new WmclCardHeader
        {
            Title = "卡片标题",
            Subtitle = "副标题说明文字",
            Action = new Button { Content = "操作" }
        }, "卡片统一标题栏"),
        new("WmclEmptyState", _ => new WmclEmptyState
        {
            Title = "这里还没有内容",
            Description = "空状态说明文字",
            Action = new Button { Content = "去添加" }
        }, "空列表占位"),
        new("WmclSkeleton", _ => new WmclSkeleton { Rows = 3, RowHeight = 14, ShowThumbnail = true }, "加载骨架屏（已统一改用 PclLoadingIndicator，当前零引用）"),
        new("WmclStatusDot", _ => new WmclStatusDot { State = WmclStatusState.Ok, Text = "已就绪" }, "状态圆点 + 文本"),
        new("MySearchBox", _ => new MySearchBox { Watermark = "搜索…", Width = 260 }, "搜索框（TemplatedControl）"),
        new("VersionPicker", _ => new VersionPicker
        {
            Watermark = "全部版本",
            Width = 180,
            ItemsSource = new[] { "1.21.1", "1.20.1" },
            SelectedItem = "1.21.1"
        }, "版本下拉选择"),
        new("ResourceFavoritePicker", c => new ResourceFavoritePicker { Width = 220, DataContext = c.ViewModel.Mods.Favorite }, "收藏夹选择器"),
        new("MyHint", _ => new MyHint { Content = new TextBlock { Text = "提示内容" }, Level = HintLevel.Info }, "提示条（当前零引用）"),
        new("PclLoadingIndicator", _ => new PclLoadingIndicator { IsRunning = true, Progress = 60, Width = 140 }, "PCL 风格加载指示（所有加载态的统一样式）"),
        new("LoadingOverlay", _ => new LoadingOverlay
        {
            IsRunning = true,
            Width = 420,
            Height = 160
        }, "页面级加载遮罩：覆盖内容区 + 居中指示器"),
        new("MotdTextBlock", _ => new MotdTextBlock
        {
            Width = 300,
            Runs = [new MotdRun("服务器 ", Bold: true), new MotdRun("MOTD 富文本", "#55FFFF"), new MotdRun(" 已就绪", Italic: true)]
        }, "服务器 MOTD 富文本"),
        new("ResponsiveNewsPanel", _ =>
        {
            var panel = new ResponsiveNewsPanel { Width = 380, Height = 120 };
            panel.Children.Add(new Border { Background = Brushes.LightSteelBlue, Child = new TextBlock { Text = "新闻 1" } });
            panel.Children.Add(new Border { Background = Brushes.LightGreen, Child = new TextBlock { Text = "新闻 2" } });
            panel.Children.Add(new Border { Background = Brushes.Khaki, Child = new TextBlock { Text = "新闻 3" } });
            return panel;
        }, "响应式新闻网格"),
        new("LocalResourceIcon", c => new LocalResourceIcon
        {
            Width = 48,
            Height = 48,
            SourcePath = System.IO.Path.Combine(c.AssetsRoot, "Blocks", "Grass.png")
        }, "本地资源图标（Blocks/Grass.png）"),
        new("RemoteImage", _ => new RemoteImage { Width = 64, Height = 64 }, "远程图片（需 Url，网络图未加载）"),
        new("SkinFaceImage", c => new SkinFaceImage { Width = 64, Height = 64, Source = c.Skin }, "皮肤头像（Assets/Skins/Steve.png）"),
        new("SkinModelPreview", c => new SkinModelPreview
        {
            Width = 160,
            Height = 200,
            Skin = c.Skin,
            Variant = "classic",
            Yaw = 20
        }, "3D 皮肤预览"),
        new("SkinColorPalette", _ => new SkinColorPalette { Width = 220, Height = 160 }, "肤色/颜色调色板"),
        new("SkinPixelEditor", _ => new SkinPixelEditor { Width = 220, Height = 160 }, "像素编辑器（需 ViewModel）"),
        new("ExpanderReveal", _ => new ExpanderReveal { Child = new TextBlock { Text = "展开动画容器" } }, "展开动画装饰器（WMCL.UI）"),
        new("ComponentActionButton", c => new ComponentActionButton
        {
            Content = "↻",
            DataContext = c.ViewModel.Install.ForgeCard.Entries.FirstOrDefault()
        }, "组件行内操作按钮"),
        new("DownloadActionButton", c => new DownloadActionButton
        {
            Content = "下载",
            Show = true,
            IsModification = false,
            DataContext = c.ViewModel.Install.ForgeCard.Entries.FirstOrDefault()
        }, "下载操作按钮"),
        new("InstallComponentCard", c => new InstallComponentCard
        {
            Width = 320,
            DataContext = c.ViewModel.Install.ForgeCard
        }, "安装页组件卡片（真实卡片 VM）"),
        new("InstallComponentRow", c => new InstallComponentRow
        {
            Width = 320,
            DataContext = c.ViewModel.Install.ForgeCard.Entries.FirstOrDefault()
        }, "安装页组件行（真实条目 VM）"),
        new("InstallMinecraftCard", c => new InstallMinecraftCard
        {
            Width = 320,
            DataContext = c.ViewModel.Install
        }, "Minecraft 版本卡片（真实 InstallViewModel）"),
        new("InstalledVersionCard", _ => new InstalledVersionCard
        {
            Width = 360,
            DataContext = new VersionItem
            {
                Id = "1.21.1",
                Type = "release",
                ReleaseTime = "2024-08-08T00:00:00+00:00",
                IsInstalled = true,
                IsLatest = true
            }
        }, "已安装版本卡片（真实 VersionItem）"),
        new("VersionCatalogCard", _ => new VersionCatalogCard
        {
            Width = 360,
            DataContext = new VersionItem
            {
                Id = "1.21.1",
                Type = "release",
                ReleaseTime = "2024-08-08T00:00:00+00:00"
            }
        }, "版本目录卡片（真实 VersionItem）"),
        new("ResourceVersionGroupCard", _ => new ResourceVersionGroupCard { Width = 360, Height = 120 }, "资源版本分组卡片（需分组 VM，仅外壳）"),
        new("SkinSaturationField", _ => new SkinSaturationField
        {
            Width = 120,
            Height = 80,
            Hue = 200,
            Saturation = 0.8,
            Value = 0.9
        }, "HSV 饱和度字段（当前零引用）")
    ];

    [AvaloniaFact]
    public void CaptureAllControls()
    {
        var oldReduceMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        var oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var oldConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var dataDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wmcl-gallery-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", dataDir);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", dataDir);
        MainWindowViewModel? viewModel = null;
        var outDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outDir);
        var manifest = new List<string>();
        try
        {
            ThemeService.Apply("light");
            var repoRoot = FindRepoRoot();
            var settings = new AppSettings { GameDirectory = System.IO.Path.Combine(dataDir, "game") };
            viewModel = new MainWindowViewModel(new AppServices(settings, saveSettings: _ => { }));
            var context = new GalleryContext
            {
                ViewModel = viewModel,
                Skin = new Bitmap(System.IO.Path.Combine(repoRoot, "src", "WMCL.App", "Assets", "Skins", "Steve.png")),
                AssetsRoot = System.IO.Path.Combine(repoRoot, "src", "WMCL.App", "Assets")
            };

            foreach (var entry in Entries)
            {
                var window = new Window
                {
                    Width = 560,
                    Height = 260,
                    Background = Brushes.White,
                    Content = new Border
                    {
                        Background = (IBrush?)Application.Current!.Resources["WmclPageFlat"] ?? Brushes.White,
                        Padding = new Thickness(16),
                        Child = new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            Content = new StackPanel
                            {
                                Spacing = 8,
                                VerticalAlignment = VerticalAlignment.Top,
                                HorizontalAlignment = HorizontalAlignment.Left,
                                Children =
                                {
                                    new TextBlock { Text = entry.Name, FontSize = 13, Opacity = 0.6 },
                                    new Border
                                    {
                                        HorizontalAlignment = HorizontalAlignment.Left,
                                        Child = Build(() => entry.Create(context))
                                    }
                                }
                            }
                        }
                    }
                };
                try
                {
                    window.Show();
                    Settle();
                    var frame = window.CaptureRenderedFrame();
                    if (frame is null) { manifest.Add($"{entry.Name}|false|截图失败"); continue; }
                    frame.Save(System.IO.Path.Combine(outDir, entry.Name + ".png"), PngBitmapEncoderOptions.Default);
                    manifest.Add($"{entry.Name}|true|{entry.Note}");
                }
                catch (Exception ex)
                {
                    manifest.Add($"{entry.Name}|false|渲染异常：{ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    window.Close();
                }
            }
        }
        finally
        {
            viewModel?.Dispose();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", oldReduceMotion);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldConfig);
            File.WriteAllLines(System.IO.Path.Combine(outDir, "manifest.txt"), manifest);
            try { Directory.Delete(dataDir, true); } catch { /* 临目录清理失败不影响生成 */ }
        }
    }

    private static Control Build(Func<Control> create)
    {
        try { return create(); }
        catch (Exception ex)
        {
            return new TextBlock { Text = "构造失败：" + ex.Message, Foreground = Brushes.IndianRed };
        }
    }

    private static void Settle()
    {
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static string ResolveOutputDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("WMCL_GALLERY_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return System.IO.Path.Combine(FindRepoRoot(), "screenshots", "controls");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "WMCL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
