using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

/// <summary>头像交互：右键刷新皮肤、悬停有提示。</summary>
public sealed class AvatarInteractionTests
{
    private static (LaunchView View, Window Window, LaunchViewModel Model) Create()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "avatar-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        model.SelectOfflineAccountCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return (view, window, model);
    }

    [AvaloniaFact]
    public void ContextMenuOffersSkinAndRefresh()
    {
        var (view, window, model) = Create();
        try
        {
            var avatar = view.FindControl<Border>("AvatarButton")!;
            var menu = avatar.ContextMenu;
            Assert.NotNull(menu);
            var headers = menu!.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? "").ToArray();
            Assert.Equal(new[] { "皮肤", "刷新皮肤" }, headers);

            // 点「刷新皮肤」应触发刷新
            var refresh = menu.Items.OfType<MenuItem>().Last();
            refresh.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("刷新", model.AccountStatus);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AvatarIsRoundedSquareWithHandCursor()
    {
        var (view, window, _) = Create();
        try
        {
            var avatar = view.FindControl<Border>("AvatarButton")!;
            Assert.NotNull(avatar.Cursor);
            Assert.Contains("Hand", avatar.Cursor!.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            // 方形头像：圆角明显小于半径（64 宽，半径 32 才是圆形）
            Assert.True(avatar.CornerRadius.TopLeft <= 12, $"圆角 {avatar.CornerRadius.TopLeft} 太大，会显示成圆形");
        }
        finally { window.Close(); }
    }
}
