using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Core.Logging;
using PCL.Download;

namespace PCL.Launch.Java;

/// <summary>
/// 按需安装 Mojang Java Runtime（原版 PCL 同源方案）：
/// all.json → 组件 manifest → 逐文件下载 → 设置可执行位 → 探测。
/// </summary>
public sealed class JavaProvisioner
{
    private const string AllJsonUrl =
        "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
    private const string AllJsonMirror =
        "https://bmclapi2.bangbang93.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private readonly JavaLocator _locator;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JavaProvisioner(JavaLocator? locator = null, HttpClient? http = null)
    {
        _locator = locator ?? new JavaLocator();
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1");
    }

    /// <summary>优先使用系统已安装的 Java；没有时下载 Mojang Java Runtime。</summary>
    public async Task<JavaRuntime> EnsureAsync(
        int major,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var runtimes = await _locator.DiscoverAsync(ct: ct).ConfigureAwait(false);

        var match = runtimes
            .Where(r => r.MajorVersion == major)
            .OrderByDescending(r => r.Is64Bit)
            .FirstOrDefault()
            ?? runtimes
                .Where(r => r.MajorVersion > major)
                .OrderBy(r => r.MajorVersion)
                .ThenByDescending(r => r.Is64Bit)
                .FirstOrDefault();

        if (match is not null) return match;

        Log.Info($"未找到 Java {major}，准备下载 Mojang Java Runtime。");
        return await ProvisionAsync(major, force: false, progress, ct).ConfigureAwait(false);
    }

