using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class AccountPanelLayoutTests
{
    [AvaloniaFact]
    public void AllThreeAccountStatesUseTheSamePanelHeight()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "account-panel-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 400, Height = 600 };
        try
        {
            window.Show();
            Pump();
            var detail = view.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "AccountDetail");
            var offline = detail.Bounds.Height;
            model.SetMicrosoftAccount(new MicrosoftAccount("Player", "uuid", "token", "refresh", DateTimeOffset.MaxValue));
            Pump();
            var microsoft = detail.Bounds.Height;
            model.SetAuthlibAccount(new AuthlibAccount("https://example.invalid/api/yggdrasil", "Third", "uuid", "token", "client"));
            Pump();
            var authlib = detail.Bounds.Height;
            Assert.Equal(150, offline);
            Assert.Equal(offline, microsoft);
            Assert.Equal(offline, authlib);
        }
        finally { window.Close(); }
    }

    private static void Pump()
    {
        for (var index = 0; index < 4; index++)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
