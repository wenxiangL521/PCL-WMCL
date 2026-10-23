using System.IO.Compression;
using System.Security.Cryptography;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Modding.Loaders;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Services;

internal sealed record ComponentRowActionRequest(string Title, string? Url = null,
    InstallerArtifact? Artifact = null, OptiFineVersion? OptiFine = null)
{
    public string SuggestedFileName => OptiFine?.FileName ?? Artifact?.FileName ?? "";
}

internal sealed class ComponentRowActionService
{
    private readonly OptiFineService _optifine;
    private readonly HttpClient _http;
    private readonly Func<string, CancellationToken, Task<string?>> _pick;
    private readonly Action<string> _open;

    public ComponentRowActionService(OptiFineService optifine, HttpClient? http = null,
        Func<string, CancellationToken, Task<string?>>? pick = null, Action<string>? open = null)
    {
        _optifine = optifine;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _pick = pick ?? DialogService.SaveInstallerAsync;
        _open = open ?? ExternalLauncher.Open;
    }

    public async Task<bool> ExecuteAsync(ComponentRowActionRequest request, Func<bool> current, CancellationToken ct,
        IProgress<JobProgress>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!current()) return false;
        if (request.Url is { } url)
        {
            _open(url);
            return true;
        }
        if (request.SuggestedFileName.Length == 0) throw new InvalidDataException("缺少安装器文件信息。");
        var chosen = await _pick(request.SuggestedFileName, ct);
        ct.ThrowIfCancellationRequested();
        if (chosen is null || !current()) return false;
        progress?.Report(new JobProgress("获取安装器下载地址", 0));
        var destination = Path.GetFullPath(chosen);
        var previousHash = File.Exists(destination) ? await FileUtil.Sha1Async(destination, ct) : null;
        var temporary = destination + ".wmcl-" + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            if (request.OptiFine is { } selected)
            {
                var mirror = OptiFineService.GetMirrorDownloadUrl(selected);
                var urls = new List<string> { mirror };
                try
                {
                    urls.Add(await _optifine.GetInstallerDownloadUrlAsync(selected, ct));
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                }
                await DownloadAsync(new(selected.FileName, urls, 64 * 1024), temporary, ct, progress);
            }
            else if (request.Artifact is { } artifact)
                await DownloadAsync(artifact, temporary, ct, progress);
            else throw new InvalidDataException("缺少安装器下载地址。");
            ValidateJar(temporary, request.OptiFine is null ? request.Artifact?.MinimumSize ?? 1 : 64 * 1024, request.Artifact?.Hash);
            ct.ThrowIfCancellationRequested();
            if (!current()) return false;
            var actualHash = File.Exists(destination) ? await FileUtil.Sha1Async(destination, ct) : null;
            if (previousHash != actualHash) throw new IOException("保存期间目标文件已被其他操作更改，未覆盖文件。");
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            progress?.Report(new JobProgress("保存完成", 1));
            return true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task DownloadAsync(InstallerArtifact artifact, string temporary, CancellationToken ct, IProgress<JobProgress>? progress)
    {
        Exception? failure = null;
        foreach (var url in artifact.Urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    var total = response.Content.Headers.ContentLength;
                    int count;
                    while ((count = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), ct);
                        received += count;
                        progress?.Report(new JobProgress("下载 " + artifact.FileName,
                            total is > 0 ? Math.Min(1, (double)received / total.Value) : null,
                            Detail: $"{received / 1024.0:0.0} KB"));
                    }
                }
                ValidateJar(temporary, artifact.MinimumSize, artifact.Hash);
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                failure = exception;
            }
        }
        throw failure ?? new InvalidDataException("没有可用的安装器下载地址。");
    }

    private static void ValidateJar(string path, long minimumSize, string? hash)
    {
        if (new FileInfo(path).Length < minimumSize) throw new InvalidDataException("下载的安装器文件不完整。");
        using var jar = ZipFile.OpenRead(path);
        if (jar.Entries.Count == 0) throw new InvalidDataException("下载的安装器没有有效内容。");
        if (!string.IsNullOrWhiteSpace(hash))
        {
            using var input = File.OpenRead(path);
            var actual = hash.Length == 32 ? Convert.ToHexStringLower(MD5.HashData(input)) :
                hash.Length == 40 ? Convert.ToHexStringLower(SHA1.HashData(input)) :
                throw new InvalidDataException("安装器校验信息无效。");
            if (!string.Equals(hash, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装器文件校验失败。");
        }
    }
}
