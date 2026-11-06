using WMCL.Core;
using System.Diagnostics;

namespace WMCL.App.Services;

/// <summary>
/// 背景音乐：调用系统 ffplay 顺序/随机播放文件夹内的音频。
/// PCL2 使用 WPF MediaPlayer；Linux 下用 ffplay 作为轻量替代。
/// </summary>
public sealed class MusicService : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Process? _process;

    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _process is { HasExited: false };
        }
    }

    /// <summary>当前平台是否具备播放能力（Windows 走 MCI，其余需要 ffplay）。</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || FindPlaybackExecutable() is not null;

    /// <summary>按平台选择播放器：Windows/macOS 上 ffplay 通常不存在，交给用户系统播放器前先探测。</summary>
    private static string? FindPlaybackExecutable()
    {
        foreach (var candidate in OperatingSystem.IsWindows() ? new[] { "ffplay.exe", "ffplay" } : new[] { "ffplay" })
        {
            try
            {
                var probe = new ProcessStartInfo(candidate)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                probe.ArgumentList.Add("-version");
                using var process = Process.Start(probe);
                if (process is null) continue;
                if (!process.WaitForExit(1500)) { try { process.Kill(true); } catch (InvalidOperationException) { /* 进程已退出 */ } catch (System.ComponentModel.Win32Exception) { /* 无权限结束，忽略 */ } continue; }
                if (process.ExitCode == 0) return candidate;
            }
            catch
            {
                // 未安装
            }
        }
        return null;
    }

    public void Start(string? folder, int volume)
    {
        Stop();
        var player = FindPlaybackExecutable();
        if (player is null && !OperatingSystem.IsWindows()) return;
        var files = MusicPlaylist.Enumerate(folder);
        if (files.Count == 0) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoopAsync(files, Math.Clamp(volume, 0, 100), cts.Token, player);
    }

    private bool _pausedForGame;

    /// <summary>游戏开始时按设置暂停音乐，结束后恢复（只在因游戏暂停过时才恢复）。</summary>
    public void SyncGameRunning(bool running, AppSettings settings)
    {
        if (running)
        {
            if (settings.MusicPauseOnGame && IsPlaying)
            {
                Stop();
                _pausedForGame = true;
            }
        }
        else if (_pausedForGame)
        {
            _pausedForGame = false;
            Start(settings.MusicFolder, settings.MusicVolume);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Process? process;
        lock (_gate)
        {
            cts = _cts;
            process = _process;
            _cts = null;
            _process = null;
        }
        cts?.Cancel();
        cts?.Dispose();
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
        process?.Dispose();
    }

    private async Task LoopAsync(IReadOnlyList<string> files, int volume, CancellationToken ct, string? player)
    {
        var order = files.ToList();
        var random = new Random();
        while (!ct.IsCancellationRequested)
        {
            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            foreach (var file in order)
            {
                ct.ThrowIfCancellationRequested();
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        await WindowsMciAudio.PlayAsync(file, volume, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                    catch { /* 单个文件失败继续下一首 */ }
                    continue;
                }
                try
                {
                    var startInfo = new ProcessStartInfo(player!)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    startInfo.ArgumentList.Add("-nodisp");
                    startInfo.ArgumentList.Add("-autoexit");
                    startInfo.ArgumentList.Add("-loglevel");
                    startInfo.ArgumentList.Add("quiet");
                    startInfo.ArgumentList.Add("-volume");
                    startInfo.ArgumentList.Add(volume.ToString());
                    startInfo.ArgumentList.Add(file);

                    using var process = Process.Start(startInfo);
                    if (process is null) return;
                    lock (_gate) _process = process;
                    await process.WaitForExitAsync(ct).ConfigureAwait(false);
                    lock (_gate) _process = null;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    lock (_gate) _process = null;
                }
            }
        }
    }

    public void Dispose() => Stop();
}
