using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

public sealed record RuleContext(
    string OsName,
    string OsArch,
    string OsVersion,
    IReadOnlyDictionary<string, bool> Features)
{
    public static RuleContext CreateDefault(IReadOnlyDictionary<string, bool>? features = null) =>
        new(GetOsName(), GetOsArch(), GetOsVersion(),
            features ?? new Dictionary<string, bool>());

    public static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "linux";
    }

    public static string GetOsArch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.X64 => "x86_64",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm32",
        _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
    };

    public static string GetOsVersion()
    {
        try
        {
            if (OperatingSystem.IsLinux() && File.Exists("/proc/sys/kernel/osrelease"))
                return File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
        }
        catch
        {
            // ignore
        }
        return Environment.OSVersion.Version.ToString();
    }
}

public static class RuleEvaluator
{
    public static bool IsAllowed(IReadOnlyList<Rule>? rules, RuleContext context)
    {
        if (rules is null || rules.Count == 0) return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!Matches(rule, context)) continue;
            allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    public static bool Matches(Rule rule, RuleContext context)
    {
        if (rule.Os is not null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name) &&
                !string.Equals(rule.Os.Name, context.OsName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(rule.Os.Arch) &&
                !string.Equals(rule.Os.Arch, context.OsArch, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(rule.Os.Version))
            {
                try
                {
                    if (!Regex.IsMatch(context.OsVersion, rule.Os.Version,
                            RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                        return false;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        if (rule.Features is not null)
        {
            foreach (var (key, expected) in rule.Features)
            {
                if (!context.Features.TryGetValue(key, out var actual) || actual != expected)
                    return false;
            }
        }

        return true;
    }
}
