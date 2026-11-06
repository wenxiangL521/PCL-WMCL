using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.Features.Link;

public partial class LinkViewModel
{
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
}
