using System.Net;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

public sealed class ResourceAutoSearchTests
{
    [AvaloniaTheory]
    [InlineData("mod")]
    [InlineData("modpack")]
    [InlineData("resourcepack")]
    public async Task AutoSearchWaitsForBusyAndStillLoadsTheFirstPage(string type)
    {
        using var handler = new Hits();
        using var http = new HttpClient(handler);
        var services = CreateServices(http);
        var mods = InstallTestData.CreateMods(services);
        var packs = new ModpackViewModel(services);
        var content = new InstanceContentViewModel(services) { IsDownloadPage = true };
        Task ensure;
        Action release;
        Func<int> resultCount;

        if (type == "mod")
        {
            mods.SetMode("mods");
            mods.IsModBusy = true;
            ensure = mods.EnsureResourceSearchAsync();
            release = () => mods.IsModBusy = false;
            resultCount = () => mods.SearchResults.Count;
        }
        else if (type == "modpack")
        {
            packs.IsBusy = true;
            ensure = packs.EnsureResourceSearchAsync();
            release = () => packs.IsBusy = false;
            resultCount = () => packs.Results.Count;
        }
        else
        {
            content.SetMode("content-resource");
            content.IsDownloadBusy = true;
            ensure = content.EnsureResourceSearchAsync();
            release = () => content.IsDownloadBusy = false;
            resultCount = () => content.DownloadResults.Count;
        }

        await Task.Delay(150);
        Assert.Equal(0, resultCount());
        release();
        await ensure.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, resultCount());
        Assert.Single(handler.Requests);
    }

    [AvaloniaFact]
    public async Task WorldsDefaultToCurseForgeAndCarryCurseForgeLogoToDetail()
    {
        using var handler = new Hits();
        using var http = new HttpClient(handler);
        var services = CreateServices(http);
        var content = new InstanceContentViewModel(services) { IsDownloadPage = true };

        content.SetMode("content-world");

        Assert.Equal("世界", content.SelectedDownloadType);
        Assert.Equal("CurseForge", content.SelectedPlatform);
        Assert.Equal(["CurseForge"], content.Platforms);
        await content.EnsureResourceSearchAsync();

        var result = Assert.Single(content.DownloadResults);
        Assert.Equal("CurseForge", result.Platform);
        Assert.Equal("https://example.invalid/cf-logo.png", result.IconUrl);
        Assert.Contains("classId=17", Assert.Single(handler.Requests).Query);
        Assert.Contains("CurseForge", content.DownloadStatus);
        content.SelectedDownloadProject = result;
        Assert.Same(result, content.SelectedDownloadProject);
        Assert.Equal(result.IconUrl, content.SelectedDownloadProject.IconUrl);
    }

    private static AppServices CreateServices(HttpClient http)
    {
        var settings = new AppSettings
        {
            GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-auto-search-" + Guid.NewGuid().ToString("N"))
        };
        return new AppServices(settings, new ModrinthClient(http, http),
            new CurseForgeClient("test-key", http), saveSettings: _ => { });
    }

    private sealed class Hits : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var isCurseForge = request.RequestUri!.Host.Contains("curseforge", StringComparison.OrdinalIgnoreCase);
            var body = isCurseForge
                ? """
                  {"data":[{"id":42,"name":"CF Project","slug":"cf-project","summary":"Description","downloadCount":5,
                  "logo":{"thumbnailUrl":"https://example.invalid/cf-logo.png"}}],"pagination":{"totalCount":1}}
                  """
                : """
                  {"hits":[{"project_id":"sample","slug":"sample","title":"Sample","description":"Description",
                  "project_type":"mod","downloads":1,"categories":[]}],"total_hits":1}
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
