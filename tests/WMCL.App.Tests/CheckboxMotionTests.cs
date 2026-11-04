using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WMCL.App.Tests;

[Collection("motion-ui")]
[Trait("Kind", "Motion")]
public sealed class CheckboxMotionTests
{
    [AvaloniaFact]
    public void CheckMarkPopsInAndFadesOutInsteadOfSnapping()
    {
        var checkBox = new CheckBox
        {
            Theme = (Avalonia.Styling.ControlTheme)Application.Current!.Resources["PclCheckBoxTheme"]!,
            Content = "测试"
        };
        var window = new Window { Content = checkBox, Width = 200, Height = 60 };
        try
        {
            window.Show();
            Pump();
            var mark = checkBox.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(path => path.Name == "CheckBoxCheck");
            Assert.Equal(0, mark.Opacity);
            var initial = (mark.RenderTransform as ITransform)?.Value;
            Assert.NotNull(initial);
            Assert.InRange(initial!.Value.M11, 0.35, 0.45);

            checkBox.IsChecked = true;
            Pump();
            // 勾选后标记必须是可见的（过渡结束后稳定在 1），并且是放大到位的。
            Assert.True(WaitUntil(() => mark.Opacity > 0.99 &&
                (mark.RenderTransform as ITransform)?.Value.M11 is > 0.99 and < 1.01, 1500));

            checkBox.IsChecked = false;
            Pump();
            Assert.True(WaitUntil(() => mark.Opacity < 0.01, 1000));
        }
        finally { window.Close(); }
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    private static void Pump()
    {
        for (var index = 0; index < 4; index++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
