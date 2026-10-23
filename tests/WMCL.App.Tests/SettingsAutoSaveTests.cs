using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core;

namespace WMCL.App.Tests;

public sealed class SettingsAutoSaveTests
{
    [AvaloniaFact]
    public async Task SettingChangeSavesAfterDebounceWithoutRepeatedSaveButtons()
    {
        var saves = 0;
        var services = new AppServices(new AppSettings(), saveSettings: _ => saves++);
        var model = new SettingsViewModel(services, (_, _) => Task.FromResult(true));
        var view = new SettingsView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "保存设置"));
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "应用目录"));

            var pendingDirectory = Path.Combine(Path.GetTempPath(), "wmcl-pending-" + Guid.NewGuid().ToString("N"));
            model.GameDirectory = pendingDirectory;
            model.MemoryMb = 8192;
            await Task.Delay(600);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(8192, services.Settings.MemoryMb);
            Assert.True(saves > 0);
            Assert.NotEqual(pendingDirectory, services.Settings.GameDirectory);
            Assert.Equal(pendingDirectory, model.GameDirectory);
            Assert.Equal("已自动保存。", model.Status);
        }
        finally { window.Close(); }
    }
}
