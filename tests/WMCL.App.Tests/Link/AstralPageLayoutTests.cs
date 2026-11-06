using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class AstralPageLayoutTests
{
    private static (LinkViewModel Link, AstralViewModel Astral) Create()
    {
        var settings = new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "astral-layout-" + Guid.NewGuid().ToString("N"))
        };
        var link = new LinkViewModel(new AppServices(settings, saveSettings: _ => { }));
        link.SelectedBackend = "Astral";
        return (link, new AstralViewModel(link));
    }

    [AvaloniaFact]
    public void HomeShowsTwoCardsAndSwitchesToCreateThenBack()
    {
        var (link, astral) = Create();
        var view = new AstralView { DataContext = astral };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var cards = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("roomCard") && button.IsEffectivelyVisible).ToArray();
            Assert.Equal(2, cards.Length);

            astral.ShowCreateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(astral.ShowCreateForm);
            Assert.False(astral.ShowHome);
            // 首页卡片全部隐藏，只留创建表单
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
                button => button.Classes.Contains("roomCard") && button.IsEffectivelyVisible);

            astral.GoHomeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(astral.ShowHome);
            Assert.Equal(2, view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("roomCard") && button.IsEffectivelyVisible).Count());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void JoinPanelShowsRoomListBeforeAddRoomAndCanGoBack()
    {
        var (link, astral) = Create();
        var view = new AstralView { DataContext = astral };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            astral.ShowJoinCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(astral.ShowJoinPanel);

            // 房间列表在添加房间之前
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
            Assert.Contains("房间列表", texts);
            Assert.Contains("添加房间", texts);
            Assert.True(texts.IndexOf("房间列表") < texts.IndexOf("添加房间"));

            // 添加房间默认收起
            var expander = view.GetVisualDescendants().OfType<Expander>()
                .First(item => item.Header is StackPanel panel &&
                               panel.Children.OfType<TextBlock>().Any(block => block.Text == "添加房间"));
            Assert.False(expander.IsExpanded);

            astral.GoHomeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(astral.ShowHome);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsAstralSectionExposesWorkingCommands()
    {
        var (link, astral) = Create();
        link.Astral = astral;
        var settings = new LinkSettingsViewModel(link);
        var view = new LinkSettingsView { DataContext = settings };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        try
        {
            var advanced = view.GetVisualDescendants().OfType<Expander>()
                .First(item => item.Header?.ToString()?.Contains("高级设置", StringComparison.Ordinal) == true);
            advanced.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            var follow = view.GetVisualDescendants().OfType<Button>()
                .First(button => Equals(button.Content, "跟随账号名"));
            Assert.True(follow.DataContext is AstralViewModel,
                $"按钮 DataContext 类型：{follow.DataContext?.GetType().Name ?? "null"}");
            Assert.NotNull(follow.Command);
            Assert.True(follow.IsEffectivelyEnabled);

            var before = astral.Relays.Count;
            astral.NewRelayUri = "tcp://relay.test:11010";
            var add = view.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, "添加"));
            Assert.NotNull(add.Command);
            add.Command.Execute(add.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, astral.Relays.Count);
        }
        finally
        {
            window.Close();
        }
    }
}

public sealed class AstralHomeCenteringTests
{
    [AvaloniaFact]
    public void HomeCardsAreVerticallyCenteredWhenThereIsSpareSpace()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "center-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var link = new LinkViewModel(services);
        link.SelectedBackend = "Astral";
        var astral = new AstralViewModel(link);
        var view = new AstralView { DataContext = astral };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var card = view.GetVisualDescendants().OfType<Button>()
                .First(item => item.Classes.Contains("roomCard") && item.IsEffectivelyVisible);
            // 上下留白行都分到了高度，说明内容被垂直居中而不是贴顶
            var grid = view.GetVisualDescendants().OfType<Grid>()
                .First(item => item.RowDefinitions.Count == 3 &&
                               item.RowDefinitions[0].Height.IsStar &&
                               item.RowDefinitions[2].Height.IsStar);
            var top = grid.RowDefinitions[0].ActualHeight;
            var bottom = grid.RowDefinitions[2].ActualHeight;
            Assert.True(top > 40, $"顶部留白只有 {top:0}");
            Assert.True(Math.Abs(top - bottom) < 80, $"上 {top:0} / 下 {bottom:0} 差距过大");
        }
        finally { window.Close(); }
    }
}

public sealed class AstralStateAnimationTests
{
    [AvaloniaFact]
    public void EveryStatePanelAnimatesOnEnter()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "anim-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var link = new LinkViewModel(services);
        link.SelectedBackend = "Astral";
        var astral = new AstralViewModel(link);
        var view = new AstralView { DataContext = astral };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var animated = view.GetVisualDescendants().OfType<Control>()
                .Count(control => WMCL.App.Animation.Motion.GetAnimateOnVisible(control));
            // 首页 / 创建 / 加入 / 已连接 四个状态都带入场动画
            Assert.True(animated >= 3, $"只有 {animated} 个面板带入场动画");
        }
        finally { window.Close(); }
    }
}
