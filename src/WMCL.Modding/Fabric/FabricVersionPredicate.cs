using System.Globalization;
using System.Text.Json;

namespace WMCL.Modding.Fabric;

public static class FabricVersionPredicate
{
    public static bool Matches(string version, JsonElement requirement, bool quilt = false) => requirement.ValueKind switch
    {
        JsonValueKind.String => Matches(version, requirement.GetString()!, quilt),
        JsonValueKind.Array => requirement.EnumerateArray().Select(item => Matches(version, item, quilt)).ToArray().Any(match => match),
        JsonValueKind.Object when quilt => MatchesQuiltObject(version, requirement),
        _ => throw new InvalidDataException("无法识别 Loader 版本约束：" + requirement.GetRawText())
    };

    private static bool MatchesQuiltObject(string version, JsonElement requirement)
    {
        var properties = requirement.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name is not ("any" or "all") || properties[0].Value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("无法识别 Quilt Loader 版本约束：" + requirement.GetRawText());
        var matches = properties[0].Value.EnumerateArray().Select(item => Matches(version, item, true)).ToArray();
        if (matches.Length == 0) throw new InvalidDataException("Quilt Loader 版本约束不能为空");
        return properties[0].Name == "any" ? matches.Any(match => match) : matches.All(match => match);
    }

    public static bool Matches(string version, string requirement, bool quilt = false)
    {
        var terms = requirement.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (quilt && terms.Length != 1) throw new InvalidDataException("Quilt Loader 的组合约束需要 any/all：" + requirement);
        return terms.Select(term => MatchesTerm(version, term, quilt)).ToArray().All(match => match);
    }

    private static bool MatchesTerm(string version, string term, bool quilt)
    {
        if (term is "*" or "x" or "X") return true;
        var operation = new[] { ">=", "<=", ">", "<", "=", "~", "^" }
            .FirstOrDefault(term.StartsWith) ?? (quilt ? "^" : "=");
        var reference = term.StartsWith(operation, StringComparison.Ordinal) ? term[operation.Length..] : term;
        if (string.IsNullOrWhiteSpace(reference) || reference.Any(char.IsWhiteSpace) || reference.Contains('|'))
            throw new InvalidDataException("无法识别 Loader 版本约束：" + term);

        var referenceCore = reference.Split('+')[0];
        var wildcardParts = referenceCore.Split('.');
        var wildcardIndex = Array.FindIndex(wildcardParts, part => part is "*" or "x" or "X");
        if (wildcardIndex >= 0)
        {
            if ((!quilt && operation != "=") || (quilt && term.StartsWith(operation, StringComparison.Ordinal)) ||
                wildcardParts.Skip(wildcardIndex).Any(part => part is not ("*" or "x" or "X")))
                throw new InvalidDataException("无效的 Loader 通配版本约束：" + term);
            if (wildcardIndex == 0) return true;
            var prefix = Parse(string.Join('.', wildcardParts.Take(wildcardIndex)));
            var actual = Parse(version);
            if (quilt && prefix is not null && actual is not null)
            {
                var upper = (int[])prefix.Components.Clone();
                upper[^1] = checked(upper[^1] + 1);
                return actual.CompareTo(prefix) >= 0 && actual.CompareTo(new SemanticVersion(upper, null)) < 0;
            }
            return actual is not null && prefix is not null &&
                Enumerable.Range(0, wildcardIndex).All(index => actual.Component(index) == prefix.Component(index));
        }

        var actualVersion = Parse(version);
        var referenceVersion = Parse(reference);
        if (actualVersion is null || referenceVersion is null)
        {
            if (operation is ">" or "<")
                throw new InvalidDataException("排除边界的 Loader 版本约束必须使用语义版本：" + term);
            return string.Equals(version, reference, StringComparison.Ordinal);
        }
        var comparison = actualVersion.CompareTo(referenceVersion);
        if (quilt && operation is "~" or "^")
        {
            var upper = operation == "^" ? new[] { checked(referenceVersion.Component(0) + 1) }
                : new[] { referenceVersion.Component(0), checked(referenceVersion.Component(1) + 1) };
            return comparison >= 0 && actualVersion.CompareTo(new SemanticVersion(upper, null)) < 0;
        }
        return operation switch
        {
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            ">" => comparison > 0,
            "<" => comparison < 0,
            "~" => comparison >= 0 && actualVersion.Component(0) == referenceVersion.Component(0) &&
                   actualVersion.Component(1) == referenceVersion.Component(1),
            "^" => comparison >= 0 && actualVersion.Component(0) == referenceVersion.Component(0),
            _ => comparison == 0
        };
    }

    private static SemanticVersion? Parse(string value)
    {
        var withoutBuild = value.Split('+')[0];
        var prereleaseIndex = withoutBuild.IndexOf('-');
        var core = prereleaseIndex < 0 ? withoutBuild : withoutBuild[..prereleaseIndex];
        var prerelease = prereleaseIndex < 0 ? null : withoutBuild[(prereleaseIndex + 1)..];
        var parts = core.Split('.');
        var components = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out components[index]))
                return null;
        }
        return new SemanticVersion(components, prerelease);
    }

    private sealed record SemanticVersion(int[] Components, string? Prerelease)
    {
        public int Component(int index) => index < Components.Length ? Components[index] : 0;

        public int CompareTo(SemanticVersion other)
        {
            for (var index = 0; index < Math.Max(Components.Length, other.Components.Length); index++)
            {
                var comparison = Component(index).CompareTo(other.Component(index));
                if (comparison != 0) return comparison;
            }
            if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
            if (other.Prerelease is null) return -1;
            if (Prerelease.Length == 0) return other.Prerelease.Length == 0 ? 0 : -1;
            if (other.Prerelease.Length == 0) return 1;
            var actualParts = Prerelease.Split('.');
            var otherParts = other.Prerelease.Split('.');
            for (var index = 0; index < Math.Min(actualParts.Length, otherParts.Length); index++)
            {
                var actual = actualParts[index];
                var reference = otherParts[index];
                var actualNumeric = actual.Length > 0 && actual.All(char.IsAsciiDigit);
                var referenceNumeric = reference.Length > 0 && reference.All(char.IsAsciiDigit);
                var comparison = actualNumeric && referenceNumeric
                    ? CompareNumericIdentifiers(actual, reference)
                    : actualNumeric != referenceNumeric ? actualNumeric ? -1 : 1
                    : string.CompareOrdinal(actual, reference);
                if (comparison != 0) return comparison;
            }
            return actualParts.Length.CompareTo(otherParts.Length);
        }

        private static int CompareNumericIdentifiers(string actual, string reference)
        {
            actual = actual.TrimStart('0');
            reference = reference.TrimStart('0');
            return actual.Length != reference.Length ? actual.Length.CompareTo(reference.Length)
                : string.CompareOrdinal(actual, reference);
        }
    }
}
