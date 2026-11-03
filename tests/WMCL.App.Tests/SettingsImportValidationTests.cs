using System.Reflection;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class SettingsImportValidationTests
{
    [Theory]
    [InlineData("1e300")]
    [InlineData("-1")]
    public void InvalidExternalDownloadLimitIsRejectedBeforeApplication(string value)
    {
        Assert.Throws<InvalidDataException>(() => SettingsService.ImportFromJson(
            "{\"PlayerName\":\"Changed\",\"DownloadSpeedLimitMbps\":" + value + "}"));
    }

    [AvaloniaFact]
    public void InvalidImportDoesNotMutateSettingsOrScheduleSave()
    {
        var saves = 0;
        var settings = new AppSettings { PlayerName = "Original" };
        using var model = new SettingsViewModel(new AppServices(settings, saveSettings: _ => saves++));
        var exception = Assert.Throws<TargetInvocationException>(() => typeof(SettingsViewModel)
            .GetMethod("LoadFromSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [new AppSettings { PlayerName = "Changed", DownloadSpeedLimitMbps = 1e300 }]));
        Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.Equal("Original", settings.PlayerName);
        Assert.Equal("Original", model.PlayerName);
        Assert.Equal(0, saves);
    }

    [AvaloniaFact]
    public void ImportPersistsEvenWhenOnlyUnboundPropertiesChange()
    {
        var saves = 0;
        var settings = new AppSettings();
        using var model = new SettingsViewModel(new AppServices(settings, saveSettings: _ => saves++));
        typeof(SettingsViewModel).GetMethod("LoadFromSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [new AppSettings { Locale = "en-US", EasyTierPeer = "example.invalid" }]);
        Assert.Equal("en-US", settings.Locale);
        Assert.Equal("example.invalid", settings.EasyTierPeer);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void NullFolderMapValuesAreNormalizedWithoutCrashingRecovery()
    {
        var settings = AppSettingsSerializer.Deserialize("""{"FavoriteVersionsByFolder":{"/tmp":null},"HiddenVersionsByFolder":{"/tmp":null}}""");
        Assert.All(settings.FavoriteVersionsByFolder.Values, Assert.Empty);
        Assert.All(settings.HiddenVersionsByFolder.Values, Assert.Empty);
    }

    [Fact]
    public void InvalidNumericConfigurationRecoversFromBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-settings-numeric-" + Guid.NewGuid().ToString("N"));
        var store = new FileAppSettingsStore(Path.Combine(root, "config.json"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(store.FilePath, """{"PlayerName":"Changed","DownloadSpeedLimitMbps":1e300}""");
            File.WriteAllText(store.BackupPath, AppSettingsSerializer.Serialize(new AppSettings { PlayerName = "Backup" }));
            var result = store.LoadWithStatus();
            Assert.Equal("Backup", result.Settings.PlayerName);
            Assert.NotNull(result.Warning);
        }
        finally { Directory.Delete(root, true); }
    }
}
