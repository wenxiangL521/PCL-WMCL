using System.Diagnostics;
using System.Text.RegularExpressions;
using PCL.Core;
using PCL.Core.Logging;

namespace PCL.Launch.Java;

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

    public void InvalidateCache() => _cache = null;

    public Task<JavaRuntime?> ProbePathAsync(string path, CancellationToken ct = default)
        => ProbeAsync(path, ct);

    public async Task<IReadOnlyList<JavaRuntime>> DiscoverAsync(
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        if (!forceRefresh && _cache is not null) return _cache;

        var candidates = EnumerateCandidates()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var tasks = candidates.Select(path => ProbeAsync(path, ct)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        _cache = results
            .Where(r => r is not null)
            .Cast<JavaRuntime>()
            .GroupBy(r => r.Home, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.MajorVersion).First())
            .OrderByDescending(r => r.MajorVersion)
            .ToList();

        return _cache;
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

    private static IEnumerable<string> EnumerateCandidates()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            yield return Path.Combine(javaHome, "bin", "java");

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(directory, "java");

        foreach (var root in new[]
                 {
                     "/usr/lib/jvm", "/usr/java", "/opt/java", "/opt"
                 })
        {
            foreach (var candidate in EnumerateUnder(root))
                yield return candidate;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[]
                 {
                     Path.Combine(home, ".jdks"),
                     Path.Combine(home, ".sdkman", "candidates", "java"),
                     Path.Combine(home, ".gradle", "jdks"),
                     PclPaths.JavaDirectory
                 })
        {
            foreach (var candidate in EnumerateUnder(root))
                yield return candidate;
        }
    }

    private static IEnumerable<string> EnumerateUnder(string root)
    {
        if (!Directory.Exists(root)) yield break;

        List<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            var java = Path.Combine(directory, "bin", "java");
            if (File.Exists(java))
            {
                yield return java;
                continue;
            }

            // 再下一层，兼容 /usr/lib/jvm/java-21-openjdk-amd64 这类结构
            List<string> nestedDirectories;
            try
            {
                nestedDirectories = Directory.EnumerateDirectories(directory).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var nested in nestedDirectories)
            {
                var nestedJava = Path.Combine(nested, "bin", "java");
                if (File.Exists(nestedJava)) yield return nestedJava;
            }
        }
    }

    private static async Task<JavaRuntime?> ProbeAsync(string path, CancellationToken ct)
    {
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
                return null;
            }

            var output = await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false);
            return Parse(path, output);
        }
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

    private static Version ParseVersion(string text)
    {
        var cleaned = text.Split('+', '-')[0].Trim();
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
