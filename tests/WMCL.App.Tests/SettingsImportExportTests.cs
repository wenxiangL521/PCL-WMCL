using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class SettingsImportExportTests
{
    [AvaloniaFact]
    public async Task ExportAndImportPreservesAllSettings()
    {
        var previousProxy = WMCL.Core.ProxyManager.Current;
        var services = new AppServices(new AppSettings());
        using var model = new SettingsViewModel(services);
        var view = new WMCL.App.Views.SettingsView { DataContext = model };
        var window = new Avalonia.Controls.Window { Content = view, Width = 900, Height = 700 };
        window.Show();

        try
        {
            // 修改各类设置
            model.FontScale = 1.3;
            model.EnableTrash = true;
            model.TrashAutoCleanDays = 45;
            model.ShortcutTab1 = "Ctrl+9";
            model.ShortcutSearch = "Ctrl+Shift+F";
            model.HideLauncherOnGame = true;
            model.HighPerformanceGpu = true;
            model.MusicVolume = 50;
            model.BackgroundImageOpacity = 60;
            model.ProxyMode = "custom";
            model.ProxyUrl = "http://127.0.0.1:8080";
            model.DownloadThreads = 32;
            model.PlayerName = "TestPlayer";

            // 等待自动保存
            await Task.Delay(600);
            Dispatcher.UIThread.RunJobs();

            // 导出 JSON
            var json = SettingsService.ExportToJson(services.Settings);

            // 导入到新实例
            var imported = SettingsService.ImportFromJson(json);
            var services2 = new AppServices(imported);
            using var model2 = new SettingsViewModel(services2);

            Assert.Equal(1.3, model2.FontScale);
            Assert.True(model2.EnableTrash);
            Assert.Equal(45, model2.TrashAutoCleanDays);
            Assert.Equal("Ctrl+9", model2.ShortcutTab1);
            Assert.Equal("Ctrl+Shift+F", model2.ShortcutSearch);
            Assert.True(model2.HideLauncherOnGame);
            Assert.True(model2.HighPerformanceGpu);
            Assert.Equal(50m, model2.MusicVolume);
            Assert.Equal(60m, model2.BackgroundImageOpacity);
            Assert.Equal("custom", model2.ProxyMode);
            Assert.Equal("http://127.0.0.1:8080", model2.ProxyUrl);
            Assert.Equal(32m, model2.DownloadThreads);
            Assert.Equal("TestPlayer", model2.PlayerName);
        }
        finally
        {
            window.Close();
            WMCL.Core.ProxyManager.Apply(previousProxy);
        }
    }

    [AvaloniaFact]
    public void ConstructorLoadsShortcutsFromCustomShortcuts()
    {
        var settings = new AppSettings();
        settings.CustomShortcuts =
            "{\"Tab1\":\"Ctrl+7\",\"Tab2\":\"Ctrl+8\",\"Tab3\":\"Ctrl+9\"," +
            "\"Tab4\":\"Ctrl+0\",\"Tab5\":\"Ctrl+=\",\"Search\":\"Ctrl+P\"}";

        var services = new AppServices(settings);
        var model = new SettingsViewModel(services);

        Assert.Equal("Ctrl+7", model.ShortcutTab1);
        Assert.Equal("Ctrl+8", model.ShortcutTab2);
        Assert.Equal("Ctrl+9", model.ShortcutTab3);
        Assert.Equal("Ctrl+0", model.ShortcutTab4);
        Assert.Equal("Ctrl+=", model.ShortcutTab5);
        Assert.Equal("Ctrl+P", model.ShortcutSearch);
    }

    [AvaloniaFact]
    public void ConstructorUsesDefaultShortcutsWhenCustomShortcutsEmpty()
    {
        var services = new AppServices(new AppSettings());
        var model = new SettingsViewModel(services);

        Assert.Equal("Ctrl+1", model.ShortcutTab1);
        Assert.Equal("Ctrl+2", model.ShortcutTab2);
        Assert.Equal("Ctrl+3", model.ShortcutTab3);
        Assert.Equal("Ctrl+4", model.ShortcutTab4);
        Assert.Equal("Ctrl+5", model.ShortcutTab5);
        Assert.Equal("Ctrl+F", model.ShortcutSearch);
    }

    [AvaloniaFact]
    public void ConstructorUsesDefaultShortcutsWhenCustomShortcutsInvalid()
    {
        var settings = new AppSettings();
        settings.CustomShortcuts = "not valid json{{";

        var services = new AppServices(settings);
        var model = new SettingsViewModel(services);

        Assert.Equal("Ctrl+1", model.ShortcutTab1);
        Assert.Equal("Ctrl+F", model.ShortcutSearch);
    }
}
