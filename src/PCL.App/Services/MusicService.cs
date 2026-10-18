using System.Diagnostics;
using PCL.Core.Music;

namespace PCL.App.Services;

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

    public void Start(string? folder, int volume)
    {
        Stop();
        var files = MusicPlaylist.Enumerate(folder);
        if (files.Count == 0) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoopAsync(files, Math.Clamp(volume, 0, 100), cts.Token);
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

    private async Task LoopAsync(IReadOnlyList<string> files, int volume, CancellationToken ct)
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
                try
                {
                    var startInfo = new ProcessStartInfo("ffplay")
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
