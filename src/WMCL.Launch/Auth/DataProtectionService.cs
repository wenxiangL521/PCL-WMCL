using System.Security.Cryptography;
using System.Text;
using WMCL.Core;

namespace WMCL.Launch.Auth;

/// <summary>
/// 启动器敏感数据保护服务。使用 AES-256-GCM 加密磁盘上的凭据文件。
/// 密钥从机器标识派生，防止随意读取；不提供真正的跨机器安全，但阻止明文暴露。
/// </summary>
public static class DataProtectionService
{
    private static readonly byte[] Salt =
        "WMCL-DataProtection-v1"u8.ToArray();

    /// <summary>加密明文字符串。</summary>
    public static string Encrypt(string plaintext)
    {
        var key = DeriveKey();
        using var gcm = new AesGcm(key, 16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];
        gcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        // 格式：版本(1) + 盐(Salt) + 随机数(12) + 密文 + 标签(16)
        var result = new byte[1 + Salt.Length + nonce.Length + cipherBytes.Length + tag.Length];
        result[0] = 1; // 版本
        Buffer.BlockCopy(Salt, 0, result, 1, Salt.Length);
        Buffer.BlockCopy(nonce, 0, result, 1 + Salt.Length, nonce.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, 1 + Salt.Length + nonce.Length, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, result, 1 + Salt.Length + nonce.Length + cipherBytes.Length, tag.Length);
        return Convert.ToBase64String(result);
    }

    /// <summary>解密字符串。失败返回 null。</summary>
    public static string? Decrypt(string ciphertext)
    {
        try
        {
            var data = Convert.FromBase64String(ciphertext);
            if (data.Length < 1 + Salt.Length + 12 + 16) return null;
            if (data[0] != 1) return null;
            var saltOffset = 1;
            var salt = new byte[Salt.Length];
            Buffer.BlockCopy(data, saltOffset, salt, 0, Salt.Length);
            if (!salt.AsSpan().SequenceEqual(Salt)) return null;
            var nonceOffset = saltOffset + Salt.Length;
            var nonce = new byte[12];
            Buffer.BlockCopy(data, nonceOffset, nonce, 0, 12);
            // 布局：[version][salt][nonce][cipher][tag]
            var tagLength = 16;
            var cipherOffset = nonceOffset + 12;
            var cipherLength = data.Length - cipherOffset - tagLength;
            if (cipherLength < 0) return null;
            var tag = new byte[tagLength];
            Buffer.BlockCopy(data, cipherOffset + cipherLength, tag, 0, tagLength);
            var cipherBytes = new byte[cipherLength];
            Buffer.BlockCopy(data, cipherOffset, cipherBytes, 0, cipherLength);
            var key = DeriveKey(salt);
            using var gcm = new AesGcm(key, 16);
            var plainBytes = new byte[cipherBytes.Length];
            gcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从机器标识派生 AES-256 密钥。</summary>
    private static byte[] DeriveKey(byte[]? salt = null)
    {
        var saltBytes = salt ?? Salt;
        var machineId = GetMachineId();
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(machineId),
            saltBytes,
            100_000,
            HashAlgorithmName.SHA256,
            32);
    }

    /// <summary>获取机器唯一标识（跨平台尽力而为）。</summary>
    private static string GetMachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 使用机器 GUID
                var reg = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
                var machineGuid = reg?.GetValue("MachineGuid") as string;
                if (!string.IsNullOrEmpty(machineGuid))
                    return "Windows:" + machineGuid;
            }
            else if (OperatingSystem.IsLinux())
            {
                var machineId = File.ReadAllText("/etc/machine-id").Trim();
                if (!string.IsNullOrEmpty(machineId))
                    return "Linux:" + machineId;
                // fallback: /var/lib/dbus/machine-id
                if (File.Exists("/var/lib/dbus/machine-id"))
                    return "Linux:" + File.ReadAllText("/var/lib/dbus/machine-id").Trim();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var ioreg = RunCommand("ioreg", "-rd1 -c IOPlatformExpertDevice");
                var uuid = ExtractPlatformUuid(ioreg);
                if (uuid is not null)
                    return "Mac:" + uuid;
            }
        }
        catch
        {
            // 获取失败时回退到用户名+主机名
        }
        return "Fallback:" + Environment.UserName + ":" + Environment.MachineName;
    }

    /// <summary>
    /// 从 ioreg 输出中提取 IOPlatformUUID。
    /// 输出行形如 <c>"IOPlatformUUID" = "3D82A4C5-…"</c>：
    /// 按引号切分后 [1] 是属性名、[3] 才是 UUID 值。
    /// </summary>
    internal static string? ExtractPlatformUuid(string? ioregOutput)
    {
        if (string.IsNullOrEmpty(ioregOutput)) return null;
        var line = ioregOutput.Split('\n')
            .FirstOrDefault(l => l.Contains("IOPlatformUUID"));
        var parts = line?.Split('"');
        if (parts is { Length: > 3 } && Guid.TryParse(parts[3].Trim(), out _))
            return parts[3].Trim();
        return null;
    }

    private static string? RunCommand(string fileName, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null) return null;
        try
        {
            // 并发读取 stdout/stderr（各自在任务上排空管道）：
            // 先同步读满一个再读另一个，在子进程写满另一条管道
            // 缓冲（~64KB）时会死锁；ioreg 输出小，实际风险低但模式必须正确。
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            Task.WaitAll([outputTask, errorTask], TimeSpan.FromSeconds(2));
            return process.ExitCode == 0 ? outputTask.IsCompleted ? outputTask.Result : null : null;
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return null;
        }
    }
}
