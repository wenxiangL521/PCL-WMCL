using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class MinecraftNewsServiceTests
{
    [Fact]
    public async Task ParsesOfficialNewsFieldsAndDecodesHtml()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"result":{"results":[
                  {"title":"A &amp; B","url":"https://www.minecraft.net/article","description":"Line &lt;one&gt;","author":"Mojang","image":"https://launchercontent.mojang.com/a.png","imageAltText":"alt","time":1700000000}
                ]}}
                """, Encoding.UTF8, "application/json")
        });
        var service = new MinecraftNewsService(new HttpClient(handler));
        var items = await service.GetAsync();
        var item = Assert.Single(items);
        Assert.Equal("A & B", item.Title);
        Assert.Equal("Line <one>", item.Description);
        Assert.Equal("Mojang", item.Author);
        Assert.True(item.HasImage);
        Assert.NotEqual("", item.PublishDateText);
    }

    [Theory]
    [InlineData("https://www.minecraft.net/article", true)]
    [InlineData("https://launchercontent.mojang.com/a.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://minecraft.net.evil.example/", false)]
    public void OnlyOfficialNewsHostsAreAllowed(string url, bool allowed)
    {
        Assert.Equal(allowed, MinecraftNewsService.IsSafeNewsLink(url));
    }

    [Fact]
    public async Task LaunchHomeLoadsThreeNewsItems()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"result":{"results":[
                  {"title":"A","url":"https://www.minecraft.net/a","description":"","image":"","time":1700000000},
                  {"title":"B","url":"https://www.minecraft.net/b","description":"","image":"","time":1700000000},
                  {"title":"C","url":"https://www.minecraft.net/c","description":"","image":"","time":1700000000},
                  {"title":"D","url":"https://www.minecraft.net/d","description":"","image":"","time":1700000000}
                ]}}
                """, Encoding.UTF8, "application/json")
        });
        var services = new AppServices(new AppSettings { GameDirectory = Path.GetTempPath() }, saveSettings: _ => { });
        var model = new WMCL.App.ViewModels.LaunchViewModel(services,
            newsService: new MinecraftNewsService(new HttpClient(handler)));
        await model.LoadHomeNewsAsync();
        Assert.Equal(3, model.HomeNews.Count);
        Assert.True(model.HasHomeNews);
        Assert.Contains("已加载 3 条新闻", model.HomeNewsStatus);
    }

    [AvaloniaFact]
    public async Task WindowListsParsedNews()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"result":{"results":[
                  {"title":"News","url":"https://www.minecraft.net/a","description":"Desc","author":"Mojang","image":"","time":1700000000}
                ]}}
                """, Encoding.UTF8, "application/json")
        });
        var window = new MinecraftNewsWindow(new MinecraftNewsService(new HttpClient(handler)))
        {
            Width = 900,
            Height = 650
        };
        window.Show();
        try
        {
            for (var index = 0; index < 20; index++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Assert.Equal(1, window.FindControl<ItemsControl>("NewsItemsControl")!.ItemCount);
            Assert.Contains("共 1 条新闻", window.FindControl<TextBlock>("StatusText")!.Text);
        }
        finally { window.Close(); }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
