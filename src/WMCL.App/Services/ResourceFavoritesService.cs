using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;

namespace WMCL.App.Services;

public sealed record FavoriteResource(
    string Platform, string ProjectId, string ResourceType,
    string Title, string Description, string Slug = "")
{
    [JsonIgnore]
    public string Key => Platform + ":" + ProjectId;

    [JsonIgnore]
    public string TypeLabel => ResourceType switch
    {
        "mod" => "Mod",
        "modpack" => "整合包",
        "datapack" => "数据包",
        "shader" => "光影",
        "world" => "存档",
        "plugin" => "插件",
        _ => "资源包"
    };

    [JsonIgnore]
    public string Detail => $"{TypeLabel} · {Platform} · {ProjectId}";

    public bool IsValid() =>
        ResourceType is "mod" or "modpack" or "datapack" or "resourcepack" or "shader" or "world" or "plugin" &&
        !string.IsNullOrWhiteSpace(Title) &&
        !string.IsNullOrWhiteSpace(ProjectId) &&
        (Platform == "Modrinth" && ProjectId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
         Platform == "CurseForge" && long.TryParse(ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
         id > 0 && id.ToString(CultureInfo.InvariantCulture) == ProjectId);
}

public sealed record ResourceFavoriteFolder(string Id, string Name, IReadOnlyList<FavoriteResource> Resources);

public sealed class ResourceFavoritesService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppSettings _settings;
    private readonly Action _save;

    public ResourceFavoritesService(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        var folders = new List<ResourceFavoriteFolder>();
        foreach (var folder in settings.ResourceFavoriteFolders ?? [])
        {
            if (folder is null || string.IsNullOrWhiteSpace(folder.Id) ||
                string.IsNullOrWhiteSpace(folder.Name) || folders.Any(item => item.Id == folder.Id)) continue;
            folders.Add(folder with
            {
                Resources = (folder.Resources ?? []).Where(resource => resource?.IsValid() == true)
                    .Select(resource => resource with { Description = resource.Description ?? "", Slug = resource.Slug ?? "" })
                    .DistinctBy(resource => resource.Key).ToArray()
            });
        }
        if (folders.Count == 0)
            folders.Add(new ResourceFavoriteFolder("default", "默认收藏夹", []));
        settings.ResourceFavoriteFolders = folders;
    }

    public event EventHandler? Changed;
    public IReadOnlyList<ResourceFavoriteFolder> Folders => _settings.ResourceFavoriteFolders;

    public bool Contains(string folderId, FavoriteResource resource) =>
        Folder(folderId).Resources.Any(item => item.Key == resource.Key);

    public ResourceFavoriteFolder CreateFolder(string name)
    {
        name = ValidateName(name);
        var folder = new ResourceFavoriteFolder(Guid.NewGuid().ToString("N"), name, []);
        Commit([.. Folders, folder]);
        return folder;
    }

    public void RenameFolder(string id, string name)
    {
        Folder(id);
        name = ValidateName(name, id);
        Commit(Folders.Select(folder => folder.Id == id ? folder with { Name = name } : folder).ToList());
    }

    public void DeleteFolder(string id)
    {
        Folder(id);
        if (Folders.Count == 1) throw new InvalidOperationException("请至少保留一个收藏夹。");
        Commit(Folders.Where(folder => folder.Id != id).ToList());
    }

    public static string CreateShareCode(IEnumerable<FavoriteResource> resources) =>
        JsonSerializer.Serialize(resources.Where(resource => resource.IsValid())
            .DistinctBy(resource => resource.Key).ToArray(), JsonOptions);

    public static string CreateNativeShareCode(IEnumerable<FavoriteResource> resources) =>
        JsonSerializer.Serialize(resources.Select(resource => resource.ProjectId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions);

    public static IReadOnlyList<string> ParseNativeShareCode(string code)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(code, JsonOptions) ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<FavoriteResource> ParseShareCode(string code)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<FavoriteResource>>(code, JsonOptions) ?? [])
                .Where(resource => resource.IsValid())
                .DistinctBy(resource => resource.Key)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void SetFavorite(string folderId, FavoriteResource resource, bool favorite)
    {
        if (!resource.IsValid()) throw new InvalidOperationException("资源项目的来源、类型或 ID 无效。");
        resource = resource with { Description = resource.Description ?? "", Slug = resource.Slug ?? "" };
        var folder = Folder(folderId);
        var resources = folder.Resources.Where(item => item.Key != resource.Key).ToList();
        if (favorite) resources.Insert(0, resource);
        Replace(folder with { Resources = resources });
    }

    public void Remove(string folderId, IReadOnlyCollection<string> keys)
    {
        var folder = Folder(folderId);
        Replace(folder with { Resources = folder.Resources.Where(item => !keys.Contains(item.Key)).ToArray() });
    }

    public void Transfer(string sourceId, string destinationId, IReadOnlyCollection<string> keys, bool move)
    {
        var source = Folder(sourceId);
        var destination = Folder(destinationId);
        if (sourceId == destinationId) throw new InvalidOperationException("请选择另一个收藏夹。");
        var selected = source.Resources.Where(item => keys.Contains(item.Key)).ToArray();
        var merged = selected.Concat(destination.Resources).DistinctBy(item => item.Key).ToArray();
        Commit(Folders.Select(folder => folder.Id == destinationId ? folder with { Resources = merged }
            : move && folder.Id == sourceId
                ? folder with { Resources = folder.Resources.Where(item => !keys.Contains(item.Key)).ToArray() }
                : folder).ToList());
    }

    private ResourceFavoriteFolder Folder(string id) =>
        Folders.FirstOrDefault(folder => folder.Id == id)
        ?? throw new InvalidOperationException("收藏夹已被删除，请重新选择。");

    private string ValidateName(string name, string? exceptId = null)
    {
        name = name.Trim();
        if (name.Length is 0 or > 60) throw new InvalidOperationException("收藏夹名称需要 1 到 60 个字符。");
        if (Folders.Any(folder => folder.Id != exceptId && string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已有同名收藏夹。");
        return name;
    }

    private void Replace(ResourceFavoriteFolder replacement) =>
        Commit(Folders.Select(folder => folder.Id == replacement.Id ? replacement : folder).ToList());

    private void Commit(List<ResourceFavoriteFolder> folders)
    {
        var previous = _settings.ResourceFavoriteFolders;
        _settings.ResourceFavoriteFolders = folders;
        try { _save(); }
        catch
        {
            _settings.ResourceFavoriteFolders = previous;
            throw;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
