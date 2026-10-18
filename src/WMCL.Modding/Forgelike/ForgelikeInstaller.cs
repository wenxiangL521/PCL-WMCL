using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Download;
using WMCL.Minecraft;

namespace WMCL.Modding.Forgelike;

public enum ForgelikeKind
{
    Forge,
    NeoForge
}

/// <summary>
/// 现代 Forge/NeoForge 安装：下载官方 installer jar，用 Java headless 执行
/// `--installClient`，再复用 VersionInstaller 补全 assets/libraries。
/// 对照原版 PCL2 的 McDownloadForgelikeLoader；PCL2 使用自定义 JavaWrapper，本项目直接用官方安装器。
/// </summary>
public sealed class ForgelikeInstaller
{
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(30)
    })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public ForgelikeInstaller(VersionRepository repository, VersionInstaller? installer = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public static IReadOnlyList<string> ParseForgeVersions(string json, string minecraftVersion)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

        var prefix = minecraftVersion + "-";
        var result = new List<string>();

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var text = GetString(entry, "version");
            if (string.IsNullOrWhiteSpace(text)) continue;

            var loader = text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? text[prefix.Length..]
                : text;
            if (!Version.TryParse(loader, out _)) continue;

            var hasInstaller = false;
            if (entry.TryGetProperty("files", out var files) &&
                files.ValueKind == JsonValueKind.Array)
            {
                hasInstaller = files.EnumerateArray().Any(file =>
                    string.Equals(GetString(file, "category"), "installer",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(GetString(file, "format"), "jar",
                        StringComparison.OrdinalIgnoreCase));
            }

            if (hasInstaller) result.Add(loader);
        }

        return result.Distinct().OrderByDescending(ParseVersion).ToList();
    }

    public static IReadOnlyList<string> ParseNeoForgeVersions(string json, string minecraftVersion)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Array)
            return [];

        var prefix = GetNeoForgePrefix(minecraftVersion);
        var result = new List<string>();

        foreach (var entry in files.EnumerateArray())
        {
            var name = GetString(entry, "name");
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!Version.TryParse(name.Split('-')[0], out _)) continue;
            result.Add(name);
        }

        return result.Distinct()
            .OrderByDescending(ParseVersion)
            .ThenByDescending(version => version.Contains("-beta", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";

    public async Task<IReadOnlyList<string>> GetVersionsAsync(
        ForgelikeKind kind,
        string minecraftVersion,
        CancellationToken ct = default)
    {
        var json = await GetListJsonAsync(kind, minecraftVersion, ct).ConfigureAwait(false);
        return kind == ForgelikeKind.Forge
            ? ParseForgeVersions(json, minecraftVersion)
            : ParseNeoForgeVersions(json, minecraftVersion);
    }

    public async Task<string> InstallAsync(
        ForgelikeKind kind,
        string minecraftVersion,
        string loaderVersion,
        string javaPath,
        IProgress<JobProgress>? progress = null,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress($"准备 {Name(kind)} 安装器", 0.02));

        var versionId = BuildVersionId(kind, minecraftVersion, loaderVersion);
        var installerPath = await DownloadInstallerAsync(
            kind, minecraftVersion, loaderVersion, progress, ct).ConfigureAwait(false);

        EnsureLauncherProfiles();
        var before = SnapshotVersionDirectories();

        if (kind == ForgelikeKind.NeoForge)
        {
            progress?.Report(new JobProgress("预置 NeoForge 依赖", 0.12));
            await PrepareNeoForgeLibrariesAsync(installerPath, progress, ct).ConfigureAwait(false);
        }

        progress?.Report(new JobProgress($"运行 {Name(kind)} 安装器", 0.4));
        await RunInstallerAsync(installerPath, javaPath, kind, log, ct).ConfigureAwait(false);

        var installedId = FindInstalledVersion(before, kind, versionId)
                          ?? throw new InvalidOperationException(
                              $"{Name(kind)} 安装器结束，但没有找到新的版本目录。");

        progress?.Report(new JobProgress($"补全 {Name(kind)} 资源", 0.8));
        await _installer.InstallAsync(
            installedId,
            new InstallOptions { Mirror = MirrorMode.Auto },
            progress,
            ct).ConfigureAwait(false);

        Log.Info($"{Name(kind)} 安装完成：{installedId}");
        return installedId;
    }

    private async Task<string> DownloadInstallerAsync(
        ForgelikeKind kind,
        string minecraftVersion,
        string loaderVersion,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        var fileName = kind == ForgelikeKind.Forge
            ? $"forge-{minecraftVersion}-{loaderVersion}-installer.jar"
            : $"neoforge-{loaderVersion}-installer.jar";
        var urls = kind == ForgelikeKind.Forge
            ? new[]
            {
                $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{minecraftVersion}-{loaderVersion}/{fileName}",
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{minecraftVersion}-{loaderVersion}/{fileName}"
            }
            : new[]
            {
                $"https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/{loaderVersion}/{fileName}",
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/{fileName}"
            };

        var destination = Path.Combine(
            WmclPaths.CacheDirectory, "installers", fileName);
        var item = new DownloadItem
        {
            Destination = destination,
            Urls = urls
        };

        var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
            $"下载 {Name(kind)} 安装器",
            0.02 + (download.Percent ?? 0) * 0.1,
            (long)download.BytesPerSecond,
            null,
            fileName)));
        var downloader = new DownloadManager();
        await downloader.DownloadAsync(
            [item], new DownloadOptions { MaxConcurrency = 1 }, adapter, ct)
            .ConfigureAwait(false);
        return destination;
    }

    private async Task RunInstallerAsync(
        string installerPath,
        string javaPath,
        ForgelikeKind kind,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = _repository.GameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);

        if (kind == ForgelikeKind.NeoForge)
        {
            // NeoForge 官方 Maven 在部分网络不可达：先预置库，再离线安装。
            startInfo.ArgumentList.Add("--offline");
            startInfo.ArgumentList.Add("--installClient");
            startInfo.ArgumentList.Add(_repository.GameDirectory);
        }
        else
        {
            startInfo.ArgumentList.Add("--installClient");
        }

        using var process = new Process { StartInfo = startInfo };
        var tail = new Queue<string>();
        void Report(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > 20) tail.Dequeue();
            }
            log?.Report(line);
        }

        process.OutputDataReceived += (_, e) => Report(e.Data);
        process.ErrorDataReceived += (_, e) => Report(e.Data);

        if (!process.Start())
            throw new InvalidOperationException("无法启动 Forge/NeoForge 安装器。");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));

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

            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("Forge/NeoForge 安装器超时。");
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (tail) detail = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException(
                $"Forge/NeoForge 安装器退出码：{process.ExitCode}{Environment.NewLine}{detail}");
        }
    }

    private async Task PrepareNeoForgeLibrariesAsync(
        string installerPath,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        var paths = new List<string>();
        using (var archive = ZipFile.OpenRead(installerPath))
        {
            ReadLibraryPaths(archive, "install_profile.json", paths);
            ReadLibraryPaths(archive, "version.json", paths);
        }

        paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return;

        var libraryRoot = Path.Combine(_repository.GameDirectory, "libraries");
        var total = paths.Count;
        var completed = 0;
        using var gate = new SemaphoreSlim(6);

        var tasks = paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(async path =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureLibraryAsync(libraryRoot, path, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new JobProgress(
                    $"预置 NeoForge 依赖 {done}/{total}",
                    0.12 + 0.25 * done / total));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static void ReadLibraryPaths(
        ZipArchive archive,
        string entryName,
        List<string> paths)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null) return;

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
            return;

        foreach (var library in libraries.EnumerateArray())
        {
            var path = "";
            if (library.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("artifact", out var artifact))
                path = GetString(artifact, "path");

            if (string.IsNullOrWhiteSpace(path))
                path = MavenPath(GetString(library, "name"));

            if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
        }
    }

    private static string MavenPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var extension = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        if (parts.Length < 3) return "";

        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/" +
               $"{parts[1]}-{parts[2]}{classifier}.{extension}";
    }

    private async Task EnsureLibraryAsync(
        string libraryRoot,
        string relativePath,
        CancellationToken ct)
    {
        var destination = Path.Combine(libraryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (IsArchive(destination)) return;

        var urls = new List<string>
        {
            "https://bmclapi2.bangbang93.com/maven/" + relativePath
        };
        if (relativePath.StartsWith("net/neoforged/", StringComparison.OrdinalIgnoreCase))
            urls.Add("https://maven.neoforged.net/releases/" + relativePath);
        urls.Add("https://repo1.maven.org/maven2/" + relativePath);
        urls.Add("https://maven.aliyun.com/repository/public/" + relativePath);

        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var bytes = await _http.GetByteArrayAsync(url, timeout.Token).ConfigureAwait(false);
                if (bytes.Length < 512 || bytes[0] != 0x50 || bytes[1] != 0x4B)
                {
                    last = new InvalidOperationException("返回内容不是有效的 JAR/ZIP");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".part";
                await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
                File.Move(temporary, destination, overwrite: true);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"下载 NeoForge 库失败（{url}）：{ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"无法下载 NeoForge 库：{relativePath}", last);
    }

    private static bool IsArchive(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            return stream.Length > 512 && stream.ReadByte() == 0x50 && stream.ReadByte() == 0x4B;
        }
        catch
        {
            return false;
        }
    }

    private HashSet<string> SnapshotVersionDirectories()
    {
        if (!Directory.Exists(_repository.VersionsDirectory)) return [];
        return Directory.EnumerateDirectories(_repository.VersionsDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private string? FindInstalledVersion(
        HashSet<string> before,
        ForgelikeKind kind,
        string predictedId)
    {
        if (!Directory.Exists(_repository.VersionsDirectory)) return null;

        var newDirectories = Directory.EnumerateDirectories(_repository.VersionsDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Where(name => !before.Contains(name))
            .ToList();

        var match = newDirectories.FirstOrDefault(name =>
            name.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(_repository.VersionsDirectory, name, name + ".json")));
        if (match is not null) return match;

        if (File.Exists(Path.Combine(_repository.VersionsDirectory, predictedId, predictedId + ".json")))
            return predictedId;

        return newDirectories.FirstOrDefault(name =>
            File.Exists(Path.Combine(_repository.VersionsDirectory, name, name + ".json")));
    }

    private void EnsureLauncherProfiles()
    {
        var path = Path.Combine(_repository.GameDirectory, "launcher_profiles.json");
        if (File.Exists(path)) return;

        var json = JsonSerializer.Serialize(new
        {
            profiles = new Dictionary<string, object>
            {
                ["WMCL"] = new
                {
                    icon = "Grass",
                    name = "WMCL",
                    lastVersionId = "latest-release",
                    type = "latest-release"
                }
            },
            selectedProfile = "WMCL",
            clientToken = "23323323323323323323323323323333"
        }, new JsonSerializerOptions { WriteIndented = true });

        Directory.CreateDirectory(_repository.GameDirectory);
        File.WriteAllText(path, json);
    }

    private async Task<string> GetListJsonAsync(
        ForgelikeKind kind,
        string minecraftVersion,
        CancellationToken ct)
    {
        var urls = kind == ForgelikeKind.Forge
            ? new[]
            {
                "https://bmclapi2.bangbang93.com/forge/minecraft/" +
                minecraftVersion.Replace('-', '_')
            }
            : new[]
            {
                "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge",
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge"
            };

        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                return await _http.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"获取 {Name(kind)} 版本列表失败（{url}）：{ex.Message}");
            }
        }

        throw new InvalidOperationException($"无法获取 {Name(kind)} 版本列表。", last);
    }

    private static string BuildVersionId(
        ForgelikeKind kind,
        string minecraftVersion,
        string loaderVersion) =>
        kind == ForgelikeKind.Forge
            ? $"{minecraftVersion}-forge-{loaderVersion}"
            : $"neoforge-{loaderVersion}";

    private static string GetNeoForgePrefix(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length >= 2 && parts[0] == "1" &&
            int.TryParse(parts[1], out var minor))
            return minor + "." + (parts.Length >= 3 ? parts[2] : "0") + ".";
        return minecraftVersion + ".";
    }

    private static Version ParseVersion(string value)
    {
        var cleaned = value.Split('-')[0];
        return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0);
    }

    private static string Name(ForgelikeKind kind) =>
        kind == ForgelikeKind.Forge ? "Forge" : "NeoForge";

    private sealed class ProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
