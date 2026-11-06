using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ResponsiveNewsLayoutTests
{
    [AvaloniaTheory]
    [InlineData("home", 420)]
    [InlineData("home", 620)]
    [InlineData("home", 900)]
    [InlineData("home", 1200)]
    [InlineData("window", 420)]
    [InlineData("window", 620)]
    [InlineData("window", 900)]
    [InlineData("window", 1200)]
    public async Task CardsStretchWithinViewportWithWrappedTitlesAndSixteenByNineImages(string surface, double width)
    {
        using var fixture = new NewsFixture();
        var window = CreateWindow(surface, width, fixture);
        try
        {
            window.Show();
            await PumpAsync();
            AssertLayout(window, surface == "home" ? 3 : 24);
            Capture(window, $"{surface}-light-{width}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("home")]
    [InlineData("window")]
    public async Task SameOpenSurfaceReflowsOnResizeInBothDirections(string surface)
    {
        using var fixture = new NewsFixture();
        var previous = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        var window = CreateWindow(surface, 420, fixture);
        try
        {
            window.Show();
            foreach (var width in new[] { 420, 620, 900, 1200, 900, 620, 420 })
            {
                window.Width = width;
                await PumpAsync();
                AssertLayout(window, surface == "home" ? 3 : 24);
                Capture(window, $"{surface}-dark-resize-{width}");
            }
            Assert.Equal(surface == "home" ? 0 : 1, fixture.Pages.Count);
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(0, 1)]
    [InlineData(180, 1)]
    [InlineData(259, 1)]
    [InlineData(260, 1)]
    [InlineData(529.9, 1)]
    [InlineData(530, 2)]
    [InlineData(799.9, 2)]
    [InlineData(800, 3)]
    [InlineData(1069.9, 3)]
    [InlineData(1070, 4)]
    public void PanelUsesExactColumnBoundariesWithoutEnforcingMinimumWidth(double width, int columns)
    {
        var panel = new ResponsiveNewsPanel { UseLayoutRounding = false };
        for (var index = 0; index < 7; index++)
            panel.Children.Add(new TextBlock
            {
                Text = string.Join(" ", Enumerable.Repeat("wrapped news title", index + 1)),
                TextWrapping = TextWrapping.Wrap,
                UseLayoutRounding = false
            });
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        Assert.Equal(columns, panel.Children.Count(child => child.Bounds.Y == 0));
        var expectedWidth = Math.Max(0, (width - (columns - 1) * 10) / columns);
        foreach (var child in panel.Children)
        {
            Assert.Equal(expectedWidth, child.Bounds.Width, 5);
            Assert.InRange(child.Bounds.Left, 0, width);
            Assert.True(child.Bounds.Right <= width + .001);
        }
        foreach (var row in panel.Children.GroupBy(child => child.Bounds.Y))
            Assert.Single(row.Select(child => child.Bounds.Height).Distinct());
    }

    [AvaloniaFact]
    public void PanelHandlesEmptyHiddenAndUnboundedContentAndPropertyChanges()
    {
        var panel = new ResponsiveNewsPanel { UseLayoutRounding = false };
        panel.Measure(Size.Infinity);
        Assert.Equal(default, panel.DesiredSize);
        var hidden = new Border { IsVisible = false, Width = 2000, Height = 2000 };
        panel.Children.Add(hidden);
        panel.Children.Add(new Border { Height = 30 });
        panel.Children.Add(new Border { Height = 60 });
        panel.Measure(Size.Infinity);
        Assert.Equal(new Size(260, 100), panel.DesiredSize);
        panel.Measure(new Size(1000, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 1000, panel.DesiredSize.Height));
        Assert.Equal(495, panel.Children[1].Bounds.Width);
        panel.MinItemWidth = 600;
        panel.RowSpacing = 20;
        panel.ColumnSpacing = 0;
        panel.Measure(new Size(1000, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 1000, panel.DesiredSize.Height));
        Assert.Equal(1000, panel.Children[1].Bounds.Width);
        Assert.Equal(50, panel.Children[2].Bounds.Top);
        Assert.Equal(110, panel.DesiredSize.Height);
    }

    [AvaloniaFact]
    public async Task WindowPreservesRefreshPagingCardAndReadMoreWithoutOpeningBrowser()
    {
        using var fixture = new NewsFixture();
        var opened = new List<string>();
        var window = new MinecraftNewsWindow(fixture.Service, opened.Add) { Width = 620, Height = 800 };
        try
        {
            window.Show();
            await PumpAsync();
            Assert.Equal(new[] { 1 }, fixture.Pages);
            var items = window.FindControl<ItemsControl>("NewsItemsControl")!;
            Assert.Equal(24, items.ItemCount);
            var card = window.GetVisualDescendants().OfType<Border>().First(control => control.Classes.Contains("newsCard"));
            var title = card.GetVisualDescendants().OfType<TextBlock>().First(control => control.Classes.Contains("newsTitle"));
            Click(window, title);
            await PumpAsync();
            Assert.Equal(new[] { fixture.Items[0].Url }, opened);
            var readMore = card.GetVisualDescendants().OfType<Button>().Single();
            readMore.BringIntoView();
            await PumpAsync();
            Click(window, readMore);
            await PumpAsync();
            Assert.Equal(2, opened.Count);
            Assert.All(opened, url => Assert.Equal(fixture.Items[0].Url, url));
            var more = window.FindControl<Button>("LoadMoreButton")!;
            Assert.True(more.IsVisible);
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpAsync();
            Assert.Equal(new[] { 1, 2 }, fixture.Pages);
            Assert.Equal(26, items.ItemCount);
            Assert.False(more.IsVisible);
            Assert.Contains("第 2 页", window.FindControl<TextBlock>("StatusText")!.Text);
            var refresh = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "刷新"));
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpAsync();
            Assert.Equal(new[] { 1, 2, 1 }, fixture.Pages);
            Assert.Equal(24, items.ItemCount);
            Assert.True(more.IsVisible);
            Assert.Contains("第 1 页", window.FindControl<TextBlock>("StatusText")!.Text);
            AssertLayout(window, 24);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task HomeRefreshBindingRemainsUsableAtNarrowWidth()
    {
        using var fixture = new NewsFixture();
        var model = new FakeHomeModel(fixture.Items.Take(3));
        var window = CreateHomeWindow(420, model);
        try
        {
            window.Show();
            await PumpAsync();
            var refresh = window.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, "刷新"));
            Click(window, refresh);
            await PumpAsync();
            Assert.Equal(1, model.RefreshCount);
            Assert.Empty(fixture.Pages);
            AssertLayout(window, 3);
        }
        finally { window.Close(); }
    }

    private static Window CreateWindow(string surface, double width, NewsFixture fixture) => surface == "home"
        ? CreateHomeWindow(width, new FakeHomeModel(fixture.Items.Take(3)))
        : new MinecraftNewsWindow(fixture.Service, _ => throw new InvalidOperationException("Unexpected browser action"))
        { Width = width, Height = 800 };

    private static Window CreateHomeWindow(double width, FakeHomeModel model)
    {
        var view = new LaunchView { DataContext = model };
        var card = view.FindControl<Border>("HomeNewsCard")!;
        Assert.IsType<StackPanel>(card.Parent).Children.Remove(card);
        card.DataContext = model;
        return new Window
        {
            Width = width,
            Height = 800,
            Content = new ScrollViewer { Content = card, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
        };
    }

    private static void AssertLayout(Window window, int count)
    {
        var panel = Assert.Single(window.GetVisualDescendants().OfType<ResponsiveNewsPanel>());
        var children = panel.Children.Where(child => child.IsVisible).ToArray();
        Assert.Equal(count, children.Length);
        var columns = Math.Max(1, Math.Min(count, (int)Math.Floor((panel.Bounds.Width + panel.ColumnSpacing) /
            (panel.MinItemWidth + panel.ColumnSpacing))));
        var expectedWidth = (panel.Bounds.Width - panel.ColumnSpacing * (columns - 1)) / columns;
        Assert.Equal(columns, children.Count(child => Math.Abs(child.Bounds.Y) < .1));
        foreach (var child in children)
        {
            Assert.InRange(child.Bounds.Width, expectedWidth - 1.1, expectedWidth + 1.1);
            Assert.True(child.Bounds.Left >= -.01);
            Assert.True(child.Bounds.Right <= panel.Bounds.Width + 1.1);
            var card = Assert.Single(child.GetVisualDescendants().OfType<Border>(), border =>
                border.Classes.Contains("newsCard") || border.Classes.Contains("homeNewsItem"));
            Assert.InRange(card.Bounds.Width, child.Bounds.Width - 1.1, child.Bounds.Width + 1.1);
            var title = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("newsTitle"));
            Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
            Assert.Equal(TextTrimming.None, title.TextTrimming);
            Assert.True(double.IsPositiveInfinity(title.MaxHeight));
            Assert.Equal(title.Text, ToolTip.GetTip(title));
            Assert.True(title.Bounds.Height > 20);
            var imageFrame = Assert.Single(card.GetVisualDescendants().OfType<Viewbox>());
            if (!imageFrame.IsVisible) continue;
            var image = Assert.Single(imageFrame.GetVisualDescendants().OfType<RemoteImage>());
            Assert.NotNull(image.Source);
            Assert.Equal(Stretch.UniformToFill, image.Stretch);
            Assert.InRange(imageFrame.Bounds.Width / imageFrame.Bounds.Height, 1.76, 1.80);
            if (card.Classes.Contains("homeNewsItem"))
            {
                Assert.InRange(imageFrame.Bounds.Width, 72, 112);
                var imageRight = imageFrame.TranslatePoint(new Point(imageFrame.Bounds.Width, 0), card)!.Value.X;
                var titleLeft = title.TranslatePoint(default, card)!.Value.X;
                Assert.True(titleLeft >= imageRight + 7);
            }
            else Assert.True(imageFrame.Bounds.Width > 100);
            Assert.True(imageFrame.Bounds.Right <= card.Bounds.Width + 1.1);
        }
        foreach (var row in children.GroupBy(child => Math.Round(child.Bounds.Y)))
        {
            var last = row.Last();
            if (row.Count() == columns)
                Assert.InRange(last.Bounds.Right, panel.Bounds.Width - 1.1, panel.Bounds.Width + 1.1);
            Assert.True(row.Max(child => child.Bounds.Height) - row.Min(child => child.Bounds.Height) <= 1.1);
        }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
        input.GetMethod("MouseMove")!.Invoke(window.PlatformImpl, [point, RawInputModifiers.None]);
        input.GetMethod("MouseDown")!.Invoke(window.PlatformImpl, [point, MouseButton.Left, RawInputModifiers.None]);
        input.GetMethod("MouseUp")!.Invoke(window.PlatformImpl, [point, MouseButton.Left, RawInputModifiers.None]);
    }

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 5; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_NEWS_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    public sealed class FakeHomeModel
    {
        public FakeHomeModel(IEnumerable<MinecraftNewsItem> items)
        {
            HomeNews = new ObservableCollection<MinecraftNewsItem>(items);
            HomeNewsDisplay = HomeNews;
            LoadHomeNewsCommand = new RelayCommand(() => RefreshCount++);
        }

        public ObservableCollection<MinecraftNewsItem> HomeNews { get; }
        public ObservableCollection<MinecraftNewsItem> HomeNewsDisplay { get; }
        public bool HasHomeNews => HomeNews.Count > 0;
        public bool IsHomeNewsLoading => false;
        public string HomeNewsStatus => "离线测试新闻";
        public ICommand LoadHomeNewsCommand { get; }
        public int RefreshCount { get; private set; }
    }

    private sealed class NewsFixture : HttpMessageHandler
    {
        private readonly string _imageUrl = "https://wmcl-news.invalid/" + Guid.NewGuid().ToString("N") + ".png";
        private readonly ConcurrentDictionary<string, byte[]> _imageCache;
        private readonly HttpClient _http;

        public NewsFixture()
        {
            _imageCache = (ConcurrentDictionary<string, byte[]>)typeof(RemoteImage)
                .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            using var stream = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            _imageCache[_imageUrl] = bytes.ToArray();
            _http = new HttpClient(this, disposeHandler: false);
            Service = new MinecraftNewsService(_http);
            Items = Enumerable.Range(1, 24).Select(index => new MinecraftNewsItem(
                $"Minecraft 新闻 {index}：探索全新世界与冒险更新 Long headline that must wrap completely without truncation",
                "https://www.minecraft.net/article/news-" + index,
                "离线模拟新闻描述，不进行真实 HTTP 请求。 Test description for adaptive news cards.",
                "Mojang Studios", index % 3 == 0 ? null : _imageUrl, "离线图片", 1700000000)).ToArray();
        }

        public MinecraftNewsService Service { get; }
        public MinecraftNewsItem[] Items { get; }
        public List<int> Pages { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("net-secondary.web.minecraft-services.net", request.RequestUri!.Host);
            var pageText = request.RequestUri.Query.Split('&').Single(part => part.StartsWith("page="));
            var page = int.Parse(pageText[5..]);
            Pages.Add(page);
            var selected = page == 1 ? Items : Items.Take(2).ToArray();
            var payload = new
            {
                result = new
                {
                    results = selected.Select(item => new
                    {
                        title = item.Title,
                        url = item.Url,
                        description = item.Description,
                        author = item.Author,
                        image = item.Image,
                        imageAltText = item.ImageAltText,
                        time = item.Time
                    })
                }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _http.Dispose();
                _imageCache.TryRemove(_imageUrl, out _);
            }
            base.Dispose(disposing);
        }
    }
}
