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
