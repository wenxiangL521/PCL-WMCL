using Avalonia.Threading;

namespace WMCL.App.Services;

/// <summary>
/// UI 线程调度的唯一入口。ViewModel 用这里的包装而不是直接摸 Avalonia 的 Dispatcher，
/// 便于测试替换，也把 Avalonia 依赖收在一个文件里。
/// </summary>
public static class UiDispatcher
{
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);

    public static void Post(Action action, DispatcherPriority priority) => Dispatcher.UIThread.Post(action, priority);

    public static Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public static Task InvokeAsync(Action action, DispatcherPriority priority) => Dispatcher.UIThread.InvokeAsync(action, priority).GetTask();
}
