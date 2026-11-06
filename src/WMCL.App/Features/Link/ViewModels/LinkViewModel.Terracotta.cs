using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.Features.Link;

public partial class LinkViewModel
{
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
        if (WMCL.App.Services.ClipboardService.TrySetText(TerracottaAddress))
            TerracottaStatus = "已复制：" + TerracottaAddress;
    }
}
