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

    /// <summary>多账号列表文件（加密 JSON 数组），与单账号文件同目录。</summary>
    public static string ListPath => ListPathFor(FilePath);

    /// <summary>由单账号文件路径推出同目录的列表文件路径（便于测试隔离）。</summary>
    public static string ListPathFor(string? accountPath)
    {
        var directory = Path.GetDirectoryName(
            string.IsNullOrWhiteSpace(accountPath) ? FilePath : accountPath);
        return Path.Combine(directory ?? Path.Combine(WmclPaths.DataDirectory, "account"), "microsoft-accounts.json");
    }

    public static List<MicrosoftAccount> LoadAll(string? path = null)
    {
        var file = path ?? ListPath;
        try
        {
            if (!File.Exists(file)) return [];
            var raw = File.ReadAllText(file);
            var json = DataProtectionService.Decrypt(raw) ?? raw;
            return JsonSerializer.Deserialize<List<MicrosoftAccount>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void SaveAll(IReadOnlyList<MicrosoftAccount> accounts, string? path = null)
    {
        var file = path ?? ListPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var json = JsonSerializer.Serialize(accounts, JsonOptions);
        var encrypted = DataProtectionService.Encrypt(json);
        var temp = file + ".tmp";
        File.WriteAllText(temp, encrypted);
        File.Move(temp, file, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void Clear(string? path = null)
    {
        var file = path ?? FilePath;
        if (File.Exists(file)) File.Delete(file);
    }
}
