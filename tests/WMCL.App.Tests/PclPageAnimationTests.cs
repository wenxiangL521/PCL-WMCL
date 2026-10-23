using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;

namespace WMCL.App.Tests;

public sealed class PclPageAnimationTests
{
    [AvaloniaFact]
    public async Task CardsEnterAndExitInOrderAndExitDisablesInputImmediately()
    {
        Control[] cards = [new Border(), new Border(), new Border(), new Border()];
        var entering = PclPageAnimation.RunAsync(cards, true, CancellationToken.None);
        Assert.Equal(0, cards[0].Opacity, 1);
        Assert.Equal(-16, ((TranslateTransform)cards[0].RenderTransform!).Y, 1);
        await Task.Delay(50);
        Assert.True(cards[0].Opacity > cards[3].Opacity);
        await entering;
        Assert.All(cards, card =>
        {
            Assert.Equal(1, card.Opacity);
            Assert.Equal(0, ((TranslateTransform)card.RenderTransform!).Y, 4);
            Assert.True(card.IsHitTestVisible);
        });
        var exiting = PclPageAnimation.RunAsync(cards, false, CancellationToken.None);
        Assert.All(cards, card => Assert.False(card.IsHitTestVisible));
        await exiting;
        Assert.All(cards, card =>
        {
            Assert.Equal(0, card.Opacity);
            Assert.Equal(-6, ((TranslateTransform)card.RenderTransform!).Y);
        });
        PclPageAnimation.Reset(cards);
        Assert.All(cards, card => Assert.True(card.IsHitTestVisible));
    }

    [AvaloniaFact]
    public async Task CancellationFreezesTheOldAnimationAndAReplacementCanComplete()
    {
        Control[] cards = [new Border(), new Border()];
        using var cancellation = new CancellationTokenSource();
        var original = PclPageAnimation.RunAsync(cards, true, cancellation.Token);
        await Task.Delay(60);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
        var stopped = cards[0].Opacity;
        await Task.Delay(80);
        Assert.Equal(stopped, cards[0].Opacity);
        await PclPageAnimation.RunAsync(cards, true, CancellationToken.None, 300);
        Assert.All(cards, card => Assert.Equal(1, card.Opacity));
    }

    [AvaloniaFact]
    public async Task ReducedMotionCompletesBothDirectionsWithoutIntermediateFrames()
    {
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        try
        {
            Control[] cards = [new Border(), new Border()];
            var entering = PclPageAnimation.RunAsync(cards, true, CancellationToken.None);
            Assert.True(entering.IsCompletedSuccessfully);
            Assert.All(cards, card => Assert.Equal(1, card.Opacity));
            var exiting = PclPageAnimation.RunAsync(cards, false, CancellationToken.None);
            Assert.True(exiting.IsCompletedSuccessfully);
            Assert.All(cards, card => Assert.Equal(0, card.Opacity));
        }
        finally { Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous); }
    }

    [AvaloniaFact]
    public async Task ARealVerticalScrollBarMovesWithoutChangingLayoutOrScrollPosition()
    {
        var card = new Border { Height = 900 };
        var viewer = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Content = viewer, Width = 320, Height = 180 };
        window.Show();
        try
        {
            await Pump(40);
            var bar = Assert.IsType<ScrollBar>(PclPageAnimation.FindVerticalScrollBar(viewer));
            bar.RenderTransform = null;
            var bounds = bar.Bounds;
            var viewport = viewer.Viewport;
            viewer.Offset = new Vector(0, 75);
            var entering = PclPageAnimation.RunAsync([card], true, CancellationToken.None, scrollViewer: viewer);
            var translation = Assert.IsType<TranslateTransform>(bar.RenderTransform);
            Assert.InRange(translation.X, 9.5, 10);
            await Pump(100);
            Assert.InRange(translation.X, 0.01, 9.5);
            await entering;
            Assert.Equal(0, translation.X);
            Assert.Equal(bounds, bar.Bounds);
            Assert.Equal(viewport, viewer.Viewport);
            Assert.Equal(75, viewer.Offset.Y);
            await PclPageAnimation.RunAsync([card], false, CancellationToken.None, scrollViewer: viewer);
            Assert.Equal(10, translation.X);
            Assert.Equal(bounds, bar.Bounds);
            Assert.Equal(75, viewer.Offset.Y);
            PclPageAnimation.ResetScrollBar(viewer);
            Assert.Equal(0, translation.X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CancelledScrollBarMotionFreezesAndReversesFromTheCurrentPosition()
    {
        var card = new Border { Height = 900 };
        var viewer = new ScrollViewer { Content = card };
        var window = new Window { Content = viewer, Width = 320, Height = 180 };
        window.Show();
        try
        {
            await Pump(40);
            var bar = Assert.IsType<ScrollBar>(PclPageAnimation.FindVerticalScrollBar(viewer));
            var translation = new TranslateTransform();
            bar.RenderTransform = translation;
            using var cancellation = new CancellationTokenSource();
            var exiting = PclPageAnimation.RunAsync([card], false, cancellation.Token, scrollViewer: viewer);
            await Pump(30);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exiting);
            var stopped = translation.X;
            Assert.InRange(stopped, 0.01, 9.99);
            await Pump(50);
            Assert.Equal(stopped, translation.X);
            var returning = PclPageAnimation.RunAsync([card], true, CancellationToken.None, scrollViewer: viewer);
            Assert.InRange(translation.X, stopped - 0.05, stopped);
            await returning;
            Assert.Equal(0, translation.X);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenVerticalAndHorizontalOnlyScrollBarsAreNotAnimated(bool horizontalOverflow)
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = horizontalOverflow ? 900 : 50, Height = 30 },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var window = new Window { Content = viewer, Width = 320, Height = 180 };
        window.Show();
        try
        {
            await Pump(40);
            Assert.Null(PclPageAnimation.FindVerticalScrollBar(viewer));
            var bars = viewer.GetVisualDescendants().OfType<ScrollBar>().ToArray();
            Assert.NotEmpty(bars);
            foreach (var bar in bars) bar.RenderTransform = new TranslateTransform(4, 0);
            await PclPageAnimation.RunAsync([], false, CancellationToken.None, scrollViewer: viewer);
            Assert.All(bars, bar => Assert.Equal(4, ((TranslateTransform)bar.RenderTransform!).X));
            PclPageAnimation.ResetScrollBar(viewer);
            foreach (var bar in bars)
                Assert.Equal(bar.Orientation == Orientation.Vertical ? 0 : 4, ((TranslateTransform)bar.RenderTransform!).X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReducedMotionSnapsTheRealScrollBarInBothDirections()
    {
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var card = new Border { Height = 900 };
        var viewer = new ScrollViewer { Content = card };
        var window = new Window { Content = viewer, Width = 320, Height = 180 };
        window.Show();
        try
        {
            await Pump(40);
            var bar = Assert.IsType<ScrollBar>(PclPageAnimation.FindVerticalScrollBar(viewer));
            var exiting = PclPageAnimation.RunAsync([card], false, CancellationToken.None, scrollViewer: viewer);
            Assert.True(exiting.IsCompletedSuccessfully);
            Assert.Equal(10, Assert.IsType<TranslateTransform>(bar.RenderTransform).X);
            var entering = PclPageAnimation.RunAsync([card], true, CancellationToken.None, scrollViewer: viewer);
            Assert.True(entering.IsCompletedSuccessfully);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(bar.RenderTransform).X);
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous);
        }
    }

    private static async Task Pump(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        } while (DateTime.UtcNow < until);
    }
}
