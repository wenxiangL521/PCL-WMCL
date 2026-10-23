using System.Globalization;
using System.Text.RegularExpressions;
using WMCL.Core.Logging;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Launch.Java;

public sealed record JavaRequirement(Version Minimum, Version? Maximum = null, bool MaximumInclusive = false,
    IReadOnlyList<int>? SupportedMajors = null)
{
    public bool Contains(Version version) => version >= Minimum &&
        (Maximum is null || (MaximumInclusive ? version <= Maximum : version < Maximum)) &&
        (SupportedMajors is not { Count: > 0 } || SupportedMajors.Contains(version.Major));

    /// <summary>指定 Java 可以继续尝试，但不满足范围时给出明确警告，而不是静默启动。</summary>
    public string? WarningFor(JavaRuntime runtime) => Contains(runtime.Version)
        ? null
        : $"Java {runtime.MajorVersion}（{runtime.Version}）不满足版本要求（{this}），可能无法启动。";

    public JavaRuntime? Select(IEnumerable<JavaRuntime> runtimes) => runtimes
        .Where(runtime => Contains(runtime.Version))
        .OrderBy(runtime => runtime.MajorVersion)
        .ThenByDescending(runtime => runtime.Is64Bit)
        .ThenByDescending(runtime => runtime.Version)
        .FirstOrDefault();

    public override string ToString() => $">= {Minimum}" +
        (Maximum is null ? "" : $"，{(MaximumInclusive ? "<=" : "<")} {Maximum}") +
        (SupportedMajors is not { Count: > 0 } ? "" : "，整合包支持 " + string.Join("、", SupportedMajors));

    public static JavaRequirement ForVersion(VersionJson version, InstallManifest? manifest = null, Action<string>? warn = null)
    {
        var minecraft = manifest?.Components.GetValueOrDefault("minecraft") ??
                        MinecraftVersionUtil.GetBaseGameVersion(version.Id, version);
        var valid = Version.TryParse(minecraft, out var game) && minecraft.StartsWith("1.", StringComparison.Ordinal);
        var released = DateTimeOffset.TryParse(version.ReleaseTime, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var date) ? date : (DateTimeOffset?)null;
        var minimum = new Version(1, 0, 0);
        Version? maximum = null;
        var inclusive = false;
        void Add(Version? lower = null, Version? upper = null, bool include = false)
        {
            var constraint = new JavaRequirement(lower ?? new Version(1, 0, 0), upper, include);
            var nextMinimum = constraint.Minimum > minimum ? constraint.Minimum : minimum;
            var nextMaximum = maximum;
            var nextInclusive = inclusive;
            if (upper is not null && (maximum is null || upper <= maximum))
            {
                nextMaximum = upper;
                nextInclusive = upper == maximum ? include && inclusive : include;
            }
            if (nextMaximum is not null && (nextMinimum > nextMaximum || (nextMinimum == nextMaximum && !nextInclusive)))
            {
                var message = $"Java 版本要求冲突，若启动失败请在版本设置手动指定 Java：当前追加的要求为 {constraint}，游戏版本为 {version.Id}";
                if (warn is not null) warn(message);
                else Log.Warn(message);
                minimum = constraint.Minimum;
                maximum = upper;
                inclusive = include;
                return;
            }
            minimum = nextMinimum;
            maximum = nextMaximum;
            inclusive = nextInclusive;
        }
        void AtLeast(int major) => Add(lower: new Version(major, 0, 0));
        void AtMost(int major, int update = 0, bool include = false) => Add(upper: new Version(major, 0, update), include: include);
        if ((valid && game! >= new Version(1, 20, 5)) || (!valid && released >= new DateTimeOffset(2024, 4, 2, 0, 0, 0, TimeSpan.Zero))) AtLeast(21);
        else if ((valid && game!.Minor >= 18) || (!valid && released >= new DateTimeOffset(2021, 11, 16, 0, 0, 0, TimeSpan.Zero))) AtLeast(17);
        else if ((valid && game!.Minor >= 17) || (!valid && released >= new DateTimeOffset(2021, 5, 11, 0, 0, 0, TimeSpan.Zero))) AtLeast(16);
        else if (released?.Year >= 2017 || (valid && game!.Minor >= 12)) AtLeast(8);
        else if (released is { Year: >= 2001 } && released <= new DateTimeOffset(2013, 5, 1, 0, 0, 0, TimeSpan.Zero)) AtMost(9);
        if (version.JavaVersion?.MajorVersion is >= 22) AtLeast(version.JavaVersion.MajorVersion);
        if (!valid && released is null) AtLeast(version.JavaVersion?.MajorVersion ?? JavaLocator.InferMajorVersion(minecraft));

        string? Loader(string key, params string[] coordinates) => version.Libraries
            .Select(library => library.Name.Split(':'))
            .Where(parts => parts.Length >= 3 && coordinates.Contains(parts[0] + ":" + parts[1], StringComparer.OrdinalIgnoreCase))
            .Select(parts => parts[2]).FirstOrDefault() ?? manifest?.Components.GetValueOrDefault(key);
        var forge = Loader("forge", "net.minecraftforge:forge", "net.minecraftforge:minecraftforge");
        var neo = Loader("neoforge", "net.neoforged:neoforge", "net.neoforged:forge");
        var fabric = Loader("fabric", "net.fabricmc:fabric-loader");
        var optifine = Loader("optifine", "optifine:OptiFine") is not null;
        var lite = Loader("liteloader", "com.mumfrey:liteloader") is not null;
        if (forge?.StartsWith(minecraft + "-", StringComparison.Ordinal) == true) forge = forge[(minecraft.Length + 1)..];
        if (optifine && valid)
        {
            if (game!.Minor < 7 || game.Minor == 12) AtMost(9);
            else if (game.Minor is >= 8 and < 12) Add(new Version(8, 0, 0), new Version(9, 0, 0));
        }
        if (forge is not null)
        {
            if (valid && game! >= new Version(1, 6, 1) && game <= new Version(1, 7, 2)) Add(new Version(7, 0, 0), new Version(8, 0, 0));
            else if (!valid || game!.Minor <= 12) AtMost(9);
            else if (game.Minor <= 14) Add(new Version(8, 0, 0), new Version(11, 0, 0));
            else if (game.Minor == 15) Add(new Version(8, 0, 0), new Version(16, 0, 0));
            else if (Between(forge, "34.0.0", "36.2.25")) AtMost(8, 320, true);
            else if (Between(forge, "36.2.26", "36.999999.999999")) AtMost(24);
            else if (Between(forge, "37.0.0", "37.0.79")) AtMost(17);
            else if (game.Minor == 18 && optifine) AtMost(19);
            else if (Between(forge, "45.0.21", "45.0.65")) AtMost(20);
            else if (Between(forge, "45.0.66", "47.4.8")) AtMost(22);
        }
        if (neo is not null && (game == new Version(1, 20, 1) ||
            (!neo.Contains("25w14craftmine", StringComparison.OrdinalIgnoreCase) && CompareLoader(neo, "20.2.62-beta") <= 0))) AtMost(22);
        if (fabric is not null && valid)
        {
            if (game!.Minor is >= 15 and <= 16) AtLeast(8);
            else if (game.Minor >= 18) AtLeast(17);
            if (fabric != "未知版本" && CompareLoader(fabric, "0.17.0") < 0) AtMost(25);
        }
        if (lite && valid) AtMost(9);
        return new(minimum, maximum, inclusive, manifest?.McbbsSupportJava.ToArray());
    }

    /// <summary>
    /// 安装阶段尚无合并后的版本 JSON：用 Minecraft + 目标加载器构造最小约束，
    /// 供 Forge/NeoForge/Fabric/OptiFine/LiteLoader 安装器与合并安装选择 Java。
    /// </summary>
    public static JavaRequirement ForInstall(string minecraft, string? loader = null,
        string? loaderVersion = null, bool liteLoader = false, int? declaredJavaMajor = null)
    {
        var version = new VersionJson
        {
            Id = minecraft,
            BaseGameVersion = minecraft,
            JavaVersion = declaredJavaMajor is null
                ? null
                : new JavaVersionInfo { MajorVersion = declaredJavaMajor.Value }
        };
        var name = loader?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(loaderVersion) && name.Length > 0)
        {
            var coordinate = name switch
            {
                "Forge" => $"net.minecraftforge:forge:{minecraft}-{loaderVersion}",
                "NeoForge" => $"net.neoforged:neoforge:{loaderVersion}",
                "Fabric" or "Quilt" or "LegacyFabric" => $"net.fabricmc:fabric-loader:{loaderVersion}",
                "OptiFine" => $"optifine:OptiFine:{loaderVersion}",
                "LiteLoader" => $"com.mumfrey:liteloader:{loaderVersion}",
                _ => null
            };
            if (coordinate is not null) version.Libraries.Add(new Library { Name = coordinate });
        }
        if (liteLoader && name != "LiteLoader")
            version.Libraries.Add(new Library { Name = "com.mumfrey:liteloader:unknown" });
        return ForVersion(version);
    }

    private static bool Between(string value, string lower, string upper) =>
        CompareLoader(value, lower) >= 0 && CompareLoader(value, upper) <= 0;

    private static int CompareLoader(string left, string right)
    {
        var leftParts = Regex.Matches(left.ToLowerInvariant(), "[a-z]+|[0-9]+");
        var rightParts = Regex.Matches(right.ToLowerInvariant(), "[a-z]+|[0-9]+");
        for (var index = 0; index < Math.Max(leftParts.Count, rightParts.Count); index++)
        {
            var leftPart = index < leftParts.Count ? leftParts[index].Value : "0";
            var rightPart = index < rightParts.Count ? rightParts[index].Value : "0";
            if (leftPart == rightPart) continue;
            var leftNumber = int.TryParse(leftPart, out var parsedLeft) ? parsedLeft : leftPart == "pre" ? -2 : leftPart == "rc" ? -1 : 0;
            var rightNumber = int.TryParse(rightPart, out var parsedRight) ? parsedRight : rightPart == "pre" ? -2 : rightPart == "rc" ? -1 : 0;
            return leftNumber != rightNumber ? leftNumber.CompareTo(rightNumber) : string.Compare(leftPart, rightPart, StringComparison.Ordinal);
        }
        return 0;
    }
}
