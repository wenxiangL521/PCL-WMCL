using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class InstallErrorDialogTests
{
    [AvaloniaFact]
    public async Task InstallationFailureUsesAnAcknowledgementDialogWithoutADeleteOrCancelAction()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var notification = DialogService.ShowMessageAsync(owner, "安装 Custom失败", "下载失败：test failure");
            var dialog = Assert.IsType<ConfirmWindow>(Assert.Single(owner.OwnedWindows));
            var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            var acknowledge = Assert.Single(buttons);
            Assert.Equal("确定", acknowledge.Content);
            Assert.Equal("安装 Custom失败", dialog.Title);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "下载失败：test failure");
            acknowledge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await notification;
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close();
            owner.Close();
        }
    }
}
