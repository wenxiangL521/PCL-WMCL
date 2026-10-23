using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;

namespace WMCL.Modding.OptiFine;

public sealed record OptiFabricFile(
    long Id, string FileName, string DisplayName, DateTimeOffset Published,
    long Size, ImmutableArray<string> GameVersions)
{
    public string? Sha1 { get; init; }
    public string Version => FileName["optifabric-".Length..^".jar".Length];
    public string Label => $"{Version} · {Published.LocalDateTime:yyyy/MM/dd HH:mm}";
    public string DownloadUrl => $"https://edge.forgecdn.net/files/{Id / 1000}/{Id % 1000:D3}/{Uri.EscapeDataString(FileName)}";
}

public sealed class OptiFabricService
{
    public const long ProjectId = 322385;
    private const string CatalogUrl = "https://api.cfwidget.com/322385";
    private readonly HttpClient _http;
    private readonly DownloadManager _downloads;

    public OptiFabricService(HttpClient? http = null, HttpClient? downloadHttp = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
        _downloads = new DownloadManager(downloadHttp);
    }

    public static bool IsOriginsVersion(string minecraft) =>
        minecraft.StartsWith("1.14", StringComparison.Ordinal) || minecraft.StartsWith("1.15", StringComparison.Ordinal);

    public static bool IsCompatible(OptiFabricFile file, string minecraft) =>
        !IsOriginsVersion(minecraft) && file.GameVersions.Contains(minecraft, StringComparer.OrdinalIgnoreCase) &&
        file.GameVersions.Contains("Fabric", StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<OptiFabricFile> ParseFiles(string json, string minecraft)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var project) || project.GetInt64() != ProjectId)
            throw new InvalidDataException("OptiFabric 元数据不是 CurseForge 项目 322385");
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("OptiFabric 元数据缺少文件列表");
        var result = new List<OptiFabricFile>();
        foreach (var entry in files.EnumerateArray())
        {
            if (!entry.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array) continue;
            var gameVersions = versions.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!).ToImmutableArray();
            if (!gameVersions.Contains(minecraft, StringComparer.OrdinalIgnoreCase) ||
                !gameVersions.Contains("Fabric", StringComparer.OrdinalIgnoreCase) || IsOriginsVersion(minecraft)) continue;
            var id = entry.GetProperty("id").GetInt64();
            var name = entry.GetProperty("name").GetString() ?? "";
            ValidateFile(id, name, entry.GetProperty("filesize").GetInt64());
            var published = DateTimeOffset.Parse(entry.GetProperty("uploaded_at").GetString()!, CultureInfo.InvariantCulture);
            result.Add(new OptiFabricFile(id, name, entry.GetProperty("display").GetString() ?? name,
                published, entry.GetProperty("filesize").GetInt64(), gameVersions));
        }
        return result.OrderByDescending(file => file.Published).ThenByDescending(file => file.Id).DistinctBy(file => file.Id).ToArray();
    }

    public async Task<IReadOnlyList<OptiFabricFile>> GetFilesAsync(string minecraft, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsOriginsVersion(minecraft)) return [];
        var json = await _http.GetStringAsync(CatalogUrl, ct).ConfigureAwait(false);
        return ParseFiles(json, minecraft);
    }

    public async Task<string> InstallAsync(OptiFabricFile file, string minecraft, string modsDirectory,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        ValidateFile(file.Id, file.FileName, file.Size, file.Sha1);
        if (!IsCompatible(file, minecraft))
            throw new InvalidOperationException($"OptiFabric {file.Version} 不支持 Minecraft {minecraft}");
        ct.ThrowIfCancellationRequested();
        var destination = FileUtil.SafeChildPath(modsDirectory, file.FileName);
        if (File.Exists(destination))
        {
            await ValidateJarAsync(destination, file, ct).ConfigureAwait(false);
            return destination;
        }
        var temporary = Path.Combine(Path.GetTempPath(), "wmcl-optifabric-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var downloaded = Path.Combine(temporary, file.FileName);
            await _downloads.DownloadAsync([new DownloadItem
            {
                Destination = downloaded,
                Size = file.Size,
                Sha1 = file.Sha1,
                Urls = [file.DownloadUrl,
                    file.DownloadUrl.Replace("edge.forgecdn.net", "mediafiles.forgecdn.net", StringComparison.Ordinal)]
            }], new DownloadOptions { MaxConcurrency = 1 },
                new ProgressAdapter(update => progress?.Report(new JobProgress("下载 OptiFabric", update.Percent,
                    (long)update.BytesPerSecond, Detail: file.FileName))), ct).ConfigureAwait(false);
            await ValidateJarAsync(downloaded, file, ct).ConfigureAwait(false);
            Directory.CreateDirectory(modsDirectory);
            ct.ThrowIfCancellationRequested();
            File.Copy(downloaded, destination, overwrite: false);
            return destination;
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void ValidateFile(long id, string name, long size, string? sha1 = null)
    {
        if (id <= 0 || size <= 0 || name != FileUtil.SafeFileName(name) ||
            !name.StartsWith("optifabric-", StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.Length <= "optifabric-.jar".Length ||
            sha1 is not null && (sha1.Length != 40 || !sha1.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("OptiFabric 文件信息无效");
    }

    private static async Task ValidateJarAsync(string path, OptiFabricFile file, CancellationToken ct)
    {
        if (new FileInfo(path).Length != file.Size)
            throw new InvalidDataException($"OptiFabric 文件大小不符：{file.FileName}");
        if (file.Sha1 is not null && !string.Equals(await FileUtil.Sha1Async(path, ct).ConfigureAwait(false), file.Sha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"OptiFabric 文件哈希与原始文件不符：{file.FileName}");
        using var archive = ZipFile.OpenRead(path);
        var metadata = archive.GetEntry("fabric.mod.json")
            ?? throw new InvalidDataException("OptiFabric jar 缺少 fabric.mod.json");
        await using var stream = metadata.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (document.RootElement.GetProperty("id").GetString() != "optifabric" ||
            document.RootElement.GetProperty("version").GetString() != file.Version)
            throw new InvalidDataException("下载的 jar 与所选 OptiFabric 版本不一致");
    }

    private sealed class ProgressAdapter(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
