using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.Views;
using Avalonia.Media.Imaging;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ConfirmWindowTests
{
    [AvaloniaTheory]
    [InlineData("Light", false)]
    [InlineData("Dark", false)]
    [InlineData("Light", true)]
    [InlineData("Dark", true)]
    public async Task ConfirmationAndDownloadFailureShareTheFlatPclShell(string theme, bool failure)
    {
        var previousTheme = Application.Current!.RequestedThemeVariant;
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        Application.Current.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var owner = new Window { Width = 850, Height = 500 };
        owner.Show();
        var message = failure
            ? "下载 Minecraft 失败\n\n无法连接下载服务器，请检查网络或稍后重试。\nhttps://example.invalid/client.jar"
            : "Minecraft 是付费游戏。如果你还没有购买，请先通过官方渠道购买正版。\n\n继续启动表示你已知晓；你可以在“关于”页打开正版购买页面。";
        var dialog = new ConfirmWindow(failure ? "下载失败" : "支持正版 Minecraft", message,
            failure ? "确定" : "继续启动", showCancel: !failure, isWarning: failure);
        try
        {
            var result = dialog.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowDecorations.None, dialog.WindowDecorations);
            Assert.False(dialog.ShowInTaskbar);
            Assert.False(dialog.CanResize);
            var surface = dialog.FindControl<Border>("DialogSurface")!;
            Assert.IsType<SolidColorBrush>(surface.Background);
            Assert.Equal(new CornerRadius(5), surface.CornerRadius);
            Assert.Equal(new Thickness(1), surface.BorderThickness);
            var body = dialog.FindControl<SelectableTextBlock>("MessageText")!;
            Assert.Equal(message, body.Text);
            Assert.Equal(TextWrapping.Wrap, body.TextWrapping);
            var confirm = dialog.FindControl<Button>("ConfirmButton")!;
            Assert.DoesNotContain("danger", confirm.Classes);
            Assert.True(confirm.IsDefault);
            Assert.Equal(!failure, dialog.FindControl<Button>("CancelButton")!.IsVisible);
            Assert.Equal(failure, dialog.FindControl<TextBlock>("TitleText")!.Classes.Contains("warning"));

            var outputDirectory = Environment.GetEnvironmentVariable("WMCL_DIALOG_SHOT_DIR");
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = dialog.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(outputDirectory, $"{theme.ToLowerInvariant()}-{(failure ? "download-failure" : "launch-confirm")}.png"), PngBitmapEncoderOptions.Default);
            }
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await result);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(false);
            owner.Close();
            Application.Current.RequestedThemeVariant = previousTheme;
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    [AvaloniaFact]
    public async Task DestructiveActionsKeepTheirWarningStyleAndDefaultToCancel()
    {
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var owner = new Window();
        owner.Show();
        var dialog = new ConfirmWindow("删除实例", "此操作不可恢复。", "确定删除");
        try
        {
            var result = dialog.ShowDialog<bool>(owner);
            var confirm = dialog.FindControl<Button>("ConfirmButton")!;
            var cancel = dialog.FindControl<Button>("CancelButton")!;
            Assert.Contains("danger", confirm.Classes);
            Assert.DoesNotContain("primary", confirm.Classes);
            Assert.False(confirm.IsDefault);
            Assert.True(cancel.IsDefault);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await result);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(false);
            owner.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    [AvaloniaFact]
    public async Task EscapeDismissesTheDialogWithoutConfirming()
    {
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var owner = new Window();
        owner.Show();
        var dialog = new ConfirmWindow("继续操作", "检查后再继续。");
        try
        {
            var result = dialog.ShowDialog<bool>(owner);
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(await result);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(false);
            owner.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    [Theory]
    [InlineData("支持正版 Minecraft", "确定")]
    [InlineData("下载失败", "确定")]
    [InlineData("删除实例", "确定删除")]
    [InlineData("移除收藏", "确定移除")]
    [InlineData("清理 Java", "确定清理")]
    [InlineData("恢复存档备份", "确定恢复")]
    public void DefaultActionMatchesTheOperationInsteadOfAlwaysDeleting(string title, string expected) =>
        Assert.Equal(expected, DialogService.DefaultConfirmText(title));
}
