using System.Diagnostics;
using WMCL.Launch.Java;

namespace WMCL.Launch;

public static class PreLaunchCommandRunner
{
    public static async Task RunAsync(string rawCommand, string gameRoot, string instanceDirectory,
        string versionId, JavaRuntime java, bool wait, Action<string>? log = null,
        CancellationToken ct = default)
    {
        var startInfo = BuildStartInfo(rawCommand, gameRoot, instanceDirectory, versionId, java, wait);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("启动前命令无法启动。");
        log?.Invoke("[启动前命令] " + startInfo.ArgumentList[^1]);
        if (!wait) return;

        string stdout, stderr;
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(ct)).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw;
        }

        if (!string.IsNullOrWhiteSpace(stdout)) log?.Invoke(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) log?.Invoke(stderr.TrimEnd());
        log?.Invoke($"[启动前命令] 退出码 {process.ExitCode}");
    }

    internal static ProcessStartInfo BuildStartInfo(string rawCommand, string gameRoot,
        string instanceDirectory, string versionId, JavaRuntime java, bool wait)
    {
        var command = rawCommand
            .Replace("{java}", java.Path, StringComparison.OrdinalIgnoreCase)
            .Replace("{minecraft}", gameRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{verpath}", instanceDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", versionId, StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
        {
            WorkingDirectory = gameRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = wait,
            RedirectStandardError = wait
        };
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }
}
