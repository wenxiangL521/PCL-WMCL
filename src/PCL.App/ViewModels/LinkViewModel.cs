using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;

namespace PCL.App.ViewModels;

/// <summary>
/// 手动 EasyTier 联机。PCL2 官方房间配置在开源版不可获取，因此这里用
/// “房间码 = 网络名 + 派生密钥 + 公共中继”的替代方案。
/// </summary>
public partial class LinkViewModel : ViewModelBase
{
    private readonly EasyTierService _service = new();
    private readonly TerracottaService _terracotta = new();
    private CancellationTokenSource? _terracottaCts;
    private CancellationTokenSource? _pollCts;

    public LinkViewModel()
    {
        _service.LogReceived += line =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendLog(line));
        };
    }

    [ObservableProperty]
    private string _roomCode = "";

    public ObservableCollection<string> Backends { get; } = ["EasyTier", "陶瓦（HMCL）"];

    [ObservableProperty]
    private string _selectedBackend = "EasyTier";

    public bool IsEasyTier => SelectedBackend == "EasyTier";
    public bool IsTerracotta => !IsEasyTier;

    [ObservableProperty]
    private string _terracottaMode = "创建房间";

    [ObservableProperty]
    private bool _autoFallbackToTerracotta = true;

    public ObservableCollection<string> TerracottaModes { get; } = ["创建房间", "加入房间"];

    [ObservableProperty]
    private string _terracottaPlayerName = "";

    [ObservableProperty]
    private string _terracottaExecutablePath = "";

    [ObservableProperty]
    private string _terracottaStatus = "陶瓦联机由 Terracotta 提供；房主创建房间后，客人用同一房间码加入。";

    [ObservableProperty]
    private string _terracottaAddress = "";

    [ObservableProperty]
    private bool _isTerracottaBusy;

    [ObservableProperty]
    private bool _isTerracottaRunning;

    public bool HasTerracottaAddress => !string.IsNullOrWhiteSpace(TerracottaAddress);
    public bool CanStartTerracotta => !IsTerracottaBusy && !IsTerracottaRunning;
    public bool CanStopTerracotta => IsTerracottaRunning || (IsTerracottaBusy && _terracottaCts is not null);

    [ObservableProperty]
    private string _peer = "tcp://public.easytier.cn:11010";

    [ObservableProperty]
    private string _status = "输入房间码，同一房间码的玩家会加入同一个 EasyTier 网络。";

    [ObservableProperty]
    private string _virtualIp = "";

    [ObservableProperty]
    private string _publicIp = "";

    [ObservableProperty]
    private string _natText = "";

    [ObservableProperty]
    private string _hostname = "";

    [ObservableProperty]
    private string _peerSummary = "";

    [ObservableProperty]
    private string _peerTestText = "";

    [ObservableProperty]
    private bool _isTestingPeers;

    [ObservableProperty]
    private string _log = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRunning;

    public bool CanStart => !IsBusy && !IsRunning;
    public bool CanStop => !IsBusy && IsRunning;
    public bool HasVirtualIp => !string.IsNullOrWhiteSpace(VirtualIp);

    partial void OnSelectedBackendChanged(string value)
    {
        OnPropertyChanged(nameof(IsEasyTier));
        OnPropertyChanged(nameof(IsTerracotta));
        StartTerracottaCommand.NotifyCanExecuteChanged();
        StopTerracottaCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTerracottaBusyChanged(bool value) => NotifyTerracottaCommands();

    partial void OnIsTerracottaRunningChanged(bool value) => NotifyTerracottaCommands();

    partial void OnTerracottaAddressChanged(string value) =>
        OnPropertyChanged(nameof(HasTerracottaAddress));

    private void NotifyTerracottaCommands()
    {
        OnPropertyChanged(nameof(CanStartTerracotta));
        OnPropertyChanged(nameof(CanStopTerracotta));
        StartTerracottaCommand.NotifyCanExecuteChanged();
        StopTerracottaCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    partial void OnIsRunningChanged(bool value) => NotifyCommands();

    partial void OnVirtualIpChanged(string value) =>
        OnPropertyChanged(nameof(HasVirtualIp));

    partial void OnRoomCodeChanged(string value) => NotifyCommands();

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var code = RoomCode.Trim();
        if (code.Length < 4)
        {
            Status = "房间码至少 4 个字符，请与朋友使用同一个房间码。";
            return;
        }

        IsBusy = true;
        try
        {
            Status = "正在准备 EasyTier…";
            var progress = new Progress<PCL.Core.Jobs.JobProgress>(update =>
                Status = update.Percent is null ? update.Stage : $"{update.Stage} {update.Percent * 100:0}%");
            await _service.EnsureCoreAsync(progress);
            Status = "正在启动 EasyTier 虚拟网络…";
            await _service.StartAsync(code, Peer);
            IsRunning = true;
            Status = "EasyTier 已启动，正在等待虚拟 IP…";
            _pollCts = new CancellationTokenSource();
            _ = PollIpAsync(_pollCts.Token);
        }
        catch (Exception ex)
        {
            Status = "联机启动失败：" + ex.Message;
            await StopInternalAsync();
            if (AutoFallbackToTerracotta)
            {
                SelectedBackend = "陶瓦（HMCL）";
                TerracottaStatus = "EasyTier 启动失败，已切换到陶瓦联机。请选择创建/加入房间。";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        IsBusy = true;
        try
        {
            await StopInternalAsync();
            Status = "已停止 EasyTier。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopInternalAsync()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _service.Stop();
        IsRunning = false;
        VirtualIp = "";
        PublicIp = "";
        NatText = "";
        Hostname = "";
        PeerSummary = "";
    }

    private async Task PollIpAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!ct.IsCancellationRequested)
        {
            var info = await _service.TryGetNodeInfoAsync(ct);
            if (info is not null)
            {
                PublicIp = info.PublicIp ?? "";
                NatText = NatName(info.NatType);
                Hostname = info.Hostname ?? "";
                if (!string.IsNullOrWhiteSpace(info.VirtualIp))
                {
                    VirtualIp = info.VirtualIp;
                    PeerSummary = await _service.TryGetPeersAsync(ct);
                    Status = "联机已连接。朋友可在 Minecraft 中连接该虚拟 IP:25565。";
                    return;
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                PeerSummary = await _service.TryGetPeersAsync(ct);
                Status = "EasyTier 已启动，但 30 秒内没有获得虚拟 IP。"
                         + "可点击“切换到陶瓦联机”使用 HMCL 陶瓦方案。";
                if (AutoFallbackToTerracotta)
                {
                    await StopInternalAsync();
                    SelectedBackend = "陶瓦（HMCL）";
                    TerracottaStatus = "EasyTier 未获得虚拟 IP，已切换到陶瓦联机。请选择创建/加入房间；"
                                       + "房主需要先开放局域网世界。";
                }
                return;
            }
            await Task.Delay(2000, ct);
        }
    }

    private static string NatName(int? nat) => nat switch
    {
        1 => "OpenInternet",
        2 => "NoPAT",
        3 => "FullCone",
        4 => "Restricted",
        5 => "PortRestricted",
        6 => "Symmetric",
        _ => "Unknown"
    };

    [RelayCommand]
    private async Task AutoSelectPeerAsync()
    {
        if (IsTestingPeers) return;
        IsTestingPeers = true;
        PeerTestText = "正在并行测试中继…";
        try
        {
            var peers = Peer.Split([',', ';', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (peers.Length == 0)
            {
                PeerTestText = "没有填写中继地址";
                return;
            }
            var results = await Task.WhenAll(peers.Select(peer =>
                EasyTierService.TestPeerAsync(peer)));
            PeerTestText = string.Join(Environment.NewLine, results.Select(result =>
                result.Success
                    ? $"✓ {result.Address} · {result.LatencyMs} ms"
                    : $"✗ {result.Address} · {result.Error}"));
            var best = results.Where(result => result.Success)
                .OrderBy(result => result.LatencyMs)
                .FirstOrDefault();
            if (best is null)
            {
                Status = "没有可用的 TCP 中继，请检查 DNS/端口/防火墙。";
                return;
            }
            Peer = best.Address;
            Status = $"已选择最快中继：{best.Address}（{best.LatencyMs} ms）";
        }
        finally
        {
            IsTestingPeers = false;
        }
    }

    [RelayCommand]
    private async Task TestPeersAsync()
    {
        if (IsTestingPeers) return;
        IsTestingPeers = true;
        PeerTestText = "正在测试中继…";
        try
        {
            var peers = Peer.Split([',', ';', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var lines = new List<string>();
            foreach (var peer in peers)
            {
                var result = await EasyTierService.TestPeerAsync(peer);
                lines.Add(result.Success
                    ? $"✓ {result.Address} · {result.LatencyMs} ms"
                    : $"✗ {result.Address} · {result.Error}");
            }
            PeerTestText = lines.Count == 0 ? "没有填写中继地址" : string.Join(Environment.NewLine, lines);
        }
        finally
        {
            IsTestingPeers = false;
        }
    }

    [RelayCommand]
    private void SwitchToTerracotta()
    {
        SelectedBackend = "陶瓦（HMCL）";
        TerracottaStatus = "已切换到陶瓦联机。房主选择“创建房间”，客人选择“加入房间”。";
    }

    [RelayCommand(CanExecute = nameof(CanStartTerracotta))]
    private async Task StartTerracottaAsync()
    {
        var code = RoomCode.Trim();
        var isJoin = TerracottaMode == "加入房间";
        if (isJoin && (code.Length < 21 || !code.StartsWith("U/", StringComparison.OrdinalIgnoreCase)))
        {
            TerracottaStatus = "加入房间需要完整的 U/XXXX-XXXX-XXXX-XXXX 房间码。";
            return;
        }

        _terracottaCts = new CancellationTokenSource();
        IsTerracottaBusy = true;
        TerracottaStatus = "正在准备陶瓦联机…";
        try
        {
            var progress = new Progress<PCL.Core.Jobs.JobProgress>(update =>
                TerracottaStatus = update.Percent is null
                    ? update.Stage
                    : $"{update.Stage} {update.Percent * 100:0}%");
            await _terracotta.EnsureAsync(
                progress, _terracottaCts.Token,
                string.IsNullOrWhiteSpace(TerracottaExecutablePath)
                    ? null
                    : TerracottaExecutablePath.Trim());
            TerracottaStatus = "正在启动陶瓦联机进程…";
            await _terracotta.StartAsync(_terracottaCts.Token);
            IsTerracottaRunning = true;
            if (isJoin)
            {
                TerracottaStatus = "正在连接房间…";
                var address = await _terracotta.JoinAsync(
                    code, TerracottaPlayerName, _terracottaCts.Token);
                TerracottaAddress = address;
                TerracottaStatus = "已连接。请进入 Minecraft 多人游戏，连接 " + address + "。";
            }
            else
            {
                TerracottaStatus = "正在创建房间并扫描局域网世界…";
                var room = await _terracotta.HostAsync(
                    string.IsNullOrWhiteSpace(code) ? null : code,
                    TerracottaPlayerName, _terracottaCts.Token);
                RoomCode = room;
                TerracottaAddress = room;
                TerracottaStatus = "房主已就绪。把房间码发给朋友，并在 Minecraft 中开放局域网世界。";
            }
        }
        catch (OperationCanceledException)
        {
            TerracottaStatus = "已取消陶瓦联机。";
            await StopTerracottaInternalAsync();
        }
        catch (Exception ex)
        {
            TerracottaStatus = "陶瓦联机失败：" + ex.Message;
            await StopTerracottaInternalAsync();
        }
        finally
        {
            _terracottaCts?.Dispose();
            _terracottaCts = null;
            IsTerracottaBusy = false;
            NotifyTerracottaCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopTerracotta))]
    private async Task StopTerracottaAsync()
    {
        if (IsTerracottaBusy && _terracottaCts is not null)
        {
            _terracottaCts.Cancel();
            TerracottaStatus = "正在取消陶瓦联机…";
            return;
        }
        IsTerracottaBusy = true;
        try
        {
            await StopTerracottaInternalAsync();
            TerracottaStatus = "已停止陶瓦联机。";
        }
        finally
        {
            IsTerracottaBusy = false;
        }
    }

    private async Task StopTerracottaInternalAsync()
    {
        await _terracotta.StopAsync();
        IsTerracottaRunning = false;
        TerracottaAddress = "";
    }

    [RelayCommand]
    private void CopyTerracottaAddress()
    {
        if (!HasTerracottaAddress) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(TerracottaAddress);
            TerracottaStatus = "已复制：" + TerracottaAddress;
        }
    }

    [RelayCommand]
    private void CopyIp()
    {
        if (!HasVirtualIp) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(VirtualIp);
            Status = "已复制虚拟 IP：" + VirtualIp;
        }
    }

    private void AppendLog(string line)
    {
        var text = line.TrimEnd();
        if (text.Length == 0) return;
        Log = (Log + text + Environment.NewLine) is { Length: > 8000 } value
            ? value[^8000..]
            : Log + text + Environment.NewLine;
    }
}
