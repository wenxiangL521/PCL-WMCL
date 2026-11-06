using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.Features.Link;

public partial class LinkViewModel
{
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
}
