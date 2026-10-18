using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
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

    public static async Task<UpdateInfo?> CheckAsync(
        string manifestUrl,
        string currentVersion,
        CancellationToken ct = default)
    {
        var json = await WMCL.Core.Http.Http
            .GetStringAsync(manifestUrl, ct, TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
        var info = ParseManifest(json);
        if (info is null || string.IsNullOrWhiteSpace(info.Url)) return null;
        return IsNewer(info.Version, currentVersion) ? info : null;
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
        await using (var file = File.OpenRead(archivePath))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
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

        var executable = Directory.EnumerateFiles(staging, "WMCL", SearchOption.AllDirectories)
                             .FirstOrDefault()
                         ?? Directory.EnumerateFiles(staging, "WMCL.App", SearchOption.AllDirectories)
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

    /// <summary>当前运行形态是否支持原地替换（打包后的单文件 WMCL）。</summary>
    public static bool CanApplyInPlace =>
        OperatingSystem.IsLinux() &&
        string.Equals(Path.GetFileName(Environment.ProcessPath), "WMCL",
            StringComparison.Ordinal);

    /// <summary>
    /// 生成等待当前进程退出后替换并重启的脚本。调用方随后应立即退出。
    /// </summary>
    public static void ApplyAndRestart(PreparedUpdate update)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("当前仅支持 Linux 自动替换。");

        var target = Environment.ProcessPath
                     ?? throw new InvalidOperationException("无法获取当前可执行文件路径。");
        var script = Path.Combine(
            WmclPaths.CacheDirectory, "updates", "apply-update.sh");
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
