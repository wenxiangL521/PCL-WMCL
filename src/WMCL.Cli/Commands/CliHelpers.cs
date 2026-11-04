using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft;

namespace WMCL.Cli.Commands;

/// <summary>CLI 通用参数解析与输出辅助（从 Program.cs 抽出）。</summary>
public static class CliHelpers
{
public static string GetGameDirectory(IReadOnlyDictionary<string, string> options)
{
    var configured = options.GetValueOrDefault("dir");
    return WmclPaths.Expand(string.IsNullOrWhiteSpace(configured)
        ? WmclPaths.DefaultGameDirectory
        : configured);
}

public static string GetIsolationMode(IReadOnlyDictionary<string, string> options) =>
    IsolationPolicy.Normalize(options.GetValueOrDefault("isolation"));

public static MirrorMode ParseMirror(string? value) => value?.ToLowerInvariant() switch
{
    "official" => MirrorMode.Official,
    "bmclapi" => MirrorMode.Bmclapi,
    _ => MirrorMode.Auto
};

public static int GetInt(IReadOnlyDictionary<string, string> options, string key, int fallback)
    => options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
        ? parsed
        : fallback;

public static void PrintProgress(JobProgress progress)
{
    var percent = progress.Percent.HasValue
        ? $"{progress.Percent.Value * 100,5:0.0}%"
        : "     ";
    var speed = progress.BytesPerSecond is > 0
        ? $"{progress.BytesPerSecond.Value / 1048576.0,6:0.0} MB/s"
        : "             ";
    var eta = progress.Eta.HasValue
        ? $" ETA {progress.Eta.Value:mm\\:ss}"
        : "";
    var detail = progress.Detail is null
        ? ""
        : progress.Detail.Length > 60 ? progress.Detail[..60] : progress.Detail;

    var text = $"{progress.Stage,-12} {percent} {speed}{eta}  {detail}";
    Console.Write('\r');
    Console.Write(text.PadRight(118));
}

public static string FormatBytes(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB"];
    double value = bytes;
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1)
    {
        value /= 1024;
        unit++;
    }
    return $"{value:0.0} {units[unit]}";
}
}
