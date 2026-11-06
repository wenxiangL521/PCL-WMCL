using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class CommunityResourceImageTests
{
    [AvaloniaTheory]
    [InlineData("mod", 420)]
    [InlineData("mod", 660)]
    [InlineData("modpack", 420)]
    [InlineData("modpack", 660)]
    [InlineData("content-resource", 420)]
    [InlineData("content-resource", 660)]
    [InlineData("content-shader", 660)]
    [InlineData("content-world", 660)]
    [InlineData("content-datapack", 660)]
    public async Task ResourceIconsUseLargerSharedSizesWithoutOverlappingText(string type, double width)
    {
        using var http = new HttpClient(new EmptyVersions());
        var services = new AppServices(new AppSettings(), new ModrinthClient(http, http), saveSettings: _ => { });
        var mods = InstallTestData.CreateMods(services);
        mods.SetMode("mods");
        var packs = new ModpackViewModel(services);
        var content = new InstanceContentViewModel(services) { IsDownloadPage = true };
        content.SetMode(type.StartsWith("content-") ? type : "content-resource");
        var title = "Example community resource with a long title";
        if (type == "mod")
            mods.SearchResults.Add(new ModrinthProject("sample", "sample", title, "Description", "mod", 1, null, []));
        else if (type == "modpack")
            packs.Results.Add(new ModpackItem(title, "Description", "sample", 1, "Modrinth", "sample", 0));
        else content.DownloadResults.Add(new ResourceItem(title, "Description", "Modrinth", "sample", 0));
        UserControl view = type switch
        {
            "mod" => new ModsView { DataContext = mods },
            "modpack" => new ModpackView { DataContext = packs },
            _ => new InstanceContentView { DataContext = content }
        };
        var window = new Window { Content = view, Width = width, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var image = Assert.Single(view.GetVisualDescendants().OfType<RemoteImage>(), control => control.IsEffectivelyVisible);
            using var stream = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
            using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            image.Source = bitmap;
            window.UpdateLayout();
            Assert.Equal(72, image.Width);
            Assert.Equal(72, image.Height);
            Assert.Equal(72, image.Bounds.Width);
            Assert.Equal(72, image.Bounds.Height);
            Assert.Equal(Stretch.Uniform, image.Stretch);
            var row = Assert.IsType<Grid>(image.Parent);
            var text = Assert.Single(row.Children.OfType<StackPanel>());
            Assert.True(text.Bounds.X >= image.Bounds.Right + 12);
            Assert.True(text.Bounds.Width > 0);
            Assert.True(text.Bounds.Right <= row.Bounds.Width + 0.01);
            if (type == "mod") mods.SelectedMod = mods.SearchResults[0];
            else if (type == "modpack") packs.SelectedProject = packs.Results[0];
            else content.SelectedDownloadProject = content.DownloadResults[0];
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var detailImage = Assert.Single(view.GetVisualDescendants().OfType<RemoteImage>(), control => control.IsEffectivelyVisible);
            Assert.Equal(96, detailImage.Width);
            Assert.Equal(96, detailImage.Height);
            Assert.Equal(Stretch.Uniform, detailImage.Stretch);
        }
        finally { window.Close(); }
    }

    private sealed class EmptyVersions : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }
}
