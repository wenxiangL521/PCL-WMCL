using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Animation;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class InfiniteScrollTests
{
    private sealed class RecordingCommand : ICommand
    {
        public int Executions { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Executions++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private static (ScrollViewer Scroll, RecordingCommand Command, Window Window) Create(double ratio = 0.55)
    {
        var content = new Border { Height = 2000, Width = 300 };
        var scroll = new ScrollViewer { Content = content };
        InfiniteScroll.SetEnabled(scroll, true);
        var command = new RecordingCommand();
        InfiniteScroll.SetCommand(scroll, command);
        InfiniteScroll.SetCanLoadMore(scroll, true);
        InfiniteScroll.SetTriggerRatio(scroll, ratio);
        var window = new Window { Content = scroll, Width = 400, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (scroll, command, window);
    }

    [AvaloniaFact]
    public void LoadsNextPageOnlyAfterCrossingThreshold()
    {
        var (scroll, command, window) = Create();
        try
        {
            scroll.Offset = new Vector(0, 100);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, command.Executions);

            // 2000 高、视口约 400：阈值 0.55 → 约 1100 处才触发
            scroll.Offset = new Vector(0, 900);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, command.Executions);

            // 同一次加载内重复滚动不应重复触发
            scroll.Offset = new Vector(0, 950);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, command.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DoesNotLoadWhenNoMorePages()
    {
        var (scroll, command, window) = Create();
        try
        {
            InfiniteScroll.SetCanLoadMore(scroll, false);
            scroll.Offset = new Vector(0, 1600);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, command.Executions);
        }
        finally { window.Close(); }
    }
}
