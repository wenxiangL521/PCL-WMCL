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
    private string _log = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRunning;

    public bool CanStart => !IsBusy && !IsRunning;
    public bool CanStop => !IsBusy && IsRunning;
    public bool HasVirtualIp => !string.IsNullOrWhiteSpace(VirtualIp);

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
                         + "请检查公共中继/DNS、TUN 权限或更换中继地址。";
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
