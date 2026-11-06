using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.Features.Link;

/// <summary>
/// 手动 EasyTier 联机。PCL2 官方房间配置在开源版不可获取，因此这里用
/// “房间码 = 网络名 + 派生密钥 + 公共中继”的替代方案。
/// </summary>
public partial class LinkViewModel : ViewModelBase, IDisposable
{
    private static readonly string DefaultPeer =
        string.Join("\n", EasyTierService.DefaultPeers);

    private readonly IUserSettingsStore _services;
    private readonly EasyTierService _service = new();
    private readonly TerracottaService _terracotta = new();
    private readonly Func<Task<string?>> _joinPrompt;
    private readonly Func<Task>? _startRoom;
    private CancellationTokenSource? _easyTierCts;
    private CancellationTokenSource? _terracottaCts;
    private CancellationTokenSource? _terracottaPollCts;
    private CancellationTokenSource? _pollCts;

    public LinkViewModel(IUserSettingsStore services) : this(services, null, null)
    {
    }

    internal LinkViewModel(IUserSettingsStore services, Func<Task<string?>>? joinPrompt, Func<Task>? startRoom)
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
            UiDispatcher.Post(() => AppendLog(line));
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

    [RelayCommand]
    private void CopyRoomCode()
    {
        if (string.IsNullOrWhiteSpace(RoomCode)) return;
        WMCL.App.Services.ClipboardService.TrySetText(RoomCode);
    }

    [RelayCommand]
    private void CopyIp()
    {
        if (!HasVirtualIp) return;
        if (WMCL.App.Services.ClipboardService.TrySetText(VirtualIp))
            Status = "已复制虚拟 IP：" + VirtualIp;
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
