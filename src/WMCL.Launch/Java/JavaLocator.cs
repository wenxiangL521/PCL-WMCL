using System.Diagnostics;
using System.Text.RegularExpressions;
using WMCL.Core;
using WMCL.Core.Logging;

namespace WMCL.Launch.Java;

public sealed record JavaRuntime(
    string Path,
    Version Version,
    int MajorVersion,
    string Architecture,
    string Home,
    string? Vendor)
{
    public bool Is64Bit => Architecture is "x86_64" or "amd64" or "aarch64" or "arm64";

    public string Display => $"Java {MajorVersion} ({Architecture}) {Home}";
}

/// <summary>
/// Java 发现：JAVA_HOME、PATH、常见发行版目录；通过 -XshowSettings 读取真实版本与架构。
/// </summary>
public sealed class JavaLocator
{
    private static readonly Regex PropertyLine = new(
        @"^\s*(?<key>[A-Za-z0-9_.]+)\s*=\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled);

    private IReadOnlyList<JavaRuntime>? _cache;
    private readonly object _cacheSync = new();
    private int _cacheGeneration;
    private readonly Func<CancellationToken, IReadOnlyList<string>> _candidates;
    private readonly Func<string, CancellationToken, Task<JavaRuntime?>> _probe;

    public JavaLocator() : this(ct => JavaDiscovery.Enumerate(JavaDiscovery.SystemPaths(), ct), ProbeAsync) { }

    internal JavaLocator(Func<CancellationToken, IReadOnlyList<string>> candidates,
        Func<string, CancellationToken, Task<JavaRuntime?>> probe)
    {
        _candidates = candidates;
        _probe = probe;
    }

    public void InvalidateCache()
    {
        lock (_cacheSync)
        {
            _cacheGeneration++;
            _cache = null;
        }
    }

    public Task<JavaRuntime?> ProbePathAsync(string path, CancellationToken ct = default)
        => _probe(path, ct);

    public async Task<IReadOnlyList<JavaRuntime>> DiscoverAsync(
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        int generation;
        lock (_cacheSync)
        {
            if (!forceRefresh && _cache is not null) return _cache;
            generation = _cacheGeneration;
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = _candidates(ct)
            .Distinct(comparer)
            .ToList();

        using var gate = new SemaphoreSlim(4);
        var tasks = candidates.Select(async path =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try { return await _probe(path, ct).ConfigureAwait(false); }
            finally { gate.Release(); }
        }).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var discovered = results
            .Where(r => r is not null)
            .Cast<JavaRuntime>()
            .GroupBy(r => r.Home, comparer)
            .Select(g => g.OrderByDescending(r => r.MajorVersion).First())
            .OrderByDescending(r => r.MajorVersion)
            .ToList();

        lock (_cacheSync)
            if (generation == _cacheGeneration) _cache = discovered;
        return discovered;
    }

    public async Task<JavaRuntime> SelectForAsync(
        int requiredMajor,
        CancellationToken ct = default)
    {
        var runtimes = await DiscoverAsync(ct: ct).ConfigureAwait(false);

        var exact = runtimes
            .Where(r => r.MajorVersion == requiredMajor)
            .OrderByDescending(r => r.Is64Bit)
            .FirstOrDefault();
        if (exact is not null) return exact;

        var higher = runtimes
            .Where(r => r.MajorVersion > requiredMajor)
            .OrderBy(r => r.MajorVersion)
            .ThenByDescending(r => r.Is64Bit)
            .FirstOrDefault();
        if (higher is not null)
        {
            Log.Warn($"未找到 Java {requiredMajor}，改用 Java {higher.MajorVersion}：{higher.Path}");
            return higher;
        }

        var available = runtimes.Count == 0
            ? "未发现任何 Java"
            : string.Join("，", runtimes.Select(r => $"Java {r.MajorVersion}"));
        throw new InvalidOperationException(
            $"需要 Java {requiredMajor} 或更高版本，但只找到：{available}。请安装后重试，或使用 --java 指定路径。");
    }

    public static int InferMajorVersion(string minecraftVersionId)
    {
        var id = minecraftVersionId.Trim();

        if (id.StartsWith("1.", StringComparison.Ordinal))
        {
            var parts = id.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            {
                if (minor <= 16) return 8;
                if (minor == 17) return 17;

                // 1.20.5 起要求 Java 21
                if (minor == 20)
                {
                    if (parts.Length >= 3 && int.TryParse(parts[2], out var patch) && patch >= 5)
                        return 21;
                    return 17;
                }

                return minor < 20 ? 17 : 21;
            }
        }

        // 2026 之后的年份版本（如 26.1）与快照，先按 Java 21 处理。
        return 21;
    }

    private static async Task<JavaRuntime?> ProbeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-XshowSettings:properties");
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null) return null;

            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }
                ct.ThrowIfCancellationRequested();
                return null;
            }

            var output = await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false);
            return Parse(path, output);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Debug($"探测 Java 失败（{path}）：{ex.Message}");
            return null;
        }
    }

    private static JavaRuntime? Parse(string path, string output)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var match = PropertyLine.Match(line);
            if (match.Success)
                properties[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        if (!properties.TryGetValue("java.version", out var versionText))
            return null;

        var version = ParseVersion(versionText);
        var major = version.Major;

        var home = properties.GetValueOrDefault("java.home")
                   ?? Path.GetDirectoryName(Path.GetDirectoryName(path))
                   ?? path;

        var architecture = NormalizeArchitecture(
            properties.GetValueOrDefault("os.arch") ?? "");

        return new JavaRuntime(
            path,
            version,
            major,
            architecture,
            home,
            properties.GetValueOrDefault("java.vendor"));
    }

    internal static Version ParseVersion(string text)
    {
        var cleaned = text.Split('+', '-')[0].Trim();
        var update = Regex.Match(cleaned, @"^(\d+)u(\d+)");
        if (update.Success)
            return new Version(int.Parse(update.Groups[1].Value), 0, int.Parse(update.Groups[2].Value));
        if (cleaned.StartsWith("1.", StringComparison.Ordinal))
        {
            var rest = cleaned[2..];
            var parts = rest.Split('.', '_');
            var numbers = new List<int>();
            foreach (var part in parts)
                if (int.TryParse(part, out var n)) numbers.Add(n);
            while (numbers.Count < 3) numbers.Add(0);
            return new Version(numbers[0], numbers[1], numbers[2]);
        }

        var segments = cleaned.Split('.', '_');
        var values = new List<int>();
        foreach (var segment in segments)
            if (int.TryParse(segment, out var n)) values.Add(n);
        while (values.Count < 3) values.Add(0);
        return new Version(values[0], values[1], values[2]);
    }

    private static string NormalizeArchitecture(string arch) => arch.ToLowerInvariant() switch
    {
        "amd64" or "x86_64" => "x86_64",
        "aarch64" or "arm64" => "arm64",
        "x86" or "i386" or "i686" => "x86",
        var other => other
    };
}
