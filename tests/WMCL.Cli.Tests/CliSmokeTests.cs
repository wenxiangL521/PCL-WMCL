using System.Diagnostics;

namespace WMCL.Cli.Tests;

/// <summary>最小 smoke test：直接运行 CLI 可执行文件，验证参数解析与退出码。</summary>
[Trait("Kind", "Unit")]
public sealed class CliSmokeTests
{
    private static string CliDllPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WMCL.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}")
            ? "Debug"
            : "Release";
        var path = Path.Combine(directory!.FullName, "src", "WMCL.Cli", "bin", configuration, "net10.0", "WMCL.Cli.dll");
        Assert.True(File.Exists(path), "找不到 CLI 产物：" + path);
        return path;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(CliDllPath());
        foreach (var argument in args) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, error);
    }

    [Fact]
    public async Task HelpPrintsUsageAndExitsZero()
    {
        var (code, output, _) = await RunAsync("help");
        Assert.Equal(0, code);
        Assert.Contains("install", output);
    }

    [Fact]
    public async Task NoArgumentsPrintsUsageAndExitsNonZero()
    {
        var (code, output, _) = await RunAsync();
        Assert.Equal(1, code);
        Assert.Contains("install", output);
    }

    [Fact]
    public async Task UnknownCommandReportsError()
    {
        var (code, _, error) = await RunAsync("definitely-not-a-command");
        Assert.Equal(1, code);
        Assert.Contains("未知命令", error);
    }
}
