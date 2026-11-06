using System.Runtime.InteropServices;
using System.Text;

namespace WMCL.App.Services;

/// <summary>Windows 上不依赖外部程序的音频播放（winmm MCI，支持 wav/mp3/wma 等）。</summary>
internal static class WindowsMciAudio
{
    private const string Alias = "wmclmusic";

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwnd);

    private static string Query(string command)
    {
        var buffer = new StringBuilder(64);
        return MciSendString(command, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : "";
    }

    /// <summary>播放一个文件并等待播完；<paramref name="ct"/> 取消时立即停止。</summary>
    public static async Task PlayAsync(string file, int volume, CancellationToken ct)
    {
        MciSendString($"close {Alias}", null, 0, IntPtr.Zero);
        if (MciSendString($"open \"{file}\" type mpegvideo alias {Alias}", null, 0, IntPtr.Zero) != 0)
            throw new InvalidOperationException("无法打开音频文件");
        try
        {
            MciSendString($"setaudio {Alias} volume to {Math.Clamp(volume, 0, 100) * 10}", null, 0, IntPtr.Zero);
            MciSendString($"play {Alias}", null, 0, IntPtr.Zero);
            // 轮询播放状态，直到结束或取消
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                var mode = Query($"status {Alias} mode");
                if (mode.Length == 0 || mode.Trim() == "stopped") return;
            }
            MciSendString($"stop {Alias}", null, 0, IntPtr.Zero);
        }
        finally
        {
            MciSendString($"close {Alias}", null, 0, IntPtr.Zero);
        }
    }
}
