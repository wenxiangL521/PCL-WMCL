using System.Text.Json;
using WMCL.Core;

namespace WMCL.App.Services;

public interface IAppSettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed record AppSettingsLoadResult(AppSettings Settings, string? Warning = null);

public sealed class FileAppSettingsStore : IAppSettingsStore
{
    private static readonly object Sync = new();

    public FileAppSettingsStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Settings path is required.", nameof(filePath));
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }
    public string BackupPath => FilePath + ".bak";

    public AppSettings Load() => LoadWithStatus().Settings;

    public AppSettingsLoadResult LoadWithStatus()
    {
        lock (Sync)
        {
            if (!File.Exists(FilePath) && !File.Exists(BackupPath)) return new(NewSettings());
            try
            {
                return new(AppSettingsSerializer.Deserialize(File.ReadAllText(FilePath)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
            {
                try
                {
                    var recovered = AppSettingsSerializer.Deserialize(File.ReadAllText(BackupPath));
                    return new(recovered, $"配置读取失败，已从备份恢复：{BackupPath}。原配置尚未覆盖。请检查磁盘与配置文件。");
                }
                catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
                {
                    return new(NewSettings(), $"配置及备份无法读取，暂用默认设置：{FilePath}。原文件尚未覆盖；下次保存会保留损坏配置副本。");
                }
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppSettingsNormalizer.Normalize(settings);
        var json = AppSettingsSerializer.Serialize(settings);
        lock (Sync)
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".wmcl-config-" + Guid.NewGuid().ToString("N") + ".tmp");
            var backupTemporary = temporary + ".backup";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, leaveOpen: true))
                    {
                        writer.Write(json);
                        writer.Flush();
                    }
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(FilePath))
                {
                    var valid = true;
                    try { AppSettingsSerializer.Deserialize(File.ReadAllText(FilePath)); }
                    catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidDataException) { valid = false; }
                    if (valid)
                    {
                        File.Copy(FilePath, backupTemporary, overwrite: false);
                        File.Move(backupTemporary, BackupPath, overwrite: true);
                    }
                    else
                        File.Copy(FilePath, FilePath + ".corrupt-" + Guid.NewGuid().ToString("N"), overwrite: false);
                }
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (File.Exists(backupTemporary)) File.Delete(backupTemporary);
            }
        }
    }

    private static AppSettings NewSettings()
    {
        var settings = new AppSettings();
        AppSettingsNormalizer.Normalize(settings);
        return settings;
    }
}

public static class AppSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, Options);
    }

    public static AppSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        AppSettingsNormalizer.Normalize(settings);
        AppSettingsImportPolicy.Validate(settings);
        return settings;
    }

    public static string ExportToJson(AppSettings settings)
    {
        AppSettingsNormalizer.Normalize(settings);
        return Serialize(RedactSecrets(settings));
    }

    private static AppSettings RedactSecrets(AppSettings settings)
    {
        var clone = Deserialize(Serialize(settings));
        SensitiveTextRedactor.RedactSettings(clone);
        return clone;
    }
}

public static class AppSettingsNormalizer
{
    public static void Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ApplicationOpacity = Math.Clamp(settings.ApplicationOpacity, 0, 100);
        settings.GameDirectory = WmclPaths.Expand(settings.GameDirectory);
        settings.GameFolders ??= [];
        settings.FavoriteVersions ??= [];
        settings.HiddenVersions ??= [];
        settings.FavoriteDownloadComponents ??= [];
        settings.FavoriteVersionsByFolder = NormalizeFolderMap(
            settings.FavoriteVersionsByFolder, settings.FavoriteVersions, settings.GameDirectory);
        settings.HiddenVersionsByFolder = NormalizeFolderMap(
            settings.HiddenVersionsByFolder, settings.HiddenVersions, settings.GameDirectory);
        settings.GameFolderNames ??= new(StringComparer.OrdinalIgnoreCase);
        settings.GameFolders = settings.GameFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(WmclPaths.Expand)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!settings.GameFolders.Contains(settings.GameDirectory, StringComparer.OrdinalIgnoreCase))
            settings.GameFolders.Add(settings.GameDirectory);
    }

    private static Dictionary<string, List<string>> NormalizeFolderMap(
        Dictionary<string, List<string>>? map,
        List<string> legacy,
        string activeFolder)
    {
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, ids) in map ?? [])
        {
            var full = WmclPaths.Expand(folder);
            normalized[full] = (ids ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if (normalized.Count == 0 && legacy.Count > 0)
            normalized[activeFolder] = legacy
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        return normalized;
    }
}
