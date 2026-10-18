using System.Text.Json;
using PCL.Core;

namespace PCL.Launch.Auth;

/// <summary>微软账号缓存（refresh token 用于后续续期）。Linux 下以 0600 权限保存。</summary>
public static class MicrosoftAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath =>
        Path.Combine(PclPaths.DataDirectory, "account", "microsoft.json");

    public static MicrosoftAccount? Load(string? path = null)
    {
        try
        {
            var file = path ?? FilePath;
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<MicrosoftAccount>(
                File.ReadAllText(file), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(MicrosoftAccount account, string? path = null)
    {
        var file = path ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(account, JsonOptions));

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // 某些文件系统不支持 chmod
            }
        }
    }

    public static void Clear(string? path = null)
    {
        var file = path ?? FilePath;
        if (File.Exists(file)) File.Delete(file);
    }
}
