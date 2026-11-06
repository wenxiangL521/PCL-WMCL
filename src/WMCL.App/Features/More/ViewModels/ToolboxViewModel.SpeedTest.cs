using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.Features.More;

public partial class ToolboxViewModel
{
    [RelayCommand]
    private async Task RunSpeedTestAsync()
    {
        if (!CanStartSpeedTest) return;
        IsSpeedTesting = true;
        SpeedTestProgress = 0;
        SpeedTestStatus = "正在测速…";
        try
        {
            if (!Uri.TryCreate(SpeedTestUrl.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                SpeedTestStatus = "请输入有效的 URL";
                return;
            }

            // 从多个镜像点下载，取平均值
            var results = new List<double>();
            var urls = new[]
            {
                SpeedTestUrl.Trim(),
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json"
            };

            for (var i = 0; i < urls.Length; i++)
            {
                if (!Uri.TryCreate(urls[i], UriKind.Absolute, out var testUri) ||
                    testUri.Scheme is not ("http" or "https"))
                    continue;

                SpeedTestProgress = (i + 1) * 100.0 / urls.Length;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var response = await Http.GetAsync(
                        testUri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? 0;
                    if (total < 1024) continue;

                    await using var input = await response.Content.ReadAsStreamAsync(cts.Token);
                    var buffer = new byte[65536];
                    long read = 0;
                    var start = DateTime.UtcNow;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cts.Token)) > 0)
                    {
                        read += count;
                        if (DateTime.UtcNow - start > TimeSpan.FromSeconds(3)) break;
                    }
                    var elapsed = (DateTime.UtcNow - start).TotalSeconds;
                    if (elapsed > 0.1)
                        results.Add(read / elapsed / 1024 / 1024); // MB/s
                }
                catch { /* 跳过失败的测速点 */ }
            }

            if (results.Count > 0)
            {
                var avg = results.Average();
                SpeedTestStatus = $"平均速度：{avg:F2} MB/s（{results.Count} 个测速点）";
            }
            else
            {
                SpeedTestStatus = "测速失败：所有测速点均不可用";
            }
        }
        catch (Exception ex)
        {
            SpeedTestStatus = "测速失败：" + ex.Message;
        }
        finally
        {
            IsSpeedTesting = false;
            SpeedTestProgress = 100;
        }
    }

    // ── 镜像对比测速 ──

    public ObservableCollection<MirrorResult> MirrorResults { get; } = [];

    [ObservableProperty]
    private bool _isMirrorTesting;

    [ObservableProperty]
    private string _mirrorTestStatus = "点击开始镜像对比";

    public bool CanStartMirrorTest => !IsMirrorTesting;

    partial void OnIsMirrorTestingChanged(bool value) =>
        OnPropertyChanged(nameof(CanStartMirrorTest));

    [RelayCommand]
    private async Task RunMirrorTestAsync()
    {
        if (!CanStartMirrorTest) return;
        IsMirrorTesting = true;
        MirrorResults.Clear();
        MirrorTestStatus = "正在解析测试文件…";
        try
        {
            var mirrors = await ResolveMirrorTestTargetsAsync();

            foreach (var (name, url) in mirrors)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    using var response = await Http.GetAsync(
                        url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? 0;
                    if (total < 1024)
                    {
                        MirrorResults.Add(new MirrorResult(name, url, 0, "文件太小"));
                        continue;
                    }

                    await using var input = await response.Content.ReadAsStreamAsync(cts.Token);
                    var buffer = new byte[65536];
                    long read = 0;
                    var start = DateTime.UtcNow;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cts.Token)) > 0)
                    {
                        read += count;
                        if (DateTime.UtcNow - start > TimeSpan.FromSeconds(3)) break;
                    }
                    var elapsed = (DateTime.UtcNow - start).TotalSeconds;
                    var speed = elapsed > 0 ? read / elapsed / 1024 / 1024 : 0;
                    MirrorResults.Add(new MirrorResult(name, url, speed, ""));
                }
                catch (Exception ex)
                {
                    MirrorResults.Add(new MirrorResult(name, url, 0, ex.Message));
                }
                OnPropertyChanged(nameof(MirrorResults));
            }

            var best = MirrorResults.Where(r => r.Speed > 0).OrderByDescending(r => r.Speed).FirstOrDefault();
            MirrorTestStatus = best is not null
                ? $"最快镜像：{best.Name}（{best.Speed:F2} MB/s）"
                : "所有镜像均不可用";
        }
        catch (Exception ex)
        {
            MirrorTestStatus = "镜像对比失败：" + ex.Message;
        }
        finally
        {
            IsMirrorTesting = false;
        }
    }

    /// <summary>
    /// 解析镜像测速目标。官方 piston-data 的 client.jar 直链包含完整
    /// SHA1 哈希，无法硬编码（此前硬编码的截断哈希是 404）；
    /// 这里从版本清单动态解析。解析失败时回退到两边的版本清单文件
    /// （官方与 BMCLAPI 均镜像该路径），保证对比功能始终可用。
    /// </summary>
    private async Task<List<(string Name, string Url)>> ResolveMirrorTestTargetsAsync()
    {
        const string manifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        const string bmclapiManifestUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var manifest = await Http.GetStringAsync(manifestUrl, cts.Token);
            var versionUrl = ExtractLatestReleaseUrl(manifest);
            if (versionUrl is null) throw new InvalidOperationException("版本清单中没有正式版条目。");
            var versionJson = await Http.GetStringAsync(versionUrl, cts.Token);
            var clientUrl = ExtractClientJarUrl(versionJson);
            if (clientUrl is null) throw new InvalidOperationException("版本 JSON 中没有 client.jar 下载地址。");
            return
            [
                ("官方", clientUrl),
                ("BMCLAPI", "https://bmclapi2.bangbang93.com/version/1.20.1/client.jar")
            ];
        }
        catch
        {
            // 解析失败（离线/接口变更）：退回版本清单文件，两边同路径可公平对比。
            return [("官方", manifestUrl), ("BMCLAPI", bmclapiManifestUrl)];
        }

        static string? ExtractLatestReleaseUrl(string manifestJson)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("versions", out var versions)) return null;
            foreach (var version in versions.EnumerateArray())
            {
                if (version.TryGetProperty("type", out var type) && type.GetString() == "release" &&
                    version.TryGetProperty("url", out var url))
                    return url.GetString();
            }
            return null;
        }

        static string? ExtractClientJarUrl(string versionJson)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(versionJson);
            if (!doc.RootElement.TryGetProperty("downloads", out var downloads)) return null;
            if (!downloads.TryGetProperty("client", out var client)) return null;
            if (!client.TryGetProperty("url", out var url)) return null;
            return url.GetString();
        }
    }

    public sealed record MirrorResult(string Name, string Url, double Speed, string Error);
}
