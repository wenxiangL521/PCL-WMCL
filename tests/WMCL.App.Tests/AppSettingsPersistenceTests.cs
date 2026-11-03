using WMCL.App.Services;

namespace WMCL.App.Tests;

public sealed class AppSettingsPersistenceTests
{
    [Fact]
    public void FileStore_RoundTripsUsingExplicitPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-settings-store-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "settings.json");
        try
        {
            var store = new FileAppSettingsStore(path);
            var settings = new AppSettings
            {
                GameDirectory = "~/wmcl-test-games",
                MemoryMb = 8192,
                PlayerName = "StorageTest"
            };

            store.Save(settings);
            var loaded = store.Load();

            Assert.True(File.Exists(path));
            Assert.Equal(WMCL.Core.WmclPaths.Expand("~/wmcl-test-games"), loaded.GameDirectory);
            Assert.Contains(loaded.GameFolders, folder =>
                string.Equals(folder, loaded.GameDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(8192, loaded.MemoryMb);
            Assert.Equal("StorageTest", loaded.PlayerName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FileStore_CorruptFileReturnsNormalizedDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-settings-corrupt-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "{ not valid json");
        try
        {
            var store = new FileAppSettingsStore(path);
            var result = store.LoadWithStatus();
            var loaded = result.Settings;

            Assert.NotNull(result.Warning);
            Assert.Equal("{ not valid json", File.ReadAllText(path));
            Assert.Equal(4096, loaded.MemoryMb);
            Assert.Contains(loaded.GameFolders, folder =>
                string.Equals(folder, loaded.GameDirectory, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveKeepsLastGoodBackupAndRecoversWithoutOverwritingCorruptFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-settings-recovery-" + Guid.NewGuid().ToString("N"));
        var store = new FileAppSettingsStore(Path.Combine(root, "settings.json"));
        try
        {
            store.Save(new AppSettings { PlayerName = "First" });
            store.Save(new AppSettings { PlayerName = "Second" });
            Assert.Equal("First", AppSettingsSerializer.Deserialize(File.ReadAllText(store.BackupPath)).PlayerName);
            File.WriteAllText(store.FilePath, "{ corrupt");
            var result = store.LoadWithStatus();
            Assert.Equal("First", result.Settings.PlayerName);
            Assert.Contains("已从备份恢复", result.Warning);
            Assert.Equal("{ corrupt", File.ReadAllText(store.FilePath));
            store.Save(result.Settings);
            Assert.Equal("{ corrupt", File.ReadAllText(Assert.Single(Directory.GetFiles(root, "*.corrupt-*"))));
            Assert.Equal("First", AppSettingsSerializer.Deserialize(File.ReadAllText(store.BackupPath)).PlayerName);
            Assert.Equal("First", store.Load().PlayerName);
            Assert.Empty(Directory.GetFiles(root, ".wmcl-config-*"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidNewSettingsNeverTruncateCurrentConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-settings-atomic-" + Guid.NewGuid().ToString("N"));
        var store = new FileAppSettingsStore(Path.Combine(root, "settings.json"));
        try
        {
            store.Save(new AppSettings { PlayerName = "Original" });
            var original = File.ReadAllText(store.FilePath);
            Assert.Throws<ArgumentException>(() => store.Save(new AppSettings { FontScale = double.NaN }));
            Assert.Equal(original, File.ReadAllText(store.FilePath));
            Assert.Empty(Directory.GetFiles(root, ".wmcl-config-*"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedCommitKeepsDestinationAndCleansTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-settings-commit-" + Guid.NewGuid().ToString("N"));
        var store = new FileAppSettingsStore(Path.Combine(root, "settings.json"));
        Directory.CreateDirectory(store.FilePath);
        var marker = Path.Combine(store.FilePath, "existing.txt");
        File.WriteAllText(marker, "keep");
        try
        {
            Assert.ThrowsAny<IOException>(() => store.Save(new AppSettings()));
            Assert.Equal("keep", File.ReadAllText(marker));
            Assert.Empty(Directory.GetFiles(root, ".wmcl-config-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingPrimaryUsesBackupAndReportsRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-settings-backup-" + Guid.NewGuid().ToString("N"));
        var store = new FileAppSettingsStore(Path.Combine(root, "settings.json"));
        Directory.CreateDirectory(root);
        File.WriteAllText(store.BackupPath, AppSettingsSerializer.Serialize(new AppSettings { PlayerName = "Backup" }));
        try
        {
            var result = store.LoadWithStatus();
            Assert.Equal("Backup", result.Settings.PlayerName);
            Assert.NotNull(result.Warning);
            Assert.False(File.Exists(store.FilePath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SettingsService_UsesCurrentXdgConfigPath()
    {
        var first = Path.Combine(Path.GetTempPath(), "wmcl-settings-xdg-a-" + Guid.NewGuid().ToString("N"));
        var second = Path.Combine(Path.GetTempPath(), "wmcl-settings-xdg-b-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", first);
            SettingsService.Save(new AppSettings { PlayerName = "First" });
            Assert.Equal("First", SettingsService.Load().PlayerName);

            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", second);
            Assert.Equal("Steve", SettingsService.Load().PlayerName);
            SettingsService.Save(new AppSettings { PlayerName = "Second" });
            Assert.Equal("Second", SettingsService.Load().PlayerName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
            if (Directory.Exists(first)) Directory.Delete(first, true);
            if (Directory.Exists(second)) Directory.Delete(second, true);
        }
    }
}
