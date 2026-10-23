using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Core.Http;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;

namespace WMCL.Core.Update;

public sealed record UpdateInfo(
    string Version,
    string Url,
    string? Sha256 = null,
    string? Notes = null,
    long? Size = null);

public sealed record PreparedUpdate(
    string Version,
    string StagingDirectory,
    string ExecutablePath);

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    InvalidManifest,
    MissingUrl,
    IncomparableVersion
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateInfo? Update = null,
    string? Error = null);

/// <summary>
/// 自研更新清单（latest.json）。原版 PCL2 开源代码中更新逻辑为空实现，因此这里定义自己的格式：
/// { "version": "0.1.1", "url": "https://.../WMCL-linux-x64.tar.gz", "sha256": "...", "notes": "..." }
/// </summary>
public static class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static UpdateInfo? ParseManifest(string json) =>
        JsonSerializer.Deserialize<UpdateInfo>(json, JsonOptions);

    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParseVersion(candidate, out var candidateVersion) ||
            !TryParseVersion(current, out var currentVersion))
            return false;
        return candidateVersion > currentVersion;
    }

    public static async Task<UpdateCheckResult> CheckAsync(
        string manifestUrl,
        string currentVersion,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return new(UpdateCheckStatus.MissingUrl, Error: "未配置更新源。");
        var json = await WMCL.Core.Http.Http
            .GetStringAsync(manifestUrl, ct, TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
        return EvaluateManifest(json, currentVersion);
    }

    /// <summary>把清单解析和版本判断分开，便于对无效清单/不可比较版本给出明确错误。</summary>
    public static UpdateCheckResult EvaluateManifest(string json, string currentVersion)
    {
        UpdateInfo? info;
        try
        {
            info = ParseManifest(json);
        }
        catch (JsonException exception)
        {
            return new(UpdateCheckStatus.InvalidManifest,
                Error: "更新清单不是有效 JSON：" + exception.Message);
        }
        if (info is null || string.IsNullOrWhiteSpace(info.Version))
            return new(UpdateCheckStatus.InvalidManifest, Error: "更新清单缺少 version。");
        if (string.IsNullOrWhiteSpace(info.Url))
            return new(UpdateCheckStatus.MissingUrl, Error: "更新清单缺少下载 url。");
        if (!TryParseVersion(info.Version, out var candidate) ||
            !TryParseVersion(currentVersion, out var current))
            return new(UpdateCheckStatus.IncomparableVersion,
                Error: $"无法比较版本：清单 {info.Version}，当前 {currentVersion}。");
        return candidate > current
            ? new(UpdateCheckStatus.UpdateAvailable, info)
            : new(UpdateCheckStatus.UpToDate);
    }

    public static async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var directory = Path.Combine(WmclPaths.CacheDirectory, "updates");
        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileName(new Uri(info.Url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "WMCL-update.tar.gz";
        var destination = Path.Combine(directory, fileName);
        var part = destination + ".part";

        using var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");

        using var response = await client
            .GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? info.Size ?? 0;
        await using var output = new FileStream(
            part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var buffer = new byte[81920];
        long downloaded = 0;
        var clock = Stopwatch.StartNew();
        long lastReport = 0;

        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;

            if (clock.ElapsedMilliseconds - lastReport >= 200)
            {
                var speed = clock.ElapsedMilliseconds > 0
                    ? downloaded * 1000 / clock.ElapsedMilliseconds
                    : 0;
                progress?.Report(new JobProgress(
                    "下载更新",
                    total > 0 ? (double)downloaded / total : null,
                    speed,
                    null,
                    $"{downloaded / 1048576.0:0.0} / {total / 1048576.0:0.0} MB"));
                lastReport = clock.ElapsedMilliseconds;
            }
        }

        await output.FlushAsync(ct).ConfigureAwait(false);
        await output.DisposeAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            await using var check = File.OpenRead(part);
            var actual = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(check, ct).ConfigureAwait(false));
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                throw new InvalidDataException(
                    $"更新包校验失败：期望 {info.Sha256}，实际 {actual}");
            }
        }

        File.Move(part, destination, overwrite: true);
        Log.Info($"更新包已下载：{destination}");
        return destination;
    }

    /// <summary>解压更新包，定位包内的 WMCL 可执行文件。</summary>
    public static async Task<PreparedUpdate> PrepareAsync(
        string archivePath,
        string version,
        CancellationToken ct = default)
    {
        var staging = Path.Combine(
            WmclPaths.CacheDirectory, "updates", "staging", Sanitize(version));
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        if (IsZipArchive(archivePath))
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') ||
                    name.Split('/').Contains(".."))
                    throw new InvalidDataException($"更新包包含非法路径：{entry.FullName}");
                var destination = Path.GetFullPath(Path.Combine(staging, name));
                if (!destination.StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidDataException($"更新包路径越界：{entry.FullName}");
                if (name.EndsWith('/'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = entry.Open();
                await using var output = File.Create(destination);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }
        }
        else
        {
            await using var file = File.OpenRead(archivePath);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync(cancellationToken: ct)
                       .ConfigureAwait(false)) is not null)
            {
                var name = entry.Name.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') ||
                    name.Split('/').Contains(".."))
                    throw new InvalidDataException($"更新包包含非法路径：{entry.Name}");

                var destination = Path.GetFullPath(Path.Combine(staging, name));
                if (!destination.StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidDataException($"更新包路径越界：{entry.Name}");

                if (entry.EntryType is TarEntryType.Directory)
                {
                    Directory.CreateDirectory(destination);
                }
                else if (entry.EntryType is TarEntryType.RegularFile)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await entry.ExtractToFileAsync(destination, overwrite: true, ct)
                        .ConfigureAwait(false);
                }
            }
        }

        var executable = new[] { "WMCL.exe", "WMCL", "WMCL.App" }
                             .SelectMany(name => Directory.EnumerateFiles(staging, name, SearchOption.AllDirectories))
                             .FirstOrDefault()
                         ?? throw new InvalidDataException("更新包中未找到 WMCL 可执行文件。");

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
                // 某些文件系统不支持 chmod
            }
        }

        return new PreparedUpdate(version, staging, executable);
    }

    /// <summary>当前运行形态是否支持原地替换。</summary>
    public static bool CanApplyInPlace
    {
        get
        {
            var process = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(process)) return false;
            if (OperatingSystem.IsLinux())
                return string.Equals(Path.GetFileName(process), "WMCL", StringComparison.Ordinal);
            if (OperatingSystem.IsWindows())
                return string.Equals(Path.GetFileName(process), "WMCL.exe", StringComparison.OrdinalIgnoreCase);
            if (OperatingSystem.IsMacOS())
                return process.Replace('\\', '/').Contains(".app/Contents/MacOS/WMCL", StringComparison.Ordinal);
            return false;
        }
    }

    /// <summary>
    /// 生成等待当前进程退出后替换并重启的脚本。调用方随后应立即退出。
    /// </summary>
    public static void ApplyAndRestart(PreparedUpdate update)
    {
        var target = Environment.ProcessPath
                     ?? throw new InvalidOperationException("无法获取当前可执行文件路径。");
        if (OperatingSystem.IsWindows())
            ApplyWindows(update, target);
        else if (OperatingSystem.IsMacOS())
            ApplyMac(update, target);
        else if (OperatingSystem.IsLinux())
            ApplyLinux(update, target);
        else
            throw new PlatformNotSupportedException("当前平台不支持自动替换。");
    }

    [SupportedOSPlatform("linux")]
    private static void ApplyLinux(PreparedUpdate update, string target)
    {
        var script = Path.Combine(WmclPaths.CacheDirectory, "updates", "apply-update.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        var log = Path.Combine(WmclPaths.LogsDirectory, "update.log");
        var content = $"""
            #!/bin/bash
            set -e
            pid=$1
            while kill -0 "$pid" 2>/dev/null; do sleep 0.2; done
            cp -f {Quote(update.ExecutablePath)} {Quote(target + ".new")}
            chmod +x {Quote(target + ".new")}
            mv -f {Quote(target + ".new")} {Quote(target)}
            cd {Quote(Path.GetDirectoryName(target)!)}
            {Quote(target)} >> {Quote(log)} 2>&1 &
            rm -f "$0"
            """;
        File.WriteAllText(script, content + Environment.NewLine);
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var startInfo = new ProcessStartInfo("/bin/bash")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(startInfo);
        Log.Info($"已启动更新替换脚本，目标：{target}");
    }

    private static void ApplyWindows(PreparedUpdate update, string target)
    {
        var script = Path.Combine(WmclPaths.CacheDirectory, "updates", "apply-update.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        var sourceDirectory = Path.GetDirectoryName(update.ExecutablePath)
                              ?? throw new InvalidDataException("更新包可执行文件没有目录。");
        var transfer = BuildWindowsApplyScript(sourceDirectory, Path.GetDirectoryName(target)!, target);
        File.WriteAllText(script, transfer, new System.Text.UTF8Encoding(false));
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(startInfo);
        Log.Info($"已启动 Windows 更新替换脚本，目标：{target}");
    }

    [SupportedOSPlatform("macos")]
    private static void ApplyMac(PreparedUpdate update, string target)
    {
        var sourceApp = FindAppRoot(update.ExecutablePath);
        var targetApp = FindAppRoot(target);
        var script = Path.Combine(WmclPaths.CacheDirectory, "updates", "apply-update.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        var content = BuildMacApplyScript(sourceApp, targetApp,
            Path.Combine(WmclPaths.LogsDirectory, "update.log"));
        File.WriteAllText(script, content + Environment.NewLine);
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var startInfo = new ProcessStartInfo("/bin/bash")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(startInfo);
        Log.Info($"已启动 macOS 更新替换脚本，目标：{targetApp}");
    }

    internal static string BuildWindowsApplyScript(
        string sourceDirectory, string targetDirectory, string targetExecutable) => $"""
        @echo off
        setlocal
        :wait
        tasklist /FI "PID eq %1" 2>NUL | find "%1" >NUL
        if not errorlevel 1 (
          ping 127.0.0.1 -n 2 >NUL
          goto wait
        )
        xcopy "{sourceDirectory}\*" "{targetDirectory}\" /E /I /Y /Q >NUL
        start "" "{targetExecutable}"
        del "%~f0"
        """;

    internal static string BuildMacApplyScript(string sourceApp, string targetApp, string log) => $"""
        #!/bin/bash
        set -e
        exec >> {Quote(log)} 2>&1
        pid=$1
        while kill -0 "$pid" 2>/dev/null; do sleep 0.2; done
        ditto {Quote(sourceApp)} {Quote(targetApp)}
        open {Quote(targetApp)}
        rm -f "$0"
        """;

    private static string FindAppRoot(string executable)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(executable)
                                          ?? throw new InvalidDataException("可执行文件没有目录。"));
        while (directory is not null && !directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidDataException("无法定位 WMCL.app 应用包。");
    }

    private static bool IsZipArchive(string path)
    {
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return true;
        using var stream = File.OpenRead(path);
        var signature = new byte[2];
        return stream.Read(signature, 0, 2) == 2 && signature[0] == (byte)'P' && signature[1] == (byte)'K';
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "update" : result;
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    private static bool TryParseVersion(string text, out Version version)
    {
        var normalized = text.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }
}
