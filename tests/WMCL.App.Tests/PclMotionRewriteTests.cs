using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using WMCL.App.Animation;

namespace WMCL.App.Tests;

public sealed class PclMotionRewriteTests
{
    [Fact]
    public void FluentAndBackCurvesMatchPcl2Formulas()
    {
        Assert.Equal(0.875, PclEasing.Out(0.5, 3), 6);
        Assert.Equal(0.125, PclEasing.In(0.5, 3), 6);
        Assert.Equal(1 - 0.25 * Math.Cos(1.5 * Math.PI * 0.5), PclEasing.OutBack(0.5, 2), 6);
        Assert.Equal(1, PclEasing.Out(1, 5));
        Assert.Equal(1, PclEasing.OutBack(1, 3));
    }

    [Fact]
    public void NormalButtonPressUsesTwoAdditivePclChannels()
    {
        Assert.Equal(0.945, PclButtonMotion.PressScaleAt(1, 700), 6);
        Assert.Equal(1, PclButtonMotion.PressScaleAt(1, 0));
    }

    [AvaloniaFact]
    public async Task ModalUsesTranslateAndRotateInsteadOfScale()
    {
        var window = new Window();
        await ModalMotion.AnimateInAsync(window);
        Assert.Equal(1, window.Opacity);
        var group = Assert.IsType<TransformGroup>(window.RenderTransform);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(group.Children[0]).Y, 6);
        Assert.Equal(0, Assert.IsType<RotateTransform>(group.Children[1]).Angle, 6);

        await ModalMotion.AnimateOutAsync(window);
        Assert.Equal(0, window.Opacity, 6);
        Assert.Equal(20, Assert.IsType<TranslateTransform>(group.Children[0]).Y, 6);
        Assert.Equal(6, Assert.IsType<RotateTransform>(group.Children[1]).Angle, 6);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ReducedMotionSkipsModalIntermediateFrames()
    {
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        try
        {
            var window = new Window();
            await ModalMotion.AnimateInAsync(window);
            Assert.Equal(1, window.Opacity);
            Assert.Null(window.RenderTransform);
            await ModalMotion.AnimateOutAsync(window);
            Assert.Equal(0, window.Opacity);
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous);
        }
    }

    [AvaloniaFact]
    public async Task ExtraButtonShowAndHideFinishAtPclScale()
    {
        var button = new Border();
        await PclExtraButtonMotion.SetVisibleAsync(button, true);
        Assert.True(button.IsVisible);
        Assert.Equal(1, Assert.IsType<ScaleTransform>(button.RenderTransform).ScaleX, 6);
        await PclExtraButtonMotion.SetVisibleAsync(button, false);
        Assert.False(button.IsVisible);
        Assert.Equal(0, Assert.IsType<ScaleTransform>(button.RenderTransform).ScaleX, 6);
    }

    [AvaloniaFact]
    public async Task InlineLoginSwitchCompletesWithFullyVisibleContent()
    {
        var host = new ContentControl();
        var account = new Border();
        var login = new Border();
        PclLoginMotion.Show(host, account, login);
        Assert.Same(login, host.Content);
        Assert.True(host.IsVisible);
        Assert.False(account.IsVisible);
        await WaitForOpacity(host);
        // WaitForOpacity stops at >= 0.999; the remaining frame may be a hair under 1 on a
        // loaded machine, so assert the visible-completion range rather than an exact frame.
        Assert.InRange(host.Opacity, 0.999, 1);

        PclLoginMotion.Hide(host, account);
        Assert.Null(host.Content);
        Assert.False(host.IsVisible);
        Assert.True(account.IsVisible);
        await WaitForOpacity(account);
        Assert.Equal(1, account.Opacity, 6);
    }

    private static async Task WaitForOpacity(Control control)
    {
        for (var attempt = 0; attempt < 100 && control.Opacity < 0.999; attempt++)
            await Task.Delay(10);
    }
}
