using System.Reflection;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class SettingsSecurityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalImportsNeverEnableExecutionSettings(bool fromFile)
    {
        var settings = DangerousSettings();
        var json = AppSettingsSerializer.Serialize(settings);
        var path = Path.Combine(Path.GetTempPath(), "wmcl-untrusted-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            var imported = fromFile ? SettingsService.ImportFromFile(path) : SettingsService.ImportFromJson(json);
            var defaults = new AppSettings();
            foreach (var property in typeof(AppSettings).GetProperties())
                if (AppSettingsImportPolicy.IsProtectedProperty(property.Name))
                    Assert.Equal(property.GetValue(defaults), property.GetValue(imported));
            Assert.Equal(8192, imported.MemoryMb);
            Assert.Equal("dark", imported.Theme);
            Assert.Equal("run-untrusted-command", settings.PreLaunchCommand);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TrustedLocalConfigurationRetainsExecutionSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-trusted-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileAppSettingsStore(Path.Combine(directory, "config.json"));
            store.Save(DangerousSettings());
            var loaded = store.Load();
            Assert.Equal("run-untrusted-command", loaded.PreLaunchCommand);
            Assert.Equal("custom-java", loaded.JavaPath);
            Assert.Equal("-javaagent:untrusted.jar", loaded.ExtraJvmArgs);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void ApplyingImportedSettingsKeepsLocallyTrustedExecutionValues()
    {
        var local = DangerousSettings();
        local.PreLaunchCommand = "local-command";
        local.JavaPath = "local-java";
        using var model = new SettingsViewModel(new AppServices(local, saveSettings: _ => { }));
        var imported = SettingsService.ImportFromJson(AppSettingsSerializer.Serialize(DangerousSettings()));
        typeof(SettingsViewModel).GetMethod("LoadFromSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [imported]);
        Assert.Equal("local-command", model.PreLaunchCommand);
        Assert.Equal("local-java", model.JavaPath);
        Assert.Equal("-javaagent:untrusted.jar", model.GlobalJvmArgs);
        Assert.Equal(8192, model.MemoryMb);
    }

    private static AppSettings DangerousSettings() => new()
    {
        PreLaunchCommand = "run-untrusted-command",
        PreLaunchWait = false,
        JavaPath = "custom-java",
        ExtraJvmArgs = "-javaagent:untrusted.jar",
        ExtraGameArgs = "--untrusted",
        CustomHomePagePath = "untrusted.axaml",
        TerracottaExecutablePath = "untrusted-program",
        MemoryMb = 8192,
        Theme = "dark"
    };
}
