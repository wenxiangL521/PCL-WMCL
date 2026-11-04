using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.ViewModels;

/// <summary>Astral 联机：房间管理、中转节点、用户名与邀请导入。</summary>
public sealed record AstralMember(string Name, string VirtualIp, string Latency, string Tunnel);

public partial class AstralViewModel : ViewModelBase
{
    private readonly LinkViewModel _connection;
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
        _followAccountName = string.IsNullOrWhiteSpace(_profile.UsernameOverride);
        Username = _followAccountName ? CurrentAccountName : _profile.UsernameOverride;
        VirtualIp = _profile.VirtualIp;
        foreach (var room in _profile.Rooms) Rooms.Add(room);
        foreach (var relay in _profile.Relays) Relays.Add(relay);
        Status = Rooms.Count == 0 ? "还没有加入过房间，可以粘贴邀请或从下面的记录里选。" : "选择一个房间加入。";
    }

    private bool _followAccountName;

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
    private void AddRoom()
    {
        if (!AstralInviteParser.TryParse(InviteText, out var room, out var error) || room is null)
        {
            Status = error ?? "无法解析邀请。";
            return;
        }
        Remember(room);
        InviteText = "";
        Preview = "";
        Status = $"已添加房间：{room.Name}，可在「我的房间」里选择加入。";
    }

    [RelayCommand]
    private async Task JoinSelectedAsync()
    {
        if (SelectedRoom is not { } entry) return;
        await JoinAsync(new AstralRoom(entry.Name, entry.NetworkName, entry.NetworkSecret, entry.Peers, entry.Source));
    }

    private async Task JoinAsync(AstralRoom room)
    {
        _connection.ApplyAstralProfile(Username, VirtualIp, room);
        await _connection.JoinAstralAsync();

        // 成功才记录历史；失败保留状态供排查
        RefreshConnection();
        if (_connection.IsRunning)
        {
            StartMemberPolling();
            Remember(room);
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
            room.Name, room.NetworkName, room.NetworkSecret, room.Peers.ToList(), room.Source, DateTimeOffset.Now);
        Rooms.Insert(0, entry);
        while (Rooms.Count > 20) Rooms.RemoveAt(Rooms.Count - 1);
        _profile.Rooms = Rooms.ToList();
        Persist();
        OnPropertyChanged(nameof(HasRooms));
    }

    [ObservableProperty]
    private string _shareLink = "";

    [ObservableProperty]
    private string _newRoomName = "我的房间";

    [ObservableProperty]
    private string _newRoomNetwork = "";

    [ObservableProperty]
    private string _newRoomSecret = "";

    /// <summary>true 表示当前在「加入房间」页，false 表示在首页/创建页。</summary>
    [ObservableProperty]
    private bool _isJoinView;

    [RelayCommand]
    private void ShowCreate() => IsJoinView = false;

    [RelayCommand]
    private void ShowJoin() => IsJoinView = true;

    public ObservableCollection<AstralMember> Members { get; } = [];

    public bool HasMembers => Members.Count > 0;

    private CancellationTokenSource? _memberCts;

    /// <summary>本地创建房间：生成网络名与密钥，启动后给出 Astral 兼容分享码。</summary>
    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var room = new AstralRoom(
            string.IsNullOrWhiteSpace(NewRoomName) ? "WMCL 房间 " + suffix : NewRoomName.Trim(),
            string.IsNullOrWhiteSpace(NewRoomNetwork) ? "astral-" + suffix.ToLowerInvariant() : NewRoomNetwork.Trim(),
            string.IsNullOrWhiteSpace(NewRoomSecret) ? Guid.NewGuid().ToString("N")[..16] : NewRoomSecret.Trim(),
            Relays.Where(r => r.Enabled).Select(r => r.Uri).DefaultIfEmpty("tcp://public.easytier.top:11010").ToList(),
            "Astral");

        await JoinAsync(room);
        if (!_connection.IsRunning) return;
        ShareLink = AstralInviteParser.Encode(room.Name, room.NetworkName, room.NetworkSecret, room.Peers);
        Status = "房间已创建，把分享链接发给朋友即可加入。";
        StartMemberPolling();
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
    private async Task CopyIpAsync()
    {
        var ip = _connection.VirtualIp;
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(null)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(ip);
            Status = "已复制虚拟 IP：" + ip;
        }
        catch
        {
            Status = "复制失败，请手动复制：" + ip;
        }
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        _connection.StopCommand.Execute(null);
        await Task.Delay(200);
        RefreshConnection();
        Status = "已离开房间。";
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
        _profile.UsernameOverride = _followAccountName ? "" : Username;
        _profile.VirtualIp = VirtualIp;
        _profile.Rooms = Rooms.ToList();
        _profile.Relays = Relays.ToList();
        AstralProfileStore.Save(_profile);
    }
}
