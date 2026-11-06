using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class LaunchLogExportEntryTests
{
    [AvaloniaFact]
    public void HomeLogCardExposesTheExportCommandAndAReadableLogArea()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "log-entry-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        model.Log = "line 1\nline 2";
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(model.ExportLogsCommand);
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(item => Equals(item.Content, "导出日志"));
            Assert.Same(model.ExportLogsCommand, button.Command);
            Assert.True(button.IsEffectivelyVisible);
            var box = view.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "LogBox");
            Assert.True(box.MinHeight >= 150);
        }
        finally { window.Close(); }
    }
}
