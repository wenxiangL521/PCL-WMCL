using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Core.Logging;
using PCL.Download;
using PCL.Minecraft;

namespace PCL.Modding.OptiFine;

public sealed record OptiFineVersion(
    string Minecraft,
    string Type,
    string Patch,
    string FileName,
    string? ForgeVersion)
{
    public bool IsPreview => Patch.Contains("pre", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => $"{Minecraft} {Type.Replace("HD_U", "").Trim()} {Patch}".Trim();
}

/// <summary>
/// OptiFine 安装。对照 PCL：官方页面取签名下载地址 → 下载安装器 →
/// 在临时 user.home/.minecraft 中准备原版 JSON/jar → 运行 optifine.Installer →
/// 合并回游戏目录 → VersionInstaller 补全。
/// </summary>
public sealed class OptiFineService
{
    private const string ListUrl = "https://bmclapi2.bangbang93.com/optifine/versionList";
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;
    private readonly HttpClient _http;

    public OptiFineService(
        VersionRepository repository,
        VersionInstaller? installer = null,
        HttpClient? http = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(20)
        })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
    }

    public static IReadOnlyList<OptiFineVersion> ParseVersions(string json, string minecraft)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

        var result = new List<OptiFineVersion>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var mc = element.TryGetProperty("mcversion", out var mcElement)
                ? mcElement.GetString()
                : null;
            if (!string.Equals(mc, minecraft, StringComparison.OrdinalIgnoreCase)) continue;

            var type = element.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString() ?? ""
                : "";
            var patch = element.TryGetProperty("patch", out var patchElement)
                ? patchElement.GetString() ?? ""
                : "";
            var filename = element.TryGetProperty("filename", out var fileElement)
                ? fileElement.GetString() ?? ""
                : "";
            var forge = element.TryGetProperty("forge", out var forgeElement)
                ? forgeElement.GetString()
                : null;
            var forgeVersion = forge?
                .Replace("Forge ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("#", "")
                .Trim();
            if (string.IsNullOrWhiteSpace(forgeVersion) || forgeVersion.Contains("N/A"))
                forgeVersion = null;

            if (filename.Length > 0)
                result.Add(new OptiFineVersion(mc!, type, patch, filename, forgeVersion));
        }

        return result;
    }

    public async Task<IReadOnlyList<OptiFineVersion>> GetVersionsAsync(
        string minecraft,
        CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync(ListUrl, ct).ConfigureAwait(false);
        return ParseVersions(json, minecraft)
            .OrderByDescending(version => version.IsPreview ? 0 : 1)
            .ThenByDescending(version => version.Patch, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>确保 OptiFine 安装器已缓存，并返回版本与 jar 路径；不运行安装器。</summary>
    public async Task<(OptiFineVersion Version, string Path)> EnsureInstallerAsync(
        string minecraft,
        string? patch = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress("获取 OptiFine 版本", 0.01));
        var versions = await GetVersionsAsync(minecraft, ct).ConfigureAwait(false);
        var version = patch is null
            ? versions.FirstOrDefault(item => !item.IsPreview) ?? versions.FirstOrDefault()
            : versions.FirstOrDefault(item =>
                  string.Equals(item.Patch, patch, StringComparison.OrdinalIgnoreCase) ||
                  item.FileName.Contains(patch, StringComparison.OrdinalIgnoreCase));
        if (version is null)
            throw new InvalidOperationException($"没有找到 OptiFine {minecraft} {patch}。");

        var cache = Path.Combine(PclPaths.CacheDirectory, "optifine", version.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        if (!IsZip(cache))
        {
            var url = await GetOfficialDownloadUrlAsync(version.FileName, ct).ConfigureAwait(false);
            progress?.Report(new JobProgress("下载 OptiFine", 0.08));
            await DownloadAsync(url, cache, progress, ct).ConfigureAwait(false);
        }
        if (!IsZip(cache))
            throw new InvalidDataException("下载的 OptiFine 安装器不是有效 jar。");
        return (version, cache);
    }

    public async Task<string> InstallAsync(
        string minecraft,
        string? patch = null,
        string? javaPath = null,
        IProgress<JobProgress>? progress = null,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            throw new InvalidOperationException("安装 OptiFine 需要 Java 路径。");

        progress?.Report(new JobProgress("获取 OptiFine 版本", 0.01));
        var versions = await GetVersionsAsync(minecraft, ct).ConfigureAwait(false);
        var version = patch is null
            ? versions.FirstOrDefault(item => !item.IsPreview) ?? versions.FirstOrDefault()
            : versions.FirstOrDefault(item =>
                  string.Equals(item.Patch, patch, StringComparison.OrdinalIgnoreCase) ||
                  item.FileName.Contains(patch, StringComparison.OrdinalIgnoreCase));
        if (version is null)
            throw new InvalidOperationException($"没有找到 OptiFine {minecraft} {patch}。");

        progress?.Report(new JobProgress("安装原版依赖", 0.03));
        await _installer.InstallAsync(
            minecraft, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct)
            .ConfigureAwait(false);

        var cache = Path.Combine(PclPaths.CacheDirectory, "optifine", version.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        if (!IsZip(cache))
        {
            var url = await GetOfficialDownloadUrlAsync(version.FileName, ct).ConfigureAwait(false);
            progress?.Report(new JobProgress("下载 OptiFine", 0.08));
            await DownloadAsync(url, cache, progress, ct).ConfigureAwait(false);
        }

        if (!IsZip(cache))
            throw new InvalidDataException("下载的 OptiFine 安装器不是有效 jar。");

        var before = Directory.Exists(_repository.VersionsDirectory)
            ? Directory.EnumerateDirectories(_repository.VersionsDirectory).ToHashSet()
            : [];
        var template = Path.Combine(Path.GetTempPath(), "pcl-optifine-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(template, "home");
        var minecraftHome = Path.Combine(home, ".minecraft");
        var vanillaDirectory = Path.Combine(minecraftHome, "versions", minecraft);
        Directory.CreateDirectory(vanillaDirectory);

        var sourceVersion = _repository.GetVersionDirectory(minecraft);
        File.Copy(Path.Combine(sourceVersion, minecraft + ".json"),
            Path.Combine(vanillaDirectory, minecraft + ".json"), overwrite: true);
        var sourceJar = Path.Combine(sourceVersion, minecraft + ".jar");
        if (File.Exists(sourceJar))
            File.Copy(sourceJar, Path.Combine(vanillaDirectory, minecraft + ".jar"), overwrite: true);
        await File.WriteAllTextAsync(
            Path.Combine(minecraftHome, "launcher_profiles.json"),
            """{"profiles":{},"selectedProfile":null,"clientToken":"23323323323323323323323323323333"}""",
            ct).ConfigureAwait(false);

        try
        {
            progress?.Report(new JobProgress("运行 OptiFine 安装器", 0.15));
            await RunInstallerAsync(cache, javaPath, home, log, ct).ConfigureAwait(false);

            progress?.Report(new JobProgress("合并 OptiFine 文件", 0.7));
            CopyMerge(minecraftHome, _repository.GameDirectory);

            var installedId = Directory.EnumerateDirectories(_repository.VersionsDirectory)
                                  .FirstOrDefault(directory => !before.Contains(directory) &&
                                      File.Exists(Path.Combine(directory,
                                          Path.GetFileName(directory) + ".json")))
                              is { } installed
                ? Path.GetFileName(installed)
                : $"{minecraft}-OptiFine_{version.Type}_{version.Patch}";

            progress?.Report(new JobProgress("补全 OptiFine 资源", 0.8));
            await _installer.InstallAsync(
                installedId, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct)
                .ConfigureAwait(false);

            Log.Info($"OptiFine 安装完成：{installedId}");
            return installedId;
        }
        finally
        {
            try
            {
                if (Directory.Exists(template)) Directory.Delete(template, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private async Task<string> GetOfficialDownloadUrlAsync(string fileName, CancellationToken ct)
    {
        var pageUrl = "https://optifine.net/adloadx?f=" + Uri.EscapeDataString(fileName);
        using var page = await _http.GetAsync(pageUrl, ct).ConfigureAwait(false);
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var match = Regex.Match(html, @"downloadx\?f=[^""']+");
        if (!match.Success)
            throw new InvalidOperationException("OptiFine 官方页面没有返回下载地址。");
        return "https://optifine.net/" + match.Value.Replace("&amp;", "&");
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://optifine.net/downloads");
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            readTotal += read;
            progress?.Report(new JobProgress(
                "下载 OptiFine",
                total > 0 ? 0.08 + (double)readTotal / total * 0.05 : null,
                null, null, $"{readTotal / 1048576.0:0.0} MB"));
        }
    }

    private static async Task RunInstallerAsync(
        string installerPath,
        string javaPath,
        string home,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = home,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-Duser.home=" + home);
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("optifine.Installer");

        using var process = new Process { StartInfo = startInfo };
        var tail = new Queue<string>();
        void Report(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > 30) tail.Dequeue();
            }
            log?.Report(line);
        }

        process.OutputDataReceived += (_, e) => Report(e.Data);
        process.ErrorDataReceived += (_, e) => Report(e.Data);
        if (!process.Start())
            throw new InvalidOperationException("无法启动 OptiFine 安装器。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (tail) detail = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException(
                $"OptiFine 安装器退出码 {process.ExitCode}：{detail}");
        }
    }

    private static void CopyMerge(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (string.Equals(relative, "launcher_profiles.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
