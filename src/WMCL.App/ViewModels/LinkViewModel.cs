using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.ViewModels;

/// <summary>
/// 手动 EasyTier 联机。PCL2 官方房间配置在开源版不可获取，因此这里用
/// “房间码 = 网络名 + 派生密钥 + 公共中继”的替代方案。
/// </summary>
public partial class LinkViewModel : ViewModelBase, IDisposable
{
    private static readonly string DefaultPeer =
        string.Join("\n", EasyTierService.DefaultPeers);

    private readonly AppServices _services;
    private readonly EasyTierService _service = new();
    private readonly TerracottaService _terracotta = new();
    private readonly Func<Task<string?>> _joinPrompt;
    private readonly Func<Task>? _startRoom;
    private CancellationTokenSource? _easyTierCts;
    private CancellationTokenSource? _terracottaCts;
    private CancellationTokenSource? _terracottaPollCts;
    private CancellationTokenSource? _pollCts;

    public LinkViewModel(AppServices services) : this(services, null, null)
    {
    }

    internal LinkViewModel(AppServices services, Func<Task<string?>>? joinPrompt, Func<Task>? startRoom)
    {
        _services = services;
        _joinPrompt = joinPrompt ?? (() => DialogService.PromptAsync("加入房间", "输入你收到的房间码", ""));
        _startRoom = startRoom;
        _connectionMode = services.Settings.LinkLatencyFirst ? "优先低延迟" : "优先直连";
        _peer = string.IsNullOrWhiteSpace(services.Settings.EasyTierPeer)
            ? DefaultPeer
            : services.Settings.EasyTierPeer;
        _roomCode = services.Settings.EasyTierRoomCode;
        _selectedBackend = services.Settings.LinkBackend switch
        {
            "陶瓦（HMCL）" => "陶瓦（HMCL）",
            "Astral" => "Astral",
            _ => "EasyTier"
        };
        _terracottaMode = services.Settings.TerracottaMode == "加入房间"
            ? "加入房间"
            : "创建房间";
        _terracottaExecutablePath = services.Settings.TerracottaExecutablePath;
        _service.LogReceived += line =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendLog(line));
        };
    }

    [ObservableProperty]
    private string _roomCode = "";

    public ObservableCollection<string> Backends { get; } = ["EasyTier", "Astral", "陶瓦（HMCL）"];

    public ObservableCollection<string> ConnectionModes { get; } = ["优先直连", "优先低延迟"];

    [ObservableProperty]
    private string _connectionMode = "优先直连";

    public event EventHandler? SettingsRequested;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        if (!IsIdle) return;
        TerracottaMode = "创建房间";
        RoomCode = IsEasyTier ? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() : "";
        await StartSelectedRoomAsync();
    }

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        if (!IsIdle) return;
        var code = await _joinPrompt();
        if (string.IsNullOrWhiteSpace(code) || !IsIdle) return;
        RoomCode = code.Trim();
        TerracottaMode = "加入房间";
        await StartSelectedRoomAsync();
    }

    private Task StartSelectedRoomAsync() => _startRoom is not null ? _startRoom()
        : IsEasyTier ? StartAsync() : StartTerracottaAsync();

    public bool IsIdle => !IsBusy && !IsRunning && !IsTerracottaBusy && !IsTerracottaRunning;

    /// <summary>房间卡片只在非 Astral 模式且空闲时显示。</summary>
    public bool ShowRoomSelection => IsIdle && !IsAstral;

    /// <summary>房间面板只在非 Astral 模式显示，避免盖住 Astral 页面。</summary>
    public bool ShowRoomPanel => !IsIdle && !IsAstral;

    public string RoomStatus => IsTerracotta ? TerracottaStatus : Status;

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(RoomStatus));

    partial void OnTerracottaStatusChanged(string value) => OnPropertyChanged(nameof(RoomStatus));

    partial void OnConnectionModeChanged(string value)
    {
        _services.Settings.LinkLatencyFirst = value == "优先低延迟";
        _services.SaveSettings();
    }

    partial void OnPeerChanged(string value)
    {
        _services.Settings.EasyTierPeer = value;
        _services.SaveSettings();
    }

    partial void OnTerracottaExecutablePathChanged(string value)
    {
        _services.Settings.TerracottaExecutablePath = value;
        _services.SaveSettings();
    }

    [ObservableProperty]
    private string _selectedBackend = "EasyTier";

    public bool IsEasyTier => SelectedBackend == "EasyTier";
    public bool IsAstral => SelectedBackend == "Astral";
    public bool IsTerracotta => SelectedBackend == "陶瓦（HMCL）";

    [ObservableProperty]
    private string _terracottaMode = "创建房间";



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
    private string _terracottaPlayersText = "";

    [ObservableProperty]
    private bool _isTerracottaBusy;

    [ObservableProperty]
    private bool _isTerracottaRunning;

    public bool HasTerracottaAddress => !string.IsNullOrWhiteSpace(TerracottaAddress);
    public bool CanStartTerracotta => !IsTerracottaBusy && !IsTerracottaRunning && !IsBusy && !IsRunning;
    public bool CanStopTerracotta => IsTerracottaRunning || (IsTerracottaBusy && _terracottaCts is not null);

    [ObservableProperty]
    private string _peer = DefaultPeer;

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

    public bool CanStart => !IsBusy && !IsRunning && !IsTerracottaBusy && !IsTerracottaRunning;
    public bool CanStop => IsRunning || (IsBusy && _easyTierCts is not null);
    public bool HasVirtualIp => !string.IsNullOrWhiteSpace(VirtualIp);

    partial void OnSelectedBackendChanged(string value)
    {
        OnPropertyChanged(nameof(RoomStatus));
        _services.Settings.LinkBackend = value;
        _services.SaveSettings();
        OnPropertyChanged(nameof(IsEasyTier));
        OnPropertyChanged(nameof(IsAstral));
        OnPropertyChanged(nameof(ShowRoomSelection));
        OnPropertyChanged(nameof(ShowRoomPanel));
        OnPropertyChanged(nameof(IsTerracotta));
        if (value == "陶瓦（HMCL）")
        {
            if (IsBusy) _easyTierCts?.Cancel();
            if (IsRunning) _ = StopInternalAsync();
            if (IsBusy || IsRunning) Status = "已切换到陶瓦联机，EasyTier 已停止。";
        }
        else if (value == "Astral")
        {
            if (IsBusy) _easyTierCts?.Cancel();
            if (IsRunning) _ = StopInternalAsync();
            if (IsTerracottaBusy) _terracottaCts?.Cancel();
            if (IsTerracottaRunning) _ = StopTerracottaInternalAsync();
        }
        else if (value == "EasyTier")
        {
            if (IsTerracottaBusy) _terracottaCts?.Cancel();
            if (IsTerracottaRunning) _ = StopTerracottaInternalAsync();
            if (IsTerracottaBusy || IsTerracottaRunning)
                TerracottaStatus = "已切换到 EasyTier，陶瓦已停止。";
        }
        NotifyLinkCommands();
    }

    partial void OnIsTerracottaBusyChanged(bool value) => NotifyLinkCommands();

    partial void OnIsTerracottaRunningChanged(bool value) => NotifyLinkCommands();

    partial void OnTerracottaAddressChanged(string value) =>
        OnPropertyChanged(nameof(HasTerracottaAddress));

    private void NotifyLinkCommands()
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ShowRoomSelection));
        OnPropertyChanged(nameof(ShowRoomPanel));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanStartTerracotta));
        OnPropertyChanged(nameof(CanStopTerracotta));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        StartTerracottaCommand.NotifyCanExecuteChanged();
        StopTerracottaCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyLinkCommands();

    partial void OnIsRunningChanged(bool value) => NotifyLinkCommands();

    partial void OnVirtualIpChanged(string value) =>
        OnPropertyChanged(nameof(HasVirtualIp));

    partial void OnRoomCodeChanged(string value)
    {
        NotifyLinkCommands();
        OnPropertyChanged(nameof(RoomCaption));
    }



    [ObservableProperty]
    private string _astralInvite = "";

    [ObservableProperty]
    private string _astralStatus = "";

    /// <summary>加入外部网络时可选的虚拟 IP；留空由 EasyTier 通过 DHCP 分配。</summary>
    [ObservableProperty]
    private string _astralVirtualIp = "";

    /// <summary>加入外部网络时使用的节点名（默认跟随 MC 账号名）。</summary>
    [ObservableProperty]
    private string _astralUsername = "";

    /// <summary>外部网络（Astral 房间）里显示的网络名；为空表示 WMCL 自己的房间码。</summary>
    [ObservableProperty]
    private string _externalNetworkName = "";

    public bool IsExternalNetwork => !string.IsNullOrWhiteSpace(ExternalNetworkName);
    public string RoomCaption => IsExternalNetwork ? $"网络：{ExternalNetworkName}" : $"房间码：{RoomCode}";

    private AstralRoom? _pendingAstralRoom;

    partial void OnExternalNetworkNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsExternalNetwork));
        OnPropertyChanged(nameof(RoomCaption));
    }



    /// <summary>由 Astral 页面准备好参数后再触发加入。</summary>
    internal void ApplyAstralProfile(string username, string virtualIp, AstralRoom room)
    {
        _pendingAstralRoom = room;
        AstralVirtualIp = virtualIp;
        AstralInvite = room.NetworkName;
        AstralUsername = username;
        if (!string.IsNullOrWhiteSpace(username)) Peer = Peer; // 用户名仅用于页面展示
    }

    /// <summary>加入 Astral 系客户端分享的房间（沿用 EasyTier，网络名/密钥来自邀请）。</summary>
    /// <summary>启动器当前使用的 MC 账号名（离线/正版/第三方都用它）。</summary>
    public string McPlayerName => _services.Settings.PlayerName ?? "";

    /// <summary>当前网络里的节点列表（easytier-cli peer 的表格）。</summary>
    internal async Task<string?> EasyTierPeersAsync()
    {
        try
        {
            return await _service.TryGetPeersAsync();
        }
        catch
        {
            return null;
        }
    }

    private object? _astralPage;

    /// <summary>Astral 模式的页面内容（联机方式切到 Astral 时展示）。</summary>
    public object? AstralPage
    {
        get => _astralPage;
        internal set => SetProperty(ref _astralPage, value);
    }

    private AstralViewModel? _astral;

    /// <summary>Astral 设置页使用的视图模型（设置页 DataContext 是联机 VM）。</summary>
    public AstralViewModel? Astral
    {
        get => _astral;
        internal set => SetProperty(ref _astral, value);
    }

    [RelayCommand]
    internal async Task JoinAstralAsync()
    {
        var room = _pendingAstralRoom;
        _pendingAstralRoom = null;
        if (room is null &&
            (!AstralInviteParser.TryParse(AstralInvite, out room, out var parseError) || room is null))
        {
            AstralStatus = parseError ?? "无法解析该邀请。";
            return;
        }

        // 环回地址无法与其他机器互连（EasyTier 绑定默认网卡），提前剔除并提示
        var peers = room.Peers
            .Where(peer => !peer.Contains("127.0.0.1", StringComparison.Ordinal) &&
                           !peer.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
                           !peer.Contains("[::1]", StringComparison.Ordinal))
            .ToList();
        if (peers.Count == 0)
        {
            AstralStatus = "邀请里的节点都是本机地址（127.0.0.1），无法与其他机器互连；请让房主用可达的公网/局域网地址重新分享。";
            return;
        }

        if (!string.IsNullOrWhiteSpace(AstralVirtualIp) &&
            !NetworkSubnetProbe.IsInPrefix(AstralVirtualIp.Trim(), NetworkSubnetProbe.EasyTierDefaultPrefix))
        {
            AstralStatus = "虚拟 IP 需要属于房间网段（" + NetworkSubnetProbe.EasyTierDefaultPrefix + "0/24）。";
            return;
        }

        // 本机若已占用 EasyTier 默认网段（多为上次残留的 tun），自动清理后再加入
        EasyTierService.CleanupOrphans();
        var conflicts = NetworkSubnetProbe.FindAddresses(NetworkSubnetProbe.EasyTierDefaultPrefix);
        for (var attempt = 0; attempt < 10 && conflicts.Count > 0; attempt++)
        {
            await Task.Delay(300);
            EasyTierService.CleanupOrphans();
            conflicts = NetworkSubnetProbe.FindAddresses(NetworkSubnetProbe.EasyTierDefaultPrefix);
        }
        AstralStatus = conflicts.Count > 0
            ? $"已清理残留实例，但本机仍有 {string.Join("、", conflicts)}，将尝试直接加入。"
            : "已自动清理残留 EasyTier 实例。";

        if (IsTerracottaRunning) await StopTerracottaInternalAsync();

        _easyTierCts?.Dispose();
        _easyTierCts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            AstralStatus = "正在准备 EasyTier…";
            var progress = new Progress<WMCL.Core.Jobs.JobProgress>(update =>
                AstralStatus = update.Percent is null ? update.Stage : $"{update.Stage} {update.Percent * 100:0}%");
            await _service.EnsureCoreAsync(progress, _easyTierCts.Token);
            AstralStatus = $"正在加入 {room.Source} 房间…";
            await _service.StartExternalAsync(
                room.NetworkName, room.NetworkSecret, string.Join(",", peers),
                string.IsNullOrWhiteSpace(AstralVirtualIp) ? null : AstralVirtualIp.Trim(),
                _easyTierCts.Token, _services.Settings.LinkLatencyFirst,
                string.IsNullOrWhiteSpace(AstralUsername) ? McPlayerName : AstralUsername);
            IsRunning = true;
            ExternalNetworkName = room.NetworkName;
            Status = $"已加入 {room.Source} 房间：{room.Name}";
            AstralStatus = peers.Count < room.Peers.Count
                ? $"已加入 {room.Source} 房间：{room.Name}（已忽略 {room.Peers.Count - peers.Count} 个本机地址节点）"
                : $"已加入 {room.Source} 房间：{room.Name}";
            _pollCts?.Dispose();
            _pollCts = new CancellationTokenSource();
            _ = PollIpAsync(_pollCts.Token);
        }
        catch (OperationCanceledException)
        {
            AstralStatus = "已取消加入。";
            await StopInternalAsync();
        }
        catch (Exception ex)
        {
            AstralStatus = "加入失败：" + ex.Message;
            await StopInternalAsync();
        }
        finally
        {
            IsBusy = false;
            NotifyLinkCommands();
        }
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

        if (IsTerracottaRunning)
        {
            await StopTerracottaInternalAsync();
            TerracottaStatus = "陶瓦已停止，正在启动 EasyTier。";
        }

        SaveLinkSettings();
        _easyTierCts?.Dispose();
        _easyTierCts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            Status = "正在准备 EasyTier…";
            var progress = new Progress<WMCL.Core.Jobs.JobProgress>(update =>
                Status = update.Percent is null ? update.Stage : $"{update.Stage} {update.Percent * 100:0}%");
            await _service.EnsureCoreAsync(progress, _easyTierCts.Token);
            Status = "正在启动 EasyTier 虚拟网络…";
            await _service.StartAsync(code, Peer, _easyTierCts.Token, _services.Settings.LinkLatencyFirst);
            IsRunning = true;
            Status = "EasyTier 已启动，正在等待虚拟 IP…";
            _pollCts?.Dispose();
            _pollCts = new CancellationTokenSource();
            _ = PollIpAsync(_pollCts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消 EasyTier 启动。";
            await StopInternalAsync();
        }
        catch (Exception ex)
        {
            Status = "联机启动失败：" + ex.Message;
            await StopInternalAsync();
            // 不再自动切换到陶瓦：由用户在「设置 → 联机方式」里手动选择
        }
        finally
        {
            _easyTierCts?.Dispose();
            _easyTierCts = null;
            IsBusy = false;
            NotifyLinkCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (IsBusy && _easyTierCts is not null)
        {
            _easyTierCts.Cancel();
            Status = "正在取消 EasyTier…";
            return;
        }
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

    /// <summary>供 Astral 页面离开房间时调用：取消并等待 EasyTier 真正退出。</summary>
    internal async Task StopForLeaveAsync()
    {
        _easyTierCts?.Cancel();
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
        ExternalNetworkName = "";
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

    public void Dispose()
    {
        if (IsRunning || IsBusy)
            _ = StopInternalAsync();
        if (IsTerracottaRunning || IsTerracottaBusy)
            _ = StopTerracottaInternalAsync();
        _easyTierCts?.Cancel();
        _easyTierCts?.Dispose();
        _easyTierCts = null;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _terracottaCts?.Cancel();
        _terracottaCts?.Dispose();
        _terracottaCts = null;
        _terracottaPollCts?.Cancel();
        _terracottaPollCts?.Dispose();
        _terracottaPollCts = null;
        _service.Dispose();
        _terracotta.Dispose();
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
    private async Task DiscoverNodesAsync()
    {
        if (IsTestingPeers) return;
        IsTestingPeers = true;
        PeerTestText = "正在从 EasyTier Uptime 获取公共节点…";
        try
        {
            var nodes = await _service.FetchPublicNodesAsync();
            Peer = string.Join("\n", nodes);
            PeerTestText = nodes.SequenceEqual(EasyTierService.DefaultPeers)
                ? "节点源不可达，已使用内置公共节点"
                : $"已获取 {nodes.Count} 个公共节点";
        }
        catch (Exception ex)
        {
            PeerTestText = "获取节点失败：" + ex.Message;
        }
        finally
        {
            IsTestingPeers = false;
        }
    }

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
            var results = await Task.WhenAll(peers.Select(peer =>
                EasyTierService.TestPeerAsync(peer)));
            PeerTestText = results.Length == 0
                ? "没有填写中继地址"
                : string.Join(Environment.NewLine, results.Select(result => result.Success
                    ? $"✓ {result.Address} · {result.LatencyMs} ms"
                    : $"✗ {result.Address} · {result.Error}"));
        }
        finally
        {
            IsTestingPeers = false;
        }
    }

    private void SwitchToTerracottaWith(string message)
    {
        SelectedBackend = "陶瓦（HMCL）";
        // EasyTier 房间码不是陶瓦的 U/XXXX-... 格式；回退时清空，避免误当客人加入。
        if (!RoomCode.Trim().StartsWith("U/", StringComparison.OrdinalIgnoreCase))
            RoomCode = "";
        TerracottaStatus = message;
    }

    [RelayCommand]
    private void SwitchToTerracotta() =>
        SwitchToTerracottaWith("已切换到陶瓦联机。房主选择“创建房间”，客人选择“加入房间”。");

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

        if (IsRunning)
        {
            await StopInternalAsync();
            Status = "EasyTier 已停止，正在启动陶瓦。";
        }

        SaveLinkSettings();
        _terracottaCts?.Dispose();
        _terracottaCts = new CancellationTokenSource();
        IsTerracottaBusy = true;
        TerracottaStatus = "正在准备陶瓦联机…";
        try
        {
            var progress = new Progress<WMCL.Core.Jobs.JobProgress>(update =>
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
            _terracottaPollCts?.Dispose();
            _terracottaPollCts = new CancellationTokenSource();
            _ = PollTerracottaAsync(_terracottaPollCts.Token);
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
            NotifyLinkCommands();
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
        _terracottaPollCts?.Cancel();
        _terracottaPollCts?.Dispose();
        _terracottaPollCts = null;
        await _terracotta.StopAsync();
        IsTerracottaRunning = false;
        TerracottaAddress = "";
        TerracottaPlayersText = "";
    }

    private async Task PollTerracottaAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, ct);
                var snapshot = await _terracotta.GetSnapshotAsync(ct);
                if (snapshot.Url is not null) TerracottaAddress = snapshot.Url;
                else if (snapshot.Room is not null) TerracottaAddress = snapshot.Room;
                TerracottaPlayersText = snapshot.Players.Count == 0
                    ? ""
                    : "玩家：" + string.Join("、", snapshot.Players);
                switch (snapshot.State)
                {
                    case "host-ok":
                        TerracottaStatus = "房主在线。朋友可用房间码加入；请保持 Minecraft 局域网世界开启。";
                        break;
                    case "guest-ok":
                        TerracottaStatus = "已连接房主。请连接 " + (snapshot.Url ?? "显示的地址") + "。";
                        break;
                    case "exception":
                        TerracottaStatus = "陶瓦会话异常，请停止后重试。";
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                TerracottaStatus = "读取陶瓦状态失败：" + ex.Message;
                return;
            }
        }
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
    private void CopyRoomCode()
    {
        if (string.IsNullOrWhiteSpace(RoomCode)) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(RoomCode);
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

    private void SaveLinkSettings()
    {
        _services.Settings.EasyTierPeer = Peer;
        _services.Settings.EasyTierRoomCode = RoomCode;
        _services.Settings.LinkBackend = SelectedBackend;
        _services.Settings.TerracottaMode = TerracottaMode;
        _services.Settings.TerracottaExecutablePath = TerracottaExecutablePath;
        _services.SaveSettings();
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
