using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Core.Jobs;
using WMCL.Download;

namespace WMCL.Launch.Java;

public sealed partial class JavaProvisioner
{
    /// <summary>
    /// Settings-page installation: exact major and native architecture only. Unlike automatic
    /// launch provisioning, explicitly choosing 11 must never silently install/reuse 17 or 21.
    /// Temurin metadata is queried for the requested OS/architecture; unavailable builds fail
    /// explicitly rather than falling back to a different major or an incompatible CPU.
    /// </summary>
    public async Task<JavaRuntime> EnsureExactAsync(int major, IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (major is not (8 or 11 or 17 or 21 or 25)) throw new ArgumentOutOfRangeException(nameof(major));
        using var activity = AcquireOperation();
        var platform = TemurinPlatform(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture);
        var runtimes = await _locator.DiscoverAsync(ct: ct).ConfigureAwait(false);
        var existing = runtimes.FirstOrDefault(runtime => runtime.MajorVersion == major &&
            NativeArchitecture(runtime.Architecture) == platform.Architecture);
        if (existing is not null) return existing;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        string? staging = null;
        try
        {
            var target = Path.Combine(_runtimeDirectory, $"mojang-{major}-temurin-{platform.Architecture}");
            if (Directory.Exists(target))
            {
                var path = FindJava(target);
                var runtime = path is null ? null : await _locator.ProbePathAsync(path, ct).ConfigureAwait(false);
                if (runtime is not null && runtime.MajorVersion == major && NativeArchitecture(runtime.Architecture) == platform.Architecture)
                    return runtime;
                throw new InvalidOperationException("已存在不完整或不兼容的托管 Java，请先清理本启动器下载的 Java 再重试。");
            }
            progress?.Report(new JobProgress($"获取 Java {major} 安装信息", 0.02));
            TemurinAsset? asset;
            try
            {
                asset = await QueryTemurinAsync(major, platform, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                asset = await QueryTemurinReleaseAsync(major, platform, ct).ConfigureAwait(false);
            }
            if (asset is null)
                throw new PlatformNotSupportedException($"Temurin 未提供 Java {major} 的 {platform.Os}/{platform.Architecture} 运行时；请手动安装该主版本兼容 Java。");
            Directory.CreateDirectory(_runtimeDirectory);
            staging = Path.Combine(_runtimeDirectory, ".java-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var archive = Path.Combine(staging, "runtime" + asset.Extension);
            var downloader = new DownloadManager(_http);
            var adapter = new ProgressAdapter(update => progress?.Report(new JobProgress($"下载 Java {major}",
                0.05 + (update.Percent ?? 0) * 0.75, (long)update.BytesPerSecond)));
            await downloader.DownloadAsync([DownloadItem.Create(asset.Url, archive, size: asset.Size)],
                new DownloadOptions { MaxConcurrency = 1, Mirror = MirrorMode.Official }, adapter, ct).ConfigureAwait(false);
            await using (var input = File.OpenRead(archive))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
                if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Java 安装包 SHA256 校验失败，未安装。");
            }
            var extracted = Path.Combine(staging, "extracted");
            progress?.Report(new JobProgress($"解压 Java {major}", 0.84));
            await ExtractTemurinAsync(archive, extracted, asset.Extension == ".zip", ct).ConfigureAwait(false);
            var java = FindJava(extracted) ?? throw new InvalidDataException("Java 安装包中未找到可执行文件。");
            MakeExecutable(java);
            var verified = await _locator.ProbePathAsync(java, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Java 安装包无法执行，未安装。");
            if (verified.MajorVersion != major || NativeArchitecture(verified.Architecture) != platform.Architecture)
                throw new InvalidDataException("Java 安装包的实际主版本或架构与选择不一致，未安装。");
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(extracted, java);
            Directory.Move(extracted, target); // Target is managed and never overwritten.
            _locator.InvalidateCache();
            var finalPath = Path.Combine(target, relative);
            progress?.Report(new JobProgress($"Java {major} 就绪", 1));
            return verified with { Path = finalPath, Home = Path.GetDirectoryName(Path.GetDirectoryName(finalPath))! };
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging))
                try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _gate.Release();
        }
    }

    private async Task<TemurinAsset?> QueryTemurinAsync(int major, (string Os, string Architecture) platform, CancellationToken ct)
    {
        foreach (var type in new[] { "jre", "jdk" })
        {
            var url = $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture={platform.Architecture}&image_type={type}&os={platform.Os}&vendor=eclipse";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync(url, timeout.Token).ConfigureAwait(false);
            var json = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "[]" : await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
            var asset = SelectTemurinAsset(json, major, platform.Os, platform.Architecture);
            if (asset is not null) return asset;
        }
        return null;
    }

