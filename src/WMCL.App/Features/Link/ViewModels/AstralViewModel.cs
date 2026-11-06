using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.Features.Link;

/// <summary>Astral 联机：房间管理、中转节点、用户名与邀请导入。</summary>
public sealed record AstralMember(string Name, string VirtualIp, string Latency, string Tunnel);

public partial class AstralViewModel : ViewModelBase
{
    private readonly LinkViewModel _connection;
    private readonly AstralShareCodeService _shareCodes = new();
    private readonly AstralProfile _profile;

    private readonly LaunchViewModel? _launch;

    public AstralViewModel(LinkViewModel connection, LaunchViewModel? launch = null)
    {
        _connection = connection;
        _launch = launch;
        if (launch is not null) launch.PropertyChanged += OnLaunchChanged;
        connection.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(LinkViewModel.VirtualIp)) RefreshConnection();
        };
        _profile = AstralProfileStore.Load();
        // 先把磁盘内容装回集合，再赋属性：属性 setter 会触发保存，
        // 顺序反了会用空集合覆盖磁盘上的房间与中转节点。
        foreach (var room in _profile.Rooms) Rooms.Add(room);
        foreach (var relay in _profile.Relays) Relays.Add(relay);
        _followAccountName = string.IsNullOrWhiteSpace(_profile.UsernameOverride);
        // 先赋值再打开写盘开关：构造期赋值不应触发保存，否则并行测试会互相抢写 astral.json
        Username = _followAccountName ? CurrentAccountName : _profile.UsernameOverride;
        VirtualIp = _profile.VirtualIp;
        _loaded = true;
        Status = Rooms.Count == 0 ? "还没有加入过房间，可以粘贴邀请或从下面的记录里选。" : "选择一个房间加入。";
    }

    private bool _followAccountName;

    /// <summary>构造完成前不写盘，避免用半初始化的集合覆盖配置。</summary>
    private bool _loaded;

    [ObservableProperty]
    private string _username = "";

    /// <summary>当前实际登录使用的账号名（正版/第三方优先，其次离线名）。</summary>
    private string CurrentAccountName
    {
        get
        {
            var active = _launch?.PlayerName?.Trim();
            return string.IsNullOrWhiteSpace(active) ? _connection.McPlayerName : active!;
        }
    }

    public string AccountNameHint => string.IsNullOrWhiteSpace(CurrentAccountName)
        ? "启动器里还没有设置账号名"
        : "当前账号：" + CurrentAccountName;

    private void OnLaunchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName != nameof(LaunchViewModel.PlayerName)) return;
        OnPropertyChanged(nameof(AccountNameHint));
        if (_followAccountName) Username = CurrentAccountName;
    }

    [ObservableProperty]
    private string _virtualIp = "";

    [ObservableProperty]
    private string _inviteText = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string _newRelayUri = "";

    [ObservableProperty]
    private string _newRelayName = "";

    [ObservableProperty]
    private AstralRoomEntry? _selectedRoom;

    [ObservableProperty]
    private AstralRelay? _selectedRelay;

    public ObservableCollection<AstralRoomEntry> Rooms { get; } = [];
    public ObservableCollection<AstralRelay> Relays { get; } = [];
    public bool HasRooms => Rooms.Count > 0;
    public bool HasRelays => Relays.Count > 0;
    public string ProfilePath => AstralProfileStore.FilePath;

    public bool IsConnected => _connection.IsRunning;
    public string ConnectedNetwork => string.IsNullOrWhiteSpace(_connection.ExternalNetworkName)
        ? "未连接"
        : _connection.ExternalNetworkName;
    public string ConnectedIp => string.IsNullOrWhiteSpace(_connection.VirtualIp) ? "等待分配…" : _connection.VirtualIp;

    private void RefreshConnection()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ShowHome));
        OnPropertyChanged(nameof(ShowCreateForm));
        OnPropertyChanged(nameof(ShowJoinPanel));
        OnPropertyChanged(nameof(ConnectedNetwork));
        OnPropertyChanged(nameof(ConnectedIp));
    }

    partial void OnUsernameChanged(string value)
    {
        _followAccountName = string.Equals(value.Trim(), CurrentAccountName, StringComparison.Ordinal);
        Persist();
    }

    /// <summary>恢复为跟随启动器里的 MC 账号名。</summary>
    [RelayCommand]
    private void UseAccountName()
    {
        _followAccountName = true;
        Username = CurrentAccountName;
        Status = "用户名已跟随当前账号：" + Username;
    }
    partial void OnVirtualIpChanged(string value) => Persist();

    partial void OnInviteTextChanged(string value)
    {
        if (!AstralInviteParser.TryParse(value, out var room, out var error) || room is null)
        {
            Preview = string.IsNullOrWhiteSpace(value) ? "" : "无法解析：" + error;
            return;
        }
        Preview = AstralInviteParser.UsedFallbackPeers(room)
            ? $"房间：{room.Name} · 来源：{room.Source} · 邀请未带节点，将使用 2 个公共中继"
            : $"房间：{room.Name} · 来源：{room.Source} · 节点 {room.Peers.Count} 个";
    }

    [RelayCommand]
    private void PreviewInvite() => OnInviteTextChanged(InviteText);

    [RelayCommand]
    private async Task JoinFromInviteAsync()
    {
        if (!AstralInviteParser.TryParse(InviteText, out var room, out var error) || room is null)
        {
            Status = error ?? "无法解析邀请。";
            return;
        }
        await JoinAsync(room);
    }

    /// <summary>只把邀请里的房间存进记录，不立即连接。</summary>
    [RelayCommand]
    private async Task AddRoomAsync()
    {
        if (!AstralInviteParser.TryParse(InviteText, out var room, out var error) || room is null)
        {
            Status = error ?? "无法解析邀请。";
            return;
        }

        // 短码先换成真实房间参数，列表里才能显示房名与节点数
        if (room.NeedsResolve && room.ShortCode is { } code)
        {
            Status = $"正在解析短码 {code}…";
            try
            {
                var resolved = await _shareCodes.ResolveAsync(code);
                Remember(resolved);
                InviteText = "";
                Preview = "";
                Status = $"已添加房间：{resolved.Name}，可在「房间列表」里选择加入。";
                return;
            }
            catch (Exception ex)
            {
                Remember(room);
                InviteText = "";
                Preview = "";
                Status = $"已添加短码房间，但解析失败（{ex.Message}），加入时会重试。";
                return;
            }
        }

        Remember(room);
        InviteText = "";
        Preview = "";
        Status = $"已添加房间：{room.Name}，可在「房间列表」里选择加入。";
    }

    [RelayCommand]
    private async Task JoinSelectedAsync()
    {
        if (SelectedRoom is not { } entry) return;
        await JoinAsync(new AstralRoom(entry.Name, entry.NetworkName, entry.NetworkSecret, entry.Peers, entry.Source)
        {
            ShortCode = entry.ShortCode,
            IsProtected = entry.IsProtected
        });
    }

    private async Task JoinAsync(AstralRoom room)
    {
        if (room.NeedsResolve && room.ShortCode is { } code)
        {
            Status = $"正在解析短码 {code}…";
            try
            {
                room = await _shareCodes.ResolveAsync(code);
            }
            catch (Exception ex)
            {
                Status = "短码解析失败：" + ex.Message;
                return;
            }
        }

        // 先校验信息完整性，避免不完整的房间也去启动 EasyTier
        if (string.IsNullOrWhiteSpace(room.NetworkName))
        {
            Status = "房间信息不完整，无法加入；请重新添加该邀请。";
            return;
        }

        _connection.ApplyAstralProfile(Username, VirtualIp, room);
        await _connection.JoinAstralAsync();

        RefreshConnection();
        if (_connection.IsRunning)
        {
            StartMemberPolling();
            Remember(room);
            await BuildShareLinksAsync(room);
            Status = $"已加入 {room.Name}（{room.Source}）。";
        }
        else
        {
            Status = _connection.AstralStatus;
        }
    }

    private void Remember(AstralRoom room)
    {
        var existing = Rooms.FirstOrDefault(item =>
            item.NetworkName == room.NetworkName && item.NetworkSecret == room.NetworkSecret);
        if (existing is not null) Rooms.Remove(existing);
        var entry = new AstralRoomEntry(
            room.Name, room.NetworkName, room.NetworkSecret, room.Peers.ToList(), room.Source,
            DateTimeOffset.Now, room.ShortCode, room.IsProtected);
        Rooms.Insert(0, entry);
        while (Rooms.Count > 20) Rooms.RemoveAt(Rooms.Count - 1);
        _profile.Rooms = Rooms.ToList();
        Persist();
        OnPropertyChanged(nameof(HasRooms));
    }

    /// <summary>短链（AstralGame 短码服务生成，可能为空）。</summary>
    [ObservableProperty]
    private string _shareLink = "";

    /// <summary>长链（离线邀请 astral://room?code=…，任何客户端都能解析）。</summary>
    [ObservableProperty]
    private string _longShareLink = "";

    [ObservableProperty]
    private string _newRoomName = "我的房间";

    [ObservableProperty]
    private string _newRoomNetwork = "";

    [ObservableProperty]
    private string _newRoomSecret = "";

    /// <summary>创建房间时是否设置密码（保护房间）。</summary>
    [ObservableProperty]
    private bool _isProtectedRoom = true;

    /// <summary>创建房间时可勾选的中转服务器。</summary>
    public ObservableCollection<AstralRelayChoice> CreateRelays { get; } = [];

    /// <summary>把已启用的中转节点复制到创建表单，供本次房间勾选。</summary>
    private void RefreshCreateRelays()
    {
        CreateRelays.Clear();
        foreach (var relay in Relays)
            CreateRelays.Add(new AstralRelayChoice(relay.Uri, relay.Name, isSelected: false));
        if (CreateRelays.Count == 0)
        {
            foreach (var uri in AstralInviteParser.DefaultPeers())
                CreateRelays.Add(new AstralRelayChoice(uri, "", isSelected: false));
        }
    }

    /// <summary>当前页面：0 首页、1 创建房间、2 加入房间。</summary>
    [ObservableProperty]
    private int _pageIndex;

    public bool ShowHome => PageIndex == 0 && !IsConnected;
    public bool ShowCreateForm => PageIndex == 1 && !IsConnected;
    public bool ShowJoinPanel => PageIndex == 2 && !IsConnected;

    public bool IsHomeView => PageIndex == 0;
    public bool IsCreateView => PageIndex == 1;
    public bool IsJoinView => PageIndex == 2;

    partial void OnPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsHomeView));
        OnPropertyChanged(nameof(IsCreateView));
        OnPropertyChanged(nameof(IsJoinView));
        OnPropertyChanged(nameof(ShowHome));
        OnPropertyChanged(nameof(ShowCreateForm));
        OnPropertyChanged(nameof(ShowJoinPanel));
    }

    [RelayCommand]
    private void GoHome() => PageIndex = 0;

    [RelayCommand]
    private void ShowCreate()
    {
        RefreshCreateRelays();
        PageIndex = 1;
    }

    [RelayCommand]
    private void ShowJoin() => PageIndex = 2;

    public ObservableCollection<AstralMember> Members { get; } = [];

    public bool HasMembers => Members.Count > 0;

    private CancellationTokenSource? _memberCts;

    /// <summary>本地创建房间：生成网络名与密钥，启动后给出 Astral 兼容分享码。</summary>
    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var peers = CreateRelays.Where(choice => choice.IsSelected).Select(choice => choice.Uri).ToList();
        if (peers.Count == 0) peers = AstralInviteParser.DefaultPeers();

        // 保护房间：房间号与密码都由程序生成；不保护房间：使用手填值，留空则无密码
        var secret = IsProtectedRoom
            ? Guid.NewGuid().ToString("N")[..16]
            : NewRoomSecret.Trim();

        var room = new AstralRoom(
            string.IsNullOrWhiteSpace(NewRoomName) ? "WMCL 房间 " + suffix : NewRoomName.Trim(),
            IsProtectedRoom || string.IsNullOrWhiteSpace(NewRoomNetwork)
                ? "astral-" + suffix.ToLowerInvariant()
                : NewRoomNetwork.Trim(),
            secret,
            peers,
            "Astral")
        {
            IsProtected = IsProtectedRoom
        };

        await JoinAsync(room);
        if (!_connection.IsRunning) return;
        Status = ShareLink.Length == 0
            ? "房间已创建；短链服务不可用，请把长链发给朋友。"
            : "房间已创建，短链与长链都可以发给朋友。";
    }

    private void ApplyMembers(string table)
    {
        Members.Clear();
        foreach (var line in table.Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith('|') || text.Contains("hostname") || text.Contains("---")) continue;
            var cells = text.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 2 || cells[1].Length == 0) continue;
            Members.Add(new AstralMember(
                cells[1],
                cells[0].Length == 0 ? "—" : cells[0],
                cells.Length > 3 ? cells[3] : "—",
                cells.Length > 7 ? cells[7] : "—"));
        }
        OnPropertyChanged(nameof(HasMembers));
    }

    /// <summary>为当前房间生成短链与长链（加入与创建共用）。</summary>
    private async Task BuildShareLinksAsync(AstralRoom room)
    {
        LongShareLink = BuildLongLink(room);
        ShareLink = room.ShortCode is { Length: > 0 } code
            ? AstralInviteParser.ShortLink(code)
            : await BuildShortLinkAsync(room);
    }

    /// <summary>离线邀请长链：不依赖任何服务，Astral 原版与 AstralGame 都能解析。</summary>
    internal static string BuildLongLink(AstralRoom room) =>
        AstralInviteParser.Encode(room.Name, room.NetworkName, room.NetworkSecret, room.Peers, room.IsProtected);

    /// <summary>申请 AstralGame 短码；失败返回空串，由调用方提示改用长链。</summary>
    private async Task<string> BuildShortLinkAsync(AstralRoom room)
    {
        try
        {
            var code = await _shareCodes.CreateAsync(room);
            return AstralInviteParser.ShortLink(code);
        }
        catch
        {
            return "";
        }
    }

    private void StartMemberPolling()
    {
        _memberCts?.Cancel();
        _memberCts = new CancellationTokenSource();
        var token = _memberCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _connection.IsRunning)
            {
                var peers = await _connection.EasyTierPeersAsync();
                if (!string.IsNullOrWhiteSpace(peers))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyMembers(peers));
                await Task.Delay(3000, token);
            }
        }, token);
    }

    [RelayCommand]
    private Task CopyShortLinkAsync() => CopyTextAsync(ShareLink, "短链");

    [RelayCommand]
    private Task CopyLongLinkAsync() => CopyTextAsync(LongShareLink, "长链");

    private async Task CopyTextAsync(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Status = $"{label}还没生成。";
            return;
        }
        try
        {
            if (await WMCL.App.Services.ClipboardService.SetTextAsync(text))
            {
                Status = $"已复制{label}。";
                return;
            }
            Status = $"复制{label}失败：没有可用的剪贴板。";
        }
        catch (Exception ex)
        {
            Status = $"复制{label}失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyIpAsync()
    {
        var ip = _connection.VirtualIp;
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            if (await WMCL.App.Services.ClipboardService.SetTextAsync(ip))
            {
                Status = "已复制虚拟 IP：" + ip;
                return;
            }
            Status = "复制失败：没有可用的剪贴板。";
        }
        catch
        {
            Status = "复制失败，请手动复制：" + ip;
        }
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        Status = "正在离开房间…";
        await _connection.StopForLeaveAsync();
        RefreshConnection();
        Members.Clear();
        OnPropertyChanged(nameof(HasMembers));
        ShareLink = "";
        LongShareLink = "";
        Status = _connection.IsRunning ? "停止 EasyTier 失败，请稍后重试。" : "已离开房间。";
    }

    [RelayCommand]
    private void RemoveRoom()
    {
        if (SelectedRoom is not { } entry) return;
        Rooms.Remove(entry);
        SelectedRoom = null;
        _profile.Rooms = Rooms.ToList();
        Persist();
        OnPropertyChanged(nameof(HasRooms));
        Status = "已从记录中移除：" + entry.Name;
    }

    [RelayCommand]
    private void AddRelay()
    {
        var uri = NewRelayUri.Trim();
        if (uri.Length == 0)
        {
            Status = "节点地址不能为空。";
            return;
        }
        if (Relays.Any(item => string.Equals(item.Uri, uri, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "该节点已存在。";
            return;
        }
        Relays.Add(new AstralRelay(uri, NewRelayName.Trim(), true));
        NewRelayUri = "";
        NewRelayName = "";
        _profile.Relays = Relays.ToList();
        Persist();
        OnPropertyChanged(nameof(HasRelays));
        Status = "已添加节点：" + uri;
    }

    [RelayCommand]
    private void RemoveRelay()
    {
        if (SelectedRelay is not { } relay) return;
        Relays.Remove(relay);
        SelectedRelay = null;
        _profile.Relays = Relays.ToList();
        Persist();
        OnPropertyChanged(nameof(HasRelays));
        Status = "已删除节点：" + relay.Uri;
    }

    [RelayCommand]
    private void ToggleRelay()
    {
        if (SelectedRelay is not { } relay) return;
        var index = Relays.IndexOf(relay);
        if (index < 0) return;
        var updated = relay with { Enabled = !relay.Enabled };
        Relays[index] = updated;
        SelectedRelay = updated;
        _profile.Relays = Relays.ToList();
        Persist();
        Status = (updated.Enabled ? "已启用：" : "已禁用：") + updated.Uri;
    }

    private void Persist()
    {
        if (!_loaded) return;
        _profile.UsernameOverride = _followAccountName ? "" : Username;
        _profile.VirtualIp = VirtualIp;
        _profile.Rooms = Rooms.ToList();
        _profile.Relays = Relays.ToList();
        AstralProfileStore.Save(_profile);
    }
}

/// <summary>创建房间时可勾选的中转节点。</summary>
public sealed partial class AstralRelayChoice(string uri, string name, bool isSelected) : ObservableObject
{
    public string Uri { get; } = uri;

    private readonly string _name = name;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    public string Display => string.IsNullOrWhiteSpace(_name) ? Uri : $"{_name}（{Uri}）";
}
