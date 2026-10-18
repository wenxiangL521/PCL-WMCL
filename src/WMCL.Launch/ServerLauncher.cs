using System.Diagnostics;

namespace WMCL.Launch;

/// <summary>启动/停止原版 Java 服务端，并转发 stdout/stderr。</summary>
public sealed class ServerLauncher : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };
    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    public void Start(
        string javaPath,
        string serverDirectory,
        string serverJar,
        int memoryMb = 2048)
    {
        if (IsRunning) throw new InvalidOperationException("服务端已经在运行。");
        if (!File.Exists(javaPath)) throw new FileNotFoundException("Java 不存在。", javaPath);
        if (!Directory.Exists(serverDirectory))
            throw new DirectoryNotFoundException(serverDirectory);

        var jar = Path.Combine(serverDirectory, serverJar);
        if (!File.Exists(jar)) throw new FileNotFoundException("server.jar 不存在。", jar);

        var start = new ProcessStartInfo(javaPath)
        {
            WorkingDirectory = serverDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add($"-Xmx{Math.Max(512, memoryMb)}M");
        start.ArgumentList.Add("-Xms512M");
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(serverJar);
        start.ArgumentList.Add("--nogui");

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(e.Data);
        };
        process.Exited += (_, _) =>
        {
            var code = process.ExitCode;
            _process = null;
            Exited?.Invoke(code);
        };
        if (!process.Start()) throw new InvalidOperationException("无法启动服务端进程。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
    }

    public async Task SendCommandAsync(string command, CancellationToken ct = default)
    {
        var process = _process;
        if (process is null || process.HasExited)
            throw new InvalidOperationException("服务端未运行。");
        await process.StandardInput.WriteLineAsync(command.AsMemory(), ct).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> StopAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var process = _process;
        if (process is null || process.HasExited) return true;

        try
        {
            await process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // 进程可能已退出
        }
        catch (InvalidOperationException)
        {
            // 标准输入已关闭
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return false;
        }
    }

    public void Dispose()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        process.Dispose();
    }
}
