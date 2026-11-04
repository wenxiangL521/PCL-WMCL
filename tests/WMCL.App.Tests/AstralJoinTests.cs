using WMCL.Link;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Integration")]
public sealed class AstralJoinTests
{
    private static LinkViewModel Model()
    {
        var settings = new AppSettings { GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "astral-" + Guid.NewGuid().ToString("N")) };
        return new LinkViewModel(new AppServices(settings, saveSettings: _ => { }));
    }

    [AvaloniaFact]
    public async Task LoopbackOnlyInviteIsRejectedWithExplanation()
    {
        var json = """{"v":1,"game_id":"mc","game_name":"Minecraft","network_name":"astral","network_secret":"astral","peers":[{"uri":"tcp://127.0.0.1:11010"}]}""";
        var code = "AG1." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var model = Model();
        model.AstralInvite = code;
        await model.JoinAstralCommand.ExecuteAsync(null);
        Assert.Contains("127.0.0.1", model.AstralStatus);
        Assert.False(model.IsRunning);
    }

    [AvaloniaFact]
    public async Task InvalidInviteShowsParseError()
    {
        var model = Model();
        model.AstralInvite = "not-a-code";
        await model.JoinAstralCommand.ExecuteAsync(null);
        Assert.False(string.IsNullOrWhiteSpace(model.AstralStatus));
        Assert.False(model.IsRunning);
    }

    [Fact]
    public void OrphanCleanupDoesNotThrowWithoutOrphans()
    {
        Assert.True(EasyTierService.CleanupOrphans() >= 0);
    }
}
