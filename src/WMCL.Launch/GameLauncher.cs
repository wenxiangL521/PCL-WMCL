using System.Diagnostics;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.Launch;

public sealed class GameLauncher
{
    public async Task<int> LaunchAsync(
        LaunchRequest request,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        var minecraftDirectory = WmclPaths.Expand(
            string.IsNullOrWhiteSpace(request.MinecraftDirectory)
                ? request.GameDirectory
                : request.MinecraftDirectory);
        // 解压 natives 是重 IO（首次启动要解几十个 zip），放线程池避免卡 UI 线程。
        await Task.Run(() => VersionInstaller.EnsureNatives(
            request.Version, minecraftDirectory, request.VersionId,
            message => log?.Report("[WMCL] " + message)), ct).ConfigureAwait(false);

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
        if (OperatingSystem.IsLinux() &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER")) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            // Minecraft 26.x 的 SDL 后端在 niri/XWayland 下会退回 XWayland，
            // 随后 SDL_GL_LoadLibrary 失败。显式使用 Wayland，与 HMCL 的行为一致。
            startInfo.Environment["SDL_VIDEO_DRIVER"] = "wayland";
        }
        if (request.HighPerformanceGpu)
        {
            // 用户选择了具体 GPU 时，DRI_PRIME 取该索引（0 通常是集显/主 GPU，
            // 此时不应再叠加强制 NVIDIA offload 变量，否则与选择矛盾）。
            var gpuIndex = request.GpuIndex.Trim();
            if (gpuIndex.Length > 0 && gpuIndex != "0")
            {
                startInfo.Environment["DRI_PRIME"] = gpuIndex;
                startInfo.Environment["__NV_PRIME_RENDER_OFFLOAD"] = "1";
                startInfo.Environment["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia";
            }
            else if (gpuIndex.Length == 0)
            {
                // 未指定索引：默认使用第一块独立 GPU。
                startInfo.Environment["DRI_PRIME"] = "1";
                startInfo.Environment["__NV_PRIME_RENDER_OFFLOAD"] = "1";
                startInfo.Environment["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia";
            }
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
