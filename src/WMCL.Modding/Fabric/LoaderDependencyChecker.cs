using System.IO.Compression;
using System.Text.Json;

namespace WMCL.Modding.Fabric;

public static class LoaderDependencyChecker
{
    public static async Task<string?> ReadModIdAsync(string jarPath, string kind, CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var quilt = kind == "Quilt" && archive.GetEntry("quilt.mod.json") is not null;
        var metadata = archive.GetEntry(quilt ? "quilt.mod.json" : "fabric.mod.json");
        if (metadata is null) return null;
        await using var stream = metadata.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = quilt ? document.RootElement.GetProperty("quilt_loader") : document.RootElement;
        return root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
    }

    public static async Task ValidateAsync(string jarPath, string kind, string loaderVersion, CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        await ValidateArchiveAsync(archive, Path.GetFileName(jarPath), kind, loaderVersion, 0, true, ct).ConfigureAwait(false);
    }

    private static async Task ValidateArchiveAsync(
        ZipArchive archive, string source, string kind, string loaderVersion,
        int depth, bool requireMetadata, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 8) throw new InvalidDataException("API 内嵌 jar 层数超过限制：" + source);
        var quilt = kind == "Quilt" && archive.GetEntry("quilt.mod.json") is not null;
        var metadata = archive.GetEntry(quilt ? "quilt.mod.json" : "fabric.mod.json");
        if (metadata is null)
        {
            if (requireMetadata) throw new InvalidDataException("API jar 缺少 Mod 元数据：" + source);
            return;
        }
        await using var metadataStream = metadata.Open();
        using var document = await JsonDocument.ParseAsync(metadataStream, cancellationToken: ct).ConfigureAwait(false);
        var root = quilt ? document.RootElement.GetProperty("quilt_loader") : document.RootElement;
        var loaderId = kind == "Quilt" ? "quilt_loader" : "fabricloader";
        if (quilt)
        {
            CheckQuiltDependencies(root, "depends", loaderId, loaderVersion, source, false);
            CheckQuiltDependencies(root, "breaks", loaderId, loaderVersion, source, true);
        }
        else
        {
            CheckFabricDependencies(root, "depends", loaderId, loaderVersion, source, false);
            CheckFabricDependencies(root, "breaks", loaderId, loaderVersion, source, true);
        }
        if (!root.TryGetProperty("jars", out var jars)) return;
        foreach (var jar in jars.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var path = jar.ValueKind == JsonValueKind.String ? jar.GetString() : jar.GetProperty("file").GetString();
            var entry = archive.GetEntry(path ?? "")
                ?? throw new InvalidDataException("API 缺少声明的内嵌 jar：" + path);
            if (entry.Length > 64 * 1024 * 1024)
                throw new InvalidDataException("API 内嵌 jar 过大：" + path);
            await using var nestedStream = new MemoryStream();
            await using (var entryStream = entry.Open())
                await entryStream.CopyToAsync(nestedStream, ct).ConfigureAwait(false);
            nestedStream.Position = 0;
            using var nested = new ZipArchive(nestedStream, ZipArchiveMode.Read, leaveOpen: true);
            await ValidateArchiveAsync(nested, source + " → " + path, kind, loaderVersion, depth + 1, false, ct)
                .ConfigureAwait(false);
        }
    }

    private static void CheckFabricDependencies(
        JsonElement root, string property, string loaderId, string version, string source, bool incompatible)
    {
        if (root.TryGetProperty(property, out var dependencies) && dependencies.TryGetProperty(loaderId, out var requirement))
            CheckRequirement(version, requirement, source, loaderId, incompatible, false);
    }

    private static void CheckQuiltDependencies(
        JsonElement root, string property, string loaderId, string version, string source, bool incompatible)
    {
        if (!root.TryGetProperty(property, out var dependencies)) return;
        foreach (var dependency in dependencies.EnumerateArray())
        {
            var matches = EvaluateQuiltDependency(dependency, loaderId, version, source, incompatible);
            if (matches.HasValue && matches.Value == incompatible)
                throw new InvalidOperationException($"{source} {(incompatible ? "不兼容" : "需要")} {loaderId} {DescribeQuiltDependency(dependency, incompatible)}，当前选择为 {version}。请更换 API 或 Loader 版本。");
        }
    }

    private static bool? EvaluateQuiltDependency(JsonElement dependency, string loaderId, string version, string source, bool incompatible)
    {
        if (dependency.ValueKind == JsonValueKind.String) return dependency.GetString() == loaderId ? true : null;
        if (dependency.ValueKind == JsonValueKind.Array)
        {
            var matches = dependency.EnumerateArray()
                .Select(item => EvaluateQuiltDependency(item, loaderId, version, source, incompatible)).ToArray();
            if (matches.All(match => !match.HasValue)) return null;
            if (matches.Any(match => !match.HasValue))
                throw new InvalidDataException("暂不能验证同时包含 Loader 和其他 Mod 的条件依赖：" + source);
            return incompatible ? matches.All(match => match!.Value) : matches.Any(match => match!.Value);
        }
        if (dependency.ValueKind != JsonValueKind.Object ||
            !dependency.TryGetProperty("id", out var id) || id.GetString() != loaderId) return null;
        if (dependency.TryGetProperty("unless", out _))
            throw new InvalidDataException("暂不能验证带 unless 的 Loader 依赖：" + source);
        return !dependency.TryGetProperty("versions", out var requirement) || FabricVersionPredicate.Matches(version, requirement, true);
    }

    private static void CheckRequirement(
        string version, JsonElement requirement, string source, string loaderId, bool incompatible, bool quilt)
    {
        if (FabricVersionPredicate.Matches(version, requirement, quilt) == incompatible)
            throw new InvalidOperationException($"{source} {(incompatible ? "不兼容" : "需要")} {loaderId} {DescribeRequirement(requirement)}，当前选择为 {version}。请更换 API 或 Loader 版本。");
    }

    private static string DescribeQuiltDependency(JsonElement dependency, bool incompatible) => dependency.ValueKind switch
    {
        JsonValueKind.Array => "(" + string.Join(incompatible ? " 且 " : " 或 ", dependency.EnumerateArray()
            .Select(item => DescribeQuiltDependency(item, incompatible))) + ")",
        JsonValueKind.Object when dependency.TryGetProperty("versions", out var versions) => DescribeRequirement(versions),
        _ => "*"
    };

    private static string DescribeRequirement(JsonElement requirement) => requirement.ValueKind switch
    {
        JsonValueKind.String => requirement.GetString()!,
        JsonValueKind.Array => "(" + string.Join(" 或 ", requirement.EnumerateArray().Select(DescribeRequirement)) + ")",
        JsonValueKind.Object => "(" + string.Join(requirement.TryGetProperty("all", out _) ? " 且 " : " 或 ",
            requirement.EnumerateObject().First().Value.EnumerateArray().Select(DescribeRequirement)) + ")",
        _ => requirement.GetRawText()
    };
}
