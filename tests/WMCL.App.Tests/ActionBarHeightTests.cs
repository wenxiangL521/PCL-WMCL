using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class ActionBarHeightTests
{
    [AvaloniaFact]
    public void ModToolbarButtonsShareOneHeight()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "bar-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var mods = new ModsViewModel(services) { IsInstanceManagement = true };
        mods.SetMode("mods");
        var view = new ModsView { DataContext = mods };
        var window = new Window { Content = view, Width = 900, Height = 400 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var bar = view.GetVisualDescendants().OfType<WrapPanel>().First(panel => panel.Classes.Contains("actionBar"));
            var buttons = bar.Children.OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
                Assert.InRange(button.Bounds.Height, 31.9, 32.1);
        }
        finally { window.Close(); }
    }
}
