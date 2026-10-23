using WMCL.Modding.Modrinth;

namespace WMCL.App.Services;

/// <summary>把 CE/ME 分享码中的 Modrinth 项目 ID 解析为收藏元数据；数字 ID 暂不解析。</summary>
public sealed class ResourceLookupService(ModrinthClient modrinth)
{
    public async Task<FavoriteResource?> ResolveAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || long.TryParse(id, out _)) return null;
        var project = await modrinth.GetProjectAsync(id, ct).ConfigureAwait(false);
        if (project is null) return null;
        var type = project.ProjectType.ToLowerInvariant() switch
        {
            "resourcepack" or "resource_pack" => "resourcepack",
            "shader" or "shaders" => "shader",
            "modpack" or "modpacks" => "modpack",
            "datapack" or "data_pack" => "datapack",
            "plugin" or "plugins" => "plugin",
            _ => "mod"
        };
        return new FavoriteResource("Modrinth", project.Id, type, project.Title,
            project.Description, project.Slug);
    }
}
