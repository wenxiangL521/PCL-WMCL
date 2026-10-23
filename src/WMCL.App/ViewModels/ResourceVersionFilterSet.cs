using System.Globalization;
using System.Text.RegularExpressions;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageDownloadCompDetail 的版本筛选：少量版本显示原值，超过 8 项按 Drop 分组。</summary>
public sealed class ResourceVersionFilterSet
{
    private static readonly Regex StandardVersion = new(
        @"^(1|[2-9]\d)\.\d+(?:\.\d+)?$", RegexOptions.CultureInvariant);

    public bool Grouped { get; }
    public IReadOnlyList<string> Labels { get; }

    private ResourceVersionFilterSet(bool grouped, IReadOnlyList<string> labels)
    {
        Grouped = grouped;
        Labels = labels;
    }

    public static ResourceVersionFilterSet Create(IEnumerable<string> gameVersions)
    {
        var distinct = gameVersions.Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0) return new(false, ["全部"]);

        var raw = distinct.OrderByDescending(VersionCode).ThenBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count < 9) return new(false, ["全部", .. raw]);

        var grouped = distinct.Select(GroupName).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(VersionCode).ThenBy(label => label, StringComparer.OrdinalIgnoreCase).ToList();
        return new(true, ["全部", .. grouped]);
    }

    public bool Matches(string filter, IEnumerable<string> gameVersions)
    {
        if (filter.Length == 0 || filter == "全部") return true;
        return gameVersions.Any(version => string.Equals(
            Grouped ? GroupName(version) : version, filter, StringComparison.OrdinalIgnoreCase));
    }

    public string GroupFor(string version) => Grouped ? GroupName(version) : version;

    public static string CardName(string name)
    {
        if (name.Contains('w', StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pre", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("snapshot", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("experimental", StringComparison.OrdinalIgnoreCase))
            return "快照版";
        return StandardVersion.IsMatch(name) ? name : "远古版";
    }

    public static int SortCode(string name) => VersionToDrop(name);

    public static string GroupName(string name)
    {
        if (name.Contains('w', StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pre", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("snapshot", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("experimental", StringComparison.OrdinalIgnoreCase))
            return "快照版";
        if (!StandardVersion.IsMatch(name)) return "远古版";
        var drop = VersionToDrop(name);
        return drop >= 250
            ? $"{drop / 10}.{drop % 10}"
            : $"1.{drop / 10}";
    }

    private static long VersionCode(string version)
    {
        if (version.Contains('w', StringComparison.OrdinalIgnoreCase)) return long.MaxValue - 1;
        var drop = VersionToDrop(version);
        return drop == 209 ? 0 : drop;
    }

    private static int VersionToDrop(string name)
    {
        var match = Regex.Match(name, @"^(1|[2-9]\d)\.(\d+)", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor))
            return 209;
        return major == 1 ? minor * 10 : major * 10 + minor;
    }
}