    /// <summary>下载并安装指定主版本的 Mojang Java Runtime。</summary>
    public async Task<JavaRuntime> ProvisionAsync(
        int major,
        bool force = false,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var target = Path.Combine(PclPaths.JavaDirectory, $"mojang-{major}");

            if (!force && Directory.Exists(target))
            {
                var existingPath = FindJava(target);
                if (existingPath is not null)
                {
                    var existing = await _locator.ProbePathAsync(existingPath, ct).ConfigureAwait(false);
                    if (existing is not null && existing.MajorVersion >= major)
                    {
                        Log.Info($"复用已下载的 Java {existing.MajorVersion}：{existingPath}");
                        return existing;
                    }
                }
            }

            progress?.Report(new JobProgress($"获取 Java {major} 信息", 0.02));
            var allJson = await GetJsonAsync([AllJsonUrl, AllJsonMirror], ct).ConfigureAwait(false);
            var component = SelectComponent(allJson, major)
                            ?? throw new InvalidOperationException(
                                $"Mojang 未提供 Java {major} 的 {GetPlatform()} 运行时。");

            progress?.Report(new JobProgress($"获取 Java {major} 文件清单", 0.04));
            var manifestUrl = component.ManifestUrl;
            var manifestMirror = manifestUrl.Replace(
                "https://piston-meta.mojang.com",
                "https://bmclapi2.bangbang93.com",
                StringComparison.OrdinalIgnoreCase);
            var manifestJson = await GetJsonAsync(
                manifestUrl == manifestMirror ? [manifestUrl] : [manifestUrl, manifestMirror],
                ct).ConfigureAwait(false);

            var files = ParseFiles(manifestJson, target);
            if (files.Count == 0)
                throw new InvalidDataException("Java 运行时文件清单为空。");

            progress?.Report(new JobProgress($"下载 Java {major}", 0.06, Detail: component.VersionName));
            var downloader = new DownloadManager();
            var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
                $"下载 Java {major}",
                0.06 + (download.Percent ?? 0) * 0.78,
                (long)download.BytesPerSecond,
                null,
                $"{download.CompletedFiles}/{download.TotalFiles}")));

            await downloader.DownloadAsync(
                files.Select(f => f.Item),
                new DownloadOptions { MaxConcurrency = 8, Mirror = MirrorMode.Official },
                adapter,
                ct).ConfigureAwait(false);

            progress?.Report(new JobProgress($"配置 Java {major}", 0.88));
            foreach (var file in files.Where(f => f.Executable))
                MakeExecutable(file.Item.Destination);

            var javaPath = FindJava(target)
                           ?? throw new InvalidOperationException(
                               $"Java {major} 下载完成但未找到 bin/java。");
            MakeExecutable(javaPath);

            var runtime = await _locator.ProbePathAsync(javaPath, ct).ConfigureAwait(false)
                          ?? throw new InvalidOperationException(
                              $"Java {major} 已安装但无法执行：{javaPath}");

            _locator.InvalidateCache();
            progress?.Report(new JobProgress($"Java {major} 就绪", 1.0));
            Log.Info($"Java {runtime.MajorVersion} 安装完成：{runtime.Path}");
            return runtime;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> GetJsonAsync(IEnumerable<string> urls, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                return await _http.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"获取 Java 元数据失败（{url}）：{ex.Message}");
            }
        }

        throw new InvalidOperationException("无法获取 Java 运行时元数据。", last);
    }

    private static JavaComponent? SelectComponent(string allJson, int requiredMajor)
    {
        using var document = JsonDocument.Parse(allJson);
        if (!document.RootElement.TryGetProperty(GetPlatform(), out var platform) ||
            platform.ValueKind != JsonValueKind.Object)
            return null;

        JavaComponent? exact = null;
        JavaComponent? higher = null;

        foreach (var property in platform.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array ||
                property.Value.GetArrayLength() == 0)
                continue;

            var entry = property.Value[0];
            if (!entry.TryGetProperty("manifest", out var manifest) ||
                !manifest.TryGetProperty("url", out var urlElement))
                continue;

            var versionName = entry.TryGetProperty("version", out var version) &&
                              version.TryGetProperty("name", out var name)
                ? name.GetString() ?? ""
                : "";
            var major = ParseMajor(versionName);
            var url = urlElement.GetString();
            if (string.IsNullOrEmpty(url)) continue;

            var candidate = new JavaComponent(property.Name, versionName, major, url);
            if (major == requiredMajor)
            {
                exact ??= candidate;
            }
            else if (major > requiredMajor)
            {
                if (higher is null || candidate.Major < higher.Major) higher = candidate;
            }
        }

        if (exact is not null) return exact;
        if (higher is not null)
        {
            Log.Warn($"没有 Java {requiredMajor}，改用 Java {higher.Major}（{higher.VersionName}）。");
            return higher;
        }

        return null;
    }

    private static List<JavaFile> ParseFiles(string manifestJson, string target)
    {
        using var document = JsonDocument.Parse(manifestJson);
        if (!document.RootElement.TryGetProperty("files", out var files))
            return [];

        var result = new List<JavaFile>();
        var separator = Path.DirectorySeparatorChar;

        foreach (var property in files.EnumerateObject())
        {
            var info = property.Value;
            if (!info.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "file", StringComparison.Ordinal))
                continue;

            if (!info.TryGetProperty("downloads", out var downloads) ||
                !downloads.TryGetProperty("raw", out var raw))
                continue;

            var url = raw.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            var sha1 = raw.TryGetProperty("sha1", out var sha1Element) ? sha1Element.GetString() : null;
            var size = raw.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed)
                ? parsed
                : (long?)null;
            if (string.IsNullOrEmpty(url)) continue;

            var relative = property.Name.Replace('/', separator);
            var destination = Path.Combine(target, relative);
            var executable = info.TryGetProperty("executable", out var exe) && exe.GetBoolean();

            result.Add(new JavaFile(
                DownloadItem.Create(url, destination, sha1, size, MirrorMode.Official),
                executable));
        }

        return result;
    }

    private static string? FindJava(string root)
    {
        var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var direct = Path.Combine(root, "bin", executable);
        if (File.Exists(direct)) return direct;

        if (!Directory.Exists(root)) return null;
        foreach (var file in Directory.EnumerateFiles(root, executable, SearchOption.AllDirectories))
            return file;

        return null;
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch
        {
            // 某些文件系统不支持 chmod，交给后续 Probe 判断
        }
    }

    private static int ParseMajor(string versionName)
    {
        var text = versionName.Trim();
        if (text.StartsWith("1.", StringComparison.Ordinal))
        {
            var rest = text[2..];
            var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var minor) ? minor : 0;
        }

        var majorDigits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(majorDigits, out var major) ? major : 0;
    }

    private static string GetPlatform()
    {
        if (OperatingSystem.IsWindows())
            return RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "windows-x64" : "windows-x86";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "mac-os-arm64" : "mac-os";
        return RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "linux-i386" : "linux";
    }

    private sealed record JavaComponent(
        string Component,
        string VersionName,
        int Major,
        string ManifestUrl);

    private sealed record JavaFile(DownloadItem Item, bool Executable);

    private sealed class ProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
