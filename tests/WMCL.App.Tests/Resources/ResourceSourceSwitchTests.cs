using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ResourceSourceSwitchTests
{
    [AvaloniaFact]
    public void ResetSearchKeepsTheSelectedSource()
    {
        var services = new AppServices(new AppSettings(), saveSettings: _ => { });
        var mods = new ModsViewModel(services) { IsSearchMode = true, SelectedModSource = "CurseForge" };
        mods.ResetSearchCommand.Execute(null);
        Assert.Equal("CurseForge", mods.SelectedModSource);
    }

    [AvaloniaFact]
    public async Task SwitchingSourceAfterASearchRunsTheSearchAgain()
    {
        var modrinthHandler = new RecordingHandler(ModrinthJson);
        var curseForgeHandler = new RecordingHandler(CurseForgeJson);
        var services = new AppServices(
            new AppSettings(),
            modrinth: new ModrinthClient(new HttpClient(modrinthHandler)),
            curseForge: new CurseForgeClient("", new HttpClient(curseForgeHandler)),
            saveSettings: _ => { });
        var model = new ModsViewModel(services) { IsSearchMode = true };

        await model.SearchModsCommand.ExecuteAsync(null);
        Assert.True(modrinthHandler.Count > 0);

        model.SelectedModSource = "CurseForge";
        var start = Environment.TickCount64;
        while (curseForgeHandler.Count == 0 && Environment.TickCount64 - start < 5000)
            await Task.Delay(20);

        Assert.True(curseForgeHandler.Count > 0, "切换来源后没有自动重新搜索");
    }

    private const string ModrinthJson = """
        {"hits":[{"project_id":"p1","title":"Sodium","description":"fps","slug":"sodium","downloads":1,"icon_url":null,"author":"a","categories":["fabric"]}],"total_hits":1}
        """;

    private const string CurseForgeJson = """
        {"data":[{"id":1,"name":"Sodium","slug":"sodium","summary":"fps","downloadCount":1,"logo":{"thumbnailUrl":null}}],"pagination":{"totalCount":1}}
        """;

    private sealed class RecordingHandler(string json) : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
