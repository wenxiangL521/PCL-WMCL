using System.Collections.Immutable;
using System.Globalization;
using WMCL.Core.IO;
using WMCL.Core.Logging;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.Modding.Resources;

public sealed class ResourceInstallPlanner(ModrinthClient? modrinth, CurseForgeClient? curseForge)
{
    public async Task<ResourceInstallPlan> ResolveAsync(
        IReadOnlyList<ResourceInstallRequest> requests, string gameVersion, string loader,
        CancellationToken ct = default, bool requireDependencies = true)
    {
        if (string.IsNullOrWhiteSpace(gameVersion)) throw new InvalidOperationException("请先选择 Minecraft 版本。");
        var rootRequests = new List<ResourceInstallRequest>();
        foreach (var request in requests)
        {
            var root = request;
            if (root.Platform == "Modrinth" && string.IsNullOrWhiteSpace(root.ProjectId))
            {
                var version = root.Modrinth;
                if (version is null && root.VersionId is { Length: > 0 } pinned && modrinth is not null)
                    version = await modrinth.GetVersionAsync(pinned, ct).ConfigureAwait(false);
                if (version is not null) root = root with { ProjectId = version.ProjectId, Modrinth = version };
            }
            rootRequests.Add(root);
        }
        var roots = rootRequests.Select(request => request.Platform + ":" + request.ProjectId).ToHashSet(StringComparer.Ordinal);
        var selectedRoots = rootRequests.Where(request => request.Modrinth is not null || request.CurseForge is not null ||
                                                        !string.IsNullOrWhiteSpace(request.VersionId))
            .GroupBy(request => request.Platform + ":" + request.ProjectId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var resolved = new Dictionary<string, ResourceInstallEntry>(StringComparer.Ordinal);
        var ordered = new List<ResourceInstallEntry>();
        var incompatible = new List<(string Owner, string Platform, string? Project, string? Version)>();
        foreach (var request in rootRequests) await ResolveAsync(request, 0).ConfigureAwait(false);
        foreach (var constraint in incompatible)
            if (ordered.Any(entry => entry.Platform == constraint.Platform &&
                (constraint.Project is null || entry.ProjectId == constraint.Project) &&
                (constraint.Version is null || entry.VersionId == constraint.Version)))
                throw new InvalidOperationException($"{constraint.Owner} 与所选资源不兼容：{constraint.Project ?? constraint.Version}");
        foreach (var group in ordered.GroupBy(entry => entry.ResourceType + ":" + entry.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            if (group.Skip(1).Any(entry => entry.Size != first.Size || entry.Sha1 is null || first.Sha1 is null ||
                                         !entry.Sha1.Equals(first.Sha1, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("不同资源使用了同一个文件名：" + first.FileName);
        }
        return new ResourceInstallPlan(gameVersion, loader, ordered.ToImmutableArray());

        async Task ResolveAsync(ResourceInstallRequest request, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > 128 || resolved.Count >= 512)
                throw new InvalidOperationException("必需依赖数量或深度超过限制。");
            if (request.ResourceType is not ("mod" or "resourcepack" or "shader" or "datapack" or "world" or "plugin"))
                throw new InvalidOperationException("该资源类型需要单独安装：" + request.ResourceType);
            if (string.IsNullOrWhiteSpace(request.VersionId) && request.Modrinth is null && request.CurseForge is null &&
                selectedRoots.TryGetValue(request.Platform + ":" + request.ProjectId, out var selected) &&
                request.ResourceType == selected.ResourceType)
                request = selected;
            if (string.IsNullOrWhiteSpace(request.VersionId) && request.Modrinth is null && request.CurseForge is null &&
                resolved.TryGetValue(request.Platform + ":" + request.ProjectId, out var existing) && existing.ResourceType == request.ResourceType)
                return;
            if (request.Platform == "Modrinth") await ResolveModrinthAsync(request, depth).ConfigureAwait(false);
            else if (request.Platform == "CurseForge") await ResolveCurseForgeAsync(request, depth).ConfigureAwait(false);
            else throw new InvalidOperationException("未知的资源来源：" + request.Platform);
        }

        async Task ResolveModrinthAsync(ResourceInstallRequest request, int depth)
        {
            var client = modrinth ?? throw new InvalidOperationException("Modrinth 服务不可用。");
            var version = request.Modrinth;
            if (version is null)
                version = request.VersionId is { Length: > 0 } pinned
                    ? await client.GetVersionAsync(pinned, ct).ConfigureAwait(false)
                    : await client.FindCompatibleVersionAsync(request.ProjectId, gameVersion, loader, ct, request.ResourceType).ConfigureAwait(false);
            if (version is null)
                throw new InvalidOperationException($"Modrinth 必需项目 {request.ProjectId} 没有适用于 {gameVersion} / {loader} 的版本。");
            if (string.IsNullOrWhiteSpace(version.ProjectId) || string.IsNullOrWhiteSpace(version.Id) ||
                !string.IsNullOrWhiteSpace(request.ProjectId) && version.ProjectId != request.ProjectId ||
                request.VersionId is { Length: > 0 } expected && version.Id != expected)
                throw new InvalidOperationException("Modrinth 必需版本或项目与请求不一致：" + request.ProjectId);
            if (!version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase) ||
                request.ResourceType == "datapack" && !version.Loaders.Contains("datapack", StringComparer.OrdinalIgnoreCase) ||
                request.ResourceType == "mod" && !version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Modrinth 必需版本 {version.Id} 不支持 Minecraft {gameVersion} / {loader}");
            var file = version.Files.FirstOrDefault(item => item.Primary) ?? version.Files.FirstOrDefault()
                ?? throw new InvalidOperationException("Modrinth 必需版本没有可下载文件：" + version.Id);
            ValidateFile(file.Filename, file.Url, file.Size, file.Sha1);
            var entry = new ResourceInstallEntry("Modrinth", version.ProjectId, version.Id,
                string.IsNullOrWhiteSpace(version.Name) ? request.Title : version.Name, request.ResourceType,
                file.Filename, file.Size, file.Sha1, [file.Url, ModrinthClient.ToMirror(file.Url)],
                !roots.Contains("Modrinth:" + version.ProjectId));
            if (!Register(entry)) return;
            if (request.ResourceType == "mod")
            {
                foreach (var dependency in version.Dependencies)
                {
                    if (dependency.DependencyType.Equals("incompatible", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(dependency.ProjectId) || !string.IsNullOrWhiteSpace(dependency.VersionId))
                            incompatible.Add((entry.Title, "Modrinth", EmptyToNull(dependency.ProjectId), EmptyToNull(dependency.VersionId)));
                    }
                    else if (dependency.DependencyType.Equals("required", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(dependency.ProjectId) && string.IsNullOrWhiteSpace(dependency.VersionId))
                            throw new InvalidOperationException("Modrinth 必需依赖缺少项目或版本标识：" + entry.Title);
                        await ResolveDependencyAsync(new ResourceInstallRequest("Modrinth", dependency.ProjectId ?? "",
                            dependency.ProjectId ?? dependency.VersionId!, VersionId: dependency.VersionId), depth + 1).ConfigureAwait(false);
                    }
                }
            }
            ordered.Add(entry);
        }

        async Task ResolveCurseForgeAsync(ResourceInstallRequest request, int depth)
        {
            var client = curseForge ?? throw new InvalidOperationException("CurseForge 服务不可用。");
            if (!long.TryParse(request.ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var projectId) || projectId <= 0 ||
                projectId.ToString(CultureInfo.InvariantCulture) != request.ProjectId)
                throw new InvalidOperationException("无效的 CurseForge 项目 ID：" + request.ProjectId);
            var file = request.CurseForge;
            if (file is null)
            {
                if (request.VersionId is { Length: > 0 } pinned)
                {
                    if (!long.TryParse(pinned, NumberStyles.None, CultureInfo.InvariantCulture, out var fileId) || fileId <= 0)
                        throw new InvalidOperationException("无效的 CurseForge 文件 ID：" + pinned);
                    file = await client.GetFileByIdAsync(projectId, fileId, ct).ConfigureAwait(false);
                }
                else file = await client.FindFileAsync(projectId, gameVersion, request.ResourceType == "mod" ? loader : null, ct).ConfigureAwait(false);
            }
            if (file is null) throw new InvalidOperationException("CurseForge 必需项目没有兼容文件：" + projectId);
            if (file.ProjectId > 0 && file.ProjectId != projectId ||
                request.VersionId is { Length: > 0 } expected && file.Id.ToString(CultureInfo.InvariantCulture) != expected)
                throw new InvalidOperationException("CurseForge 项目或文件与请求不一致：" + projectId);
            if (!file.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("CurseForge 必需文件不支持当前 Minecraft 版本：" + file.Id);
            var loaderTags = file.GameVersions.Where(value => value.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Forge", StringComparison.OrdinalIgnoreCase) || value.Equals("NeoForge", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Quilt", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (request.ResourceType == "mod" && loaderTags.Length > 0 && !loaderTags.Contains(loader, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("CurseForge 必需文件不支持当前加载器：" + file.Id);
            CurseForgeClient.ValidateDownloadMetadata(file);
            ValidateFile(file.FileName, file.DownloadUrl, file.Length, file.Sha1);
            var entry = new ResourceInstallEntry("CurseForge", request.ProjectId, file.Id.ToString(CultureInfo.InvariantCulture),
                file.DisplayName, request.ResourceType, file.FileName, file.Length, file.Sha1,
                [file.DownloadUrl, CurseForgeClient.ToMirror(file.DownloadUrl)], !roots.Contains("CurseForge:" + request.ProjectId));
            if (!Register(entry)) return;
            if (request.ResourceType == "mod")
                foreach (var dependency in file.Dependencies)
                {
                    if (dependency.RelationType == 5 && dependency.ModId > 0)
                        incompatible.Add((entry.Title, "CurseForge", dependency.ModId.ToString(CultureInfo.InvariantCulture), null));
                    else if (dependency.RelationType == 3)
                    {
                        if (dependency.ModId <= 0) throw new InvalidOperationException("CurseForge 必需依赖项目 ID 无效：" + file.Id);
                        var id = dependency.ModId.ToString(CultureInfo.InvariantCulture);
                        await ResolveDependencyAsync(new ResourceInstallRequest("CurseForge", id, id), depth + 1).ConfigureAwait(false);
                    }
                }
            ordered.Add(entry);
        }

        async Task ResolveDependencyAsync(ResourceInstallRequest dependency, int depth)
        {
            try { await ResolveAsync(dependency, depth).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (requireDependencies)
                    throw new InvalidOperationException($"无法解析必需依赖 {dependency.ProjectId}/{dependency.VersionId}：{exception.Message}", exception);
                Log.Warn($"跳过无法解析的必需依赖 {dependency.ProjectId}/{dependency.VersionId}：{exception.Message}");
            }
        }

        bool Register(ResourceInstallEntry entry)
        {
            if (resolved.TryGetValue(entry.Key, out var previous))
            {
                if (previous.VersionId != entry.VersionId || previous.ResourceType != entry.ResourceType)
                    throw new InvalidOperationException($"必需依赖版本冲突：{entry.Key} 同时要求 {previous.VersionId} 和 {entry.VersionId}");
                if (previous.FileName != entry.FileName || previous.Size != entry.Size ||
                    !string.Equals(previous.Sha1, entry.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("同一资源版本返回了不同的文件元数据：" + entry.Key);
                return false;
            }
            resolved.Add(entry.Key, entry);
            return true;
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ValidateFile(string name, string url, long size, string? sha1)
    {
        if (string.IsNullOrWhiteSpace(name) || name != FileUtil.SafeFileName(name) || name.Contains(':') || size <= 0 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            sha1 is not null && (sha1.Length != 40 || !sha1.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("资源文件元数据无效：" + name);
    }
}
