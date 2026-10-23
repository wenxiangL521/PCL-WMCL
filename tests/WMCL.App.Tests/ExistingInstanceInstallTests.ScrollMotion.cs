using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using WMCL.App.Animation;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed partial class ExistingInstanceInstallTests
{
    [AvaloniaFact]
    public async Task RealModificationViewResetsScrollMotionWhenTheRootChangesDuringExit()
    {
        CreateInstance();
        var services = Services();
        var pending = true;
        var gate = new TaskCompletionSource<IReadOnlyList<OptiFabricFile>>();
        var model = CatalogModel(services, _ => false, optifabric: _ => pending ? gate.Task
            : Task.FromResult<IReadOnlyList<OptiFabricFile>>([InstallTestData.OptiFabric()]));
        var opening = model.OpenInstanceAsync(_root, "Custom");
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 240 };
        window.Show();
        try
        {
            await PumpScroll(800);
            pending = false;
            gate.SetResult([InstallTestData.OptiFabric()]);
            await opening;
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!scroll.IsVisible && DateTime.UtcNow < until) await PumpScroll(10);
            await PumpScroll(30);
            Assert.True(scroll.IsVisible);
            var bar = PclPageAnimation.FindVerticalScrollBar(scroll);
            Assert.NotNull(bar);
            var translation = Assert.IsType<TranslateTransform>(bar.RenderTransform);
            Assert.InRange(translation.X, 0.01, 9.99);
            await PumpScroll(550);
            Assert.Equal(0, translation.X);
            pending = true;
            gate = new TaskCompletionSource<IReadOnlyList<OptiFabricFile>>();
            var reopening = model.OpenInstanceAsync(_root, "Custom");
            await PumpScroll(35);
            Assert.InRange(translation.X, 0.01, 9.99);
            services.SetGameDirectory(Path.Combine(_root, "new-root"));
            gate.SetResult([InstallTestData.OptiFabric()]);
            await reopening;
            await PumpScroll(300);
            Assert.Equal(0, translation.X);
            Assert.False(model.IsExistingInstance);
            Assert.True(view.FindControl<ScrollViewer>("CatalogScroll")!.IsVisible);
            Assert.False(scroll.IsVisible);
        }
        finally
        {
            gate.TrySetResult([]);
            window.Close();
        }
    }

    private static async Task PumpScroll(int milliseconds)
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
