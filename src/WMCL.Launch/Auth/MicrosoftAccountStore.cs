using System.Text.Json;
using WMCL.Core;

namespace WMCL.Launch.Auth;

/// <summary>微软账号缓存（refresh token 用于后续续期）。Linux 下以 0600 权限保存。</summary>
public static class MicrosoftAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath =>
        Path.Combine(WmclPaths.DataDirectory, "account", "microsoft.json");

    public static MicrosoftAccount? Load(string? path = null)
    {
        try
        {
            var file = path ?? FilePath;
            if (!File.Exists(file)) return null;
            var raw = File.ReadAllText(file);
            // 先尝试解密，失败则回退到明文 JSON（向后兼容）
            var json = DataProtectionService.Decrypt(raw) ?? raw;
            return JsonSerializer.Deserialize<MicrosoftAccount>(json, JsonOptions);
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
        var json = JsonSerializer.Serialize(account, JsonOptions);
        // 加密后写入，防止明文凭据泄露。
        var encrypted = DataProtectionService.Encrypt(json);
        // 先写临时文件再原子替换：进程在写入中途崩溃/断电时，
        // 半截密文不会破坏原账号文件（否则用户必须重新登录）。
        var temp = file + ".tmp";
        File.WriteAllText(temp, encrypted);
        File.Move(temp, file, overwrite: true);

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
