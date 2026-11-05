using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

public sealed class MicrosoftReloginButtonTests
{
    private static LaunchViewModel Model(out AppServices services)
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "relogin-" + Guid.NewGuid().ToString("N"));
        services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        return new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
    }

    [AvaloniaFact]
    public void ExpiredMicrosoftAccountReloginOpensLoginPanel()
    {
        var model = Model(out _);
        model.SetMicrosoftAccount(new MicrosoftAccount("Player", "uuid", "token", "refresh", DateTimeOffset.MaxValue));
        model.MicrosoftLoginExpired = true;

        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 420, Height = 640 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(item => item.Name == "MicrosoftReloginButton");
            Assert.True(button.IsEffectivelyVisible);

            // 走与点击相同的入口：按钮的 Click 处理器会打开登录面板
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var host = view.GetVisualDescendants().OfType<ContentControl>().Single(item => item.Name == "LoginHost");
            Assert.NotNull(host.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SuccessfulLoginClearsExpiredFlag()
    {
        var model = Model(out _);
        model.MicrosoftLoginExpired = true;
        model.SetMicrosoftAccount(new MicrosoftAccount("Player2", "uuid2", "token", "refresh", DateTimeOffset.MaxValue));
        Assert.False(model.MicrosoftLoginExpired);
    }
}
