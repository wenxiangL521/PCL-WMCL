using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class NavHeaderTests
{
    [AvaloniaFact]
    public void GroupHeaderCannotBeSelected()
    {
        var settings = new AppSettings { GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "nav-" + Guid.NewGuid().ToString("N")) };
        using var model = new MainWindowViewModel(new AppServices(settings, saveSettings: _ => { }));
        model.NavigateTo("download");
        var header = model.SubTabs.Single(item => item.Title == "社区资源");
        Assert.True(header.IsHeader);

        var before = model.SelectedSubTab;
        model.SelectedSubTab = header;
        Assert.NotSame(header, model.SelectedSubTab);
        Assert.False(model.SelectedSubTab?.IsHeader ?? false);
        Assert.Same(before, model.SelectedSubTab);
    }

    [AvaloniaFact]
    public void HeaderRowIsDisabledInTheList()
    {
        var settings = new AppSettings { GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "nav-ui-" + Guid.NewGuid().ToString("N")) };
        using var model = new MainWindowViewModel(new AppServices(settings, saveSettings: _ => { }));
        model.NavigateTo("download");
        var window = new MainWindow { DataContext = model, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            for (var index = 0; index < 4; index++)
            {
                Dispatcher.UIThread.RunJobs();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            var list = window.GetVisualDescendants().OfType<ListBox>().First(box => box.Classes.Contains("subTabs"));
            var containers = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            var headerContainer = containers.First(item => item.DataContext is NavItem { IsHeader: true });
            Assert.False(headerContainer.IsEnabled);

            var shotDir = Environment.GetEnvironmentVariable("WMCL_SHOT_DIR");
            if (!string.IsNullOrWhiteSpace(shotDir))
            {
                Directory.CreateDirectory(shotDir);
                var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(1000, 700), new Avalonia.Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(shotDir, "download-nav.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }
}
