using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ResourceUpdateReviewTests
{
    [AvaloniaFact]
    public async Task AppliesOnlyTheSelectedSnapshotAndNeverRepeatsSuccessfulItems()
    {
        var calls = new List<string>();
        var first = Item("first", _ => { calls.Add("first"); return Task.CompletedTask; });
        var second = Item("second", _ => { calls.Add("second"); return Task.CompletedTask; });
        var review = new ResourceUpdateReviewViewModel("模组", [first, second]);
        Assert.Equal(2, review.SelectedCount);
        second.IsSelected = false;
        await review.ApplySelectedCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "first" }, calls);
        Assert.True(first.IsCompleted);
        Assert.False(first.IsSelected);
        first.IsSelected = true;
        Assert.False(first.IsSelected);
        second.IsSelected = true;
        await review.ApplySelectedCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "first", "second" }, calls);
        Assert.False(review.CanApply);
    }

    [AvaloniaFact]
    public async Task SelectionChangesDuringApplyDoNotExpandSnapshotAndGuardStopsTheNextItem()
    {
        var current = true;
        var calls = new List<string>();
        var third = Item("third", _ => { calls.Add("third"); return Task.CompletedTask; });
        third.IsSelected = false;
        var first = Item("first", _ =>
        {
            calls.Add("first");
            current = false;
            third.IsSelected = true;
            return Task.CompletedTask;
        });
        var second = Item("second", _ => { calls.Add("second"); return Task.CompletedTask; });
        var review = new ResourceUpdateReviewViewModel("资源包", [first, second, third], () => current);
        await review.ApplySelectedCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "first" }, calls);
        Assert.Contains("目标已改变", review.Status);
        Assert.False(second.IsCompleted);
        Assert.True(second.IsSelected);
    }

    [AvaloniaFact]
    public async Task FailedItemRemainsSelectedAndRetriesWithoutRepeatingSuccess()
    {
        var successfulCalls = 0;
        var retryCalls = 0;
        var first = Item("first", _ => { successfulCalls++; return Task.CompletedTask; });
        var second = Item("second", _ =>
        {
            retryCalls++;
            return retryCalls == 1 ? Task.FromException(new InvalidOperationException("fake failure")) : Task.CompletedTask;
        });
        var review = new ResourceUpdateReviewViewModel("模组", [first, second]);
        await review.ApplySelectedCommand.ExecuteAsync(null);
        Assert.True(second.IsSelected);
        Assert.False(second.IsCompleted);
        Assert.Contains("fake failure", second.Status);
        await review.ApplySelectedCommand.ExecuteAsync(null);
        await review.ApplySelectedCommand.ExecuteAsync(null);
        Assert.Equal(1, successfulCalls);
        Assert.Equal(2, retryCalls);
        Assert.True(second.IsCompleted);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelAndWindowCloseNeverSubmitAndBusyCloseCancelsToken(bool closeWhileBusy)
    {
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = Item("first", async token =>
        {
            calls++;
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var review = new ResourceUpdateReviewViewModel("模组", [item]);
        review.CancelCommand.Execute(null);
        var idleWindow = new ResourceUpdateReviewWindow(review);
        idleWindow.Show();
        idleWindow.Close();
        Assert.Equal(0, calls);
        var window = new ResourceUpdateReviewWindow(review);
        window.Show();
        var applying = review.ApplySelectedCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(review.IsBusy);
        if (closeWhileBusy) window.Close();
        else review.CancelCommand.Execute(null);
        await applying.WaitAsync(TimeSpan.FromSeconds(5));
        window.Close();
        Assert.Equal(1, calls);
        Assert.False(item.IsCompleted);
        Assert.True(item.IsSelected);
        Assert.Contains("已取消", review.Status);
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task PhysicalCheckboxClickSelectsOnlyTheReviewedItem(string theme)
    {
        ThemeService.Apply(theme);
        var calls = new List<string>();
        var review = new ResourceUpdateReviewViewModel("模组", [
            Item("Alpha", _ => { calls.Add("Alpha"); return Task.CompletedTask; }),
            Item("Beta", _ => { calls.Add("Beta"); return Task.CompletedTask; })]);
        var window = new ResourceUpdateReviewWindow(review);
        window.Show();
        try
        {
            await PumpAsync();
            var checkboxes = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal(2, checkboxes.Length);
            Click(window, checkboxes[1]);
            await PumpAsync();
            Assert.Equal(1, review.SelectedCount);
            var directory = Environment.GetEnvironmentVariable("WMCL_UPDATE_REVIEW_SHOT_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"resource-update-review-{theme}.png"));
            }
            Click(window, window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "更新所选"));
            if (review.ApplySelectedCommand.ExecutionTask is { } execution) await execution;
            Assert.Equal(new[] { "Alpha" }, calls);
            Assert.False(checkboxes[0].IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EmptyReviewCannotApplyAndSelectCommandsSkipCompletedItems()
    {
        var review = new ResourceUpdateReviewViewModel("模组", []);
        Assert.True(review.IsEmpty);
        Assert.False(review.CanApply);
        Assert.Contains("没有可更新", review.Status);
        review.SelectAllCommand.Execute(null);
        review.SelectNoneCommand.Execute(null);
        Assert.Equal(0, review.SelectedCount);
    }

    private static ResourceUpdateReviewItem Item(string title, Func<CancellationToken, Task> apply) =>
        new(title, "1.0", "2.0", "用于测试的更新说明", apply);

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Input(window, "MouseMove", point, RawInputModifiers.None);
        Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Input(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 10; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }
}
