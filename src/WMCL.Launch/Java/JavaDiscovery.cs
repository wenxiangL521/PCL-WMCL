using System.Runtime.Versioning;
using Microsoft.Win32;
using WMCL.Core;

namespace WMCL.Launch.Java;

internal sealed record JavaSearchPaths(bool Windows, string Home, string RuntimeDirectory,
    string? JavaHome, string? JdkHome, string? SearchPath, IReadOnlyList<string> SystemRoots,
    IReadOnlyList<string> RegistryHomes);

internal static class JavaDiscovery
{
    internal static JavaSearchPaths SystemPaths()
    {
        var windows = OperatingSystem.IsWindows();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>();
        if (windows)
        {
            foreach (var programs in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetEnvironmentVariable("ProgramW6432"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
                     }.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "AdoptOpenJDK", "Amazon Corretto", "Zulu", "BellSoft", "Microsoft", "Semeru" })
                    roots.Add(Path.Combine(programs!, vendor));
                roots.Add(Path.Combine(programs!, "Minecraft Launcher", "runtime"));
                roots.Add(Path.Combine(programs!, "Minecraft", "runtime"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            roots.Add("/Library/Java/JavaVirtualMachines");
            roots.Add(Path.Combine(home, "Library", "Java", "JavaVirtualMachines"));
            roots.Add("/opt/homebrew/opt/openjdk");
            roots.Add("/usr/local/opt/openjdk");
            roots.Add("/opt/java");
        }
        else roots.AddRange(["/usr/lib/jvm", "/usr/java", "/opt/java", "/opt"]);
        return new(windows, home, WmclPaths.JavaDirectory,
            Environment.GetEnvironmentVariable("JAVA_HOME"), Environment.GetEnvironmentVariable("JDK_HOME"),
            Environment.GetEnvironmentVariable("PATH"), roots, windows ? ReadRegistryHomes() : []);
    }

    internal static IReadOnlyList<string> Enumerate(JavaSearchPaths paths, CancellationToken ct)
    {
        var executable = paths.Windows ? "java.exe" : "java";
        var found = new HashSet<string>(paths.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        void Add(string? directory, bool bin)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(directory)) return;
            try
            {
                directory = Path.GetFullPath(directory.Trim().Trim('"'));
                var candidate = bin ? Path.Combine(directory, "bin", executable) : Path.Combine(directory, executable);
                if (File.Exists(candidate)) found.Add(candidate);
                if (bin)
                {
                    var bundled = Path.Combine(directory, "Contents", "Home", "bin", executable);
                    if (File.Exists(bundled)) found.Add(bundled);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        }
        Add(paths.JavaHome, true);
        Add(paths.JdkHome, true);
        foreach (var directory in (paths.SearchPath ?? "").Split(paths.Windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries))
            Add(directory, false);
        foreach (var home in paths.RegistryHomes) Add(home, true);
        var roots = paths.SystemRoots.Concat(new[]
        {
            Path.Combine(paths.Home, ".jdks"), Path.Combine(paths.Home, ".sdkman", "candidates", "java"),
            Path.Combine(paths.Home, ".gradle", "jdks"), paths.RuntimeDirectory
        });
        foreach (var root in roots.Distinct(paths.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var queue = new Queue<(string Directory, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.TryDequeue(out var current))
            {
                ct.ThrowIfCancellationRequested();
                Add(current.Directory, true);
                if (current.Depth >= 3) continue;
                try
                {
                    foreach (var child in Directory.EnumerateDirectories(current.Directory))
                    {
                        ct.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            queue.Enqueue((child, current.Depth + 1));
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
            }
        }
        return found.ToList();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> ReadRegistryHomes()
    {
        var homes = new List<string>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var path in new[]
                         {
                             @"SOFTWARE\JavaSoft\Java Development Kit", @"SOFTWARE\JavaSoft\Java Runtime Environment",
                             @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\JRE",
                             @"SOFTWARE\Azul Systems\Zulu", @"SOFTWARE\BellSoft\Liberica"
                         })
                {
                    try
                    {
                        using var registry = RegistryKey.OpenBaseKey(hive, view);
                        using var parent = registry.OpenSubKey(path);
                        if (parent is null) continue;
                        foreach (var child in parent.GetSubKeyNames())
                        {
                            using var key = parent.OpenSubKey(child);
                            foreach (var value in new[] { "JavaHome", "InstallationPath" })
                                if (key?.GetValue(value) is string home && !string.IsNullOrWhiteSpace(home))
                                    homes.Add(Environment.ExpandEnvironmentVariables(home));
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
            }
        }
        return homes;
    }
}
