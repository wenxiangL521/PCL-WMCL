using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.Features.More;

public partial class ToolboxViewModel
{
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

    private static Avalonia.Media.Imaging.Bitmap? DecodeFavicon(string? base64) =>
        ImageDecoder.FromFavicon(base64);
}
