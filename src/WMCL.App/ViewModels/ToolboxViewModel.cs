using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public partial class ToolboxViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly HttpClient _http = new();
    private CancellationTokenSource? _downloadCts;

    public ToolboxViewModel(AppServices services)
    {
        _services = services;
    }

    public string LogsDirectory => WmclPaths.LogsDirectory;
    public string GameDirectory => _services.Settings.GameDirectory;
    public string ConfigDirectory => WmclPaths.ConfigDirectory;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _customDownloadUrl = "";

    [ObservableProperty]
    private string _customDownloadFileName = "";

    [ObservableProperty]
    private string _customDownloadFolder =
        Path.Combine(WmclPaths.DataDirectory, "downloads");

    [ObservableProperty]
    private string _customDownloadStatus = "输入 URL 后下载到指定目录";

    [ObservableProperty]
    private bool _isCustomDownloading;

    [ObservableProperty]
    private double _customDownloadProgress;

    [ObservableProperty]
    private string _serverAddress = "";

    [ObservableProperty]
    private string _serverPingStatus = "输入服务器地址后 Ping";

    [ObservableProperty]
    private bool _isServerPinging;

    [ObservableProperty]
    private IReadOnlyList<MotdRun>? _serverMotdRuns;

    public bool HasServerMotdRuns => ServerMotdRuns is { Count: > 0 };

    partial void OnServerMotdRunsChanged(IReadOnlyList<MotdRun>? value) =>
        OnPropertyChanged(nameof(HasServerMotdRuns));

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _serverFavicon;

    public bool HasServerFavicon => ServerFavicon is not null;

    partial void OnServerFaviconChanged(Avalonia.Media.Imaging.Bitmap? value) =>
        OnPropertyChanged(nameof(HasServerFavicon));

    public bool CanPingServer => !IsServerPinging && !string.IsNullOrWhiteSpace(ServerAddress);

    partial void OnIsServerPingingChanged(bool value) =>
        OnPropertyChanged(nameof(CanPingServer));

    partial void OnServerAddressChanged(string value) =>
        OnPropertyChanged(nameof(CanPingServer));

    public bool CanStartCustomDownload =>
        !IsCustomDownloading && !string.IsNullOrWhiteSpace(CustomDownloadUrl);

    partial void OnIsCustomDownloadingChanged(bool value) =>
        OnPropertyChanged(nameof(CanStartCustomDownload));

    partial void OnCustomDownloadUrlChanged(string value) =>
        OnPropertyChanged(nameof(CanStartCustomDownload));

    [RelayCommand]
    private void OpenLogs() => Open(WmclPaths.LogsDirectory);

    [RelayCommand]
    private void OpenGame() => Open(_services.Settings.GameDirectory);

    [RelayCommand]
    private void OpenConfig() => Open(WmclPaths.ConfigDirectory);

    [RelayCommand]
    private async Task PingServerAsync()
    {
        if (!CanPingServer) return;
        IsServerPinging = true;
        ServerFavicon = null;
        ServerMotdRuns = null;
        ServerPingStatus = "正在 Ping…";
        try
        {
            var result = await ServerPingService.PingAsync(
                ServerAddress.Trim(), timeout: TimeSpan.FromSeconds(5));
            var players = result.Online is not null && result.Max is not null
                ? $"{result.Online}/{result.Max} 人"
                : "人数未知";
            var version = string.IsNullOrWhiteSpace(result.Version) ? "版本未知" : result.Version;
            ServerMotdRuns = result.MotdRuns;
            var sample = result.PlayerSample is { Count: > 0 }
                ? " · 玩家：" + string.Join(", ", result.PlayerSample.Take(6))
                : "";
            ServerPingStatus = $"{players} · {version} · {result.LatencyMs} ms{sample}";
            ServerFavicon = DecodeFavicon(result.Favicon);
        }
        catch (Exception ex)
        {
            ServerPingStatus = "Ping 失败：" + ex.Message;
        }
        finally
        {
            IsServerPinging = false;
        }
    }

    private static Avalonia.Media.Imaging.Bitmap? DecodeFavicon(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(base64));
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task StartCustomDownloadAsync()
    {
        if (!CanStartCustomDownload) return;

        if (!Uri.TryCreate(CustomDownloadUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            CustomDownloadStatus = "请输入有效的 http/https URL";
            return;
        }

        var fileName = string.IsNullOrWhiteSpace(CustomDownloadFileName)
            ? Path.GetFileName(uri.LocalPath)
            : CustomDownloadFileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "download.bin";

        var folder = string.IsNullOrWhiteSpace(CustomDownloadFolder)
            ? Path.Combine(WmclPaths.DataDirectory, "downloads")
            : WmclPaths.Expand(CustomDownloadFolder);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(fileName));

        IsCustomDownloading = true;
        CustomDownloadProgress = 0;
        CustomDownloadStatus = "开始下载…";
        _downloadCts = new CancellationTokenSource();
        try
        {
            using var response = await _http.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
            await using var output = File.Create(destination);
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, _downloadCts.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), _downloadCts.Token);
                read += count;
                if (total is > 0)
                    CustomDownloadProgress = read * 100d / total.Value;
                CustomDownloadStatus = total is > 0
                    ? $"{read / 1024d / 1024d:0.0} / {total.Value / 1024d / 1024d:0.0} MB"
                    : $"{read / 1024d / 1024d:0.0} MB";
            }
            CustomDownloadProgress = 100;
            CustomDownloadStatus = "下载完成：" + destination;
            Status = "自定义下载完成：" + fileName;
        }
        catch (OperationCanceledException)
        {
            CustomDownloadStatus = "已取消下载";
        }
        catch (Exception ex)
        {
            CustomDownloadStatus = "下载失败：" + ex.Message;
        }
        finally
        {
            IsCustomDownloading = false;
            _downloadCts.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelCustomDownload()
    {
        _downloadCts?.Cancel();
        CustomDownloadStatus = "正在取消…";
    }

    private void Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? path : "xdg-open",
                Arguments = OperatingSystem.IsWindows() ? "" : path,
                UseShellExecute = OperatingSystem.IsWindows()
            };
            Process.Start(start);
            Status = "已打开：" + path;
        }
        catch (Exception ex)
        {
            Status = "打开失败：" + ex.Message;
        }
    }
}