    private async Task<TemurinAsset?> QueryTemurinReleaseAsync(int major, (string Os, string Architecture) platform, CancellationToken ct)
    {
        var json = await GetJsonAsync([$"https://api.github.com/repos/adoptium/temurin{major}-binaries/releases/latest"], ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("assets", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Java 发行元数据缺少文件列表。");
        var architecture = platform.Architecture == "x86" ? "x86-32" : platform.Architecture;
        foreach (var type in new[] { "jre", "jdk" })
        {
            var prefix = $"OpenJDK{major}U-{type}_{architecture}_{platform.Os}_hotspot_";
            foreach (var entry in entries.EnumerateArray())
            {
                var name = entry.GetProperty("name").GetString() ?? "";
                var extension = platform.Os == "windows" ? ".zip" : ".tar.gz";
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(extension, StringComparison.Ordinal)) continue;
                var url = entry.GetProperty("browser_download_url").GetString() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "https" ||
                    address.IdnHost != "github.com" || address.UserInfo.Length > 0 || !address.IsDefaultPort)
                    throw new InvalidDataException("Java 发行归档地址无效。");
                var size = entry.GetProperty("size").GetInt64();
                if (size is <= 0 or > 1073741824) throw new InvalidDataException("Java 发行归档大小无效。");
                var checksum = entry.TryGetProperty("digest", out var digest) && digest.GetString() is { } value && value.StartsWith("sha256:", StringComparison.Ordinal)
                    ? value[7..] : null;
                if (checksum is null)
                {
                    var checksumEntry = entries.EnumerateArray().FirstOrDefault(item => item.GetProperty("name").GetString() == name + ".sha256.txt");
                    if (checksumEntry.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Java 发行归档缺少SHA256。");
                    var checksumUrl = checksumEntry.GetProperty("browser_download_url").GetString() ?? "";
                    if (!Uri.TryCreate(checksumUrl, UriKind.Absolute, out var checksumAddress) || checksumAddress.Scheme != "https" ||
                        checksumAddress.IdnHost != "github.com" || checksumAddress.UserInfo.Length > 0 || !checksumAddress.IsDefaultPort ||
                        checksumEntry.GetProperty("size").GetInt64() > 4096)
                        throw new InvalidDataException("Java 校验文件地址或大小无效。");
                    var checksumText = await GetJsonAsync([checksumUrl], ct).ConfigureAwait(false);
                    checksum = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                }
                if (checksum is null || checksum.Length != 64 || checksum.Any(character => !Uri.IsHexDigit(character)))
                    throw new InvalidDataException("Java 发行归档SHA256无效。");
                return new(url, checksum, size, extension);
            }
        }
        return null;
    }

    internal static (string Os, string Architecture) TemurinPlatform(bool windows, bool mac, bool linux, Architecture architecture)
    {
        var os = windows ? "windows" : mac ? "mac" : linux ? "linux" : throw new PlatformNotSupportedException("当前系统不支持自动安装 Java。");
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException($"当前架构 {architecture} 不支持自动安装 Java。")
        };
        return (os, arch);
    }
    private static string NativeArchitecture(string arch) => arch.ToLowerInvariant() switch
    {
        "x86_64" or "amd64" or "x64" => "x64",
        "x86" or "i386" or "i686" => "x86",
        "arm64" or "aarch64" => "aarch64",
        _ => arch.ToLowerInvariant()
    };

    internal sealed record TemurinAsset(string Url, string Sha256, long Size, string Extension);
    internal static TemurinAsset? SelectTemurinAsset(string json, int major, string os, string architecture)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Java 元数据不是发行列表。");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("version", out var version) || !version.TryGetProperty("major", out var majorValue) ||
                !majorValue.TryGetInt32(out var actualMajor) || actualMajor != major ||
                !item.TryGetProperty("binary", out var binary) || binary.GetProperty("os").GetString() != os ||
                binary.GetProperty("architecture").GetString() != architecture) continue;
            var package = binary.GetProperty("package");
            var url = package.GetProperty("link").GetString() ?? "";
            var checksum = package.GetProperty("checksum").GetString() ?? "";
            var size = package.GetProperty("size").GetInt64();
            var name = package.GetProperty("name").GetString() ?? "";
            var extension = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "https" ||
                address.UserInfo.Length > 0 || !address.IsDefaultPort ||
                address.IdnHost is not ("github.com" or "api.adoptium.net") ||
                checksum.Length != 64 || checksum.Any(character => !Uri.IsHexDigit(character)) ||
                size is <= 0 or > 1073741824 || extension is null)
                throw new InvalidDataException("Java 安装包地址、校验信息或格式无效。");
            return new(url, checksum, size, extension);
        }
        return null;
    }

    internal static async Task ExtractTemurinAsync(string archive, string target, bool zipFormat, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        var root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string SafePath(string name)
        {
            name = name.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains(".."))
                throw new InvalidDataException("Java 安装包包含非法路径。");
            var resolved = Path.GetFullPath(Path.Combine(target, name));
            if (!resolved.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Java 安装包路径越界。");
            return resolved;
        }
        long total = 0;
        async Task Copy(Stream input, string destination, long length)
        {
            if (length < 0 || (total += length) > 2147483648L) throw new InvalidDataException("Java 解压内容过大。");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        }
        if (zipFormat)
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var destination = SafePath(entry.FullName);
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Java ZIP不允许符号链接。");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                await using var input = entry.Open();
                await Copy(input, destination, entry.Length).ConfigureAwait(false);
            }
        }
        else
        {
            await using var file = File.OpenRead(archive);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            var links = new List<(string Destination, string Source)>();
            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false)) is not null)
            {
                var destination = SafePath(entry.Name);
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(destination); continue; }
                if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                {
                    var source = Path.GetFullPath(Path.Combine(entry.EntryType == TarEntryType.SymbolicLink ? Path.GetDirectoryName(destination)! : target, entry.LinkName));
                    if (!source.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException("Java 链接指向安装目录外。");
                    links.Add((destination, source));
                    continue;
                }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("Java 包包含不支持的特殊文件。");
                if (entry.DataStream is null) continue;
                await Copy(entry.DataStream, destination, entry.Length).ConfigureAwait(false);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, entry.Mode);
            }
            // Materialize safe internal links as files; no archive-created symlink can redirect extraction.
            foreach (var link in links)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(link.Source)) throw new InvalidDataException("Java 包内部链接目标不存在。");
                await using var input = File.OpenRead(link.Source);
                await Copy(input, link.Destination, input.Length).ConfigureAwait(false);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(link.Destination, File.GetUnixFileMode(link.Source));
            }
        }
    }
}
