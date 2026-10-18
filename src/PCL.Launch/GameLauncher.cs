using System.Diagnostics;

namespace PCL.Launch;

public sealed class GameLauncher
{
    public async Task<int> LaunchAsync(
        LaunchRequest request,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        var plan = LaunchPlanBuilder.Build(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.JavaPath,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in plan.Arguments)
            startInfo.ArgumentList.Add(argument);
        if (request.HighPerformanceGpu)
        {
            startInfo.Environment["DRI_PRIME"] = "1";
            startInfo.Environment["__NV_PRIME_RENDER_OFFLOAD"] = "1";
            startInfo.Environment["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia";
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) log?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) log?.Report(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException("无法启动 Java 进程。");

        if (!string.Equals(request.ProcessPriority, "normal", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Enum.TryParse<ProcessPriorityClass>(
                        request.ProcessPriority, ignoreCase: true, out var priority))
                    process.PriorityClass = priority;
            }
            catch (Exception ex)
            {
                log?.Report($"[警告] 设置进程优先级失败：{ex.Message}");
            }
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return process.ExitCode;
    }
}
