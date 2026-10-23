namespace WMCL.Modding.Modpack;

internal sealed class LocalModpackComponents
{
    private readonly Dictionary<string, string> _versions = new(StringComparer.OrdinalIgnoreCase);

    public string? GetVersion(string id) => _versions.GetValueOrDefault(id);

    public void Add(string? id, string? version, bool inferred = false)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            throw new InvalidDataException($"整合包组件缺少标识或版本：{id}。");
        id = id.Trim().ToLowerInvariant();
        version = version.Trim();
        if (id is not ("game" or "fabric" or "quilt" or "forge" or "neoforge"))
            throw new NotSupportedException($"暂不支持整合包组件：{id}。");
        if (_versions.TryGetValue(id, out var existing))
        {
            if (inferred && existing == version) return;
            throw new InvalidDataException($"整合包组件重复或版本冲突：{id}（{existing} / {version}）。");
        }
        _versions.Add(id, version);
    }

    public LocalModpackInfo Build(LocalModpackKind kind, string baseFolder, string name, string overrides)
    {
        if (!_versions.TryGetValue("game", out var game))
            throw new InvalidDataException("整合包缺少 Minecraft 游戏版本组件。");
        if (_versions.Keys.Count(id => id != "game") > 1)
            throw new InvalidDataException("整合包声明了多个互斥加载器：" + string.Join("、", _versions.Keys.Where(id => id != "game")));
        if (_versions.TryGetValue("forge", out var forge) && forge.StartsWith("0.", StringComparison.Ordinal))
            throw new NotSupportedException("暂不支持将 Cleanroom 组件作为 Forge 安装。");
        return new LocalModpackInfo(kind, baseFolder, name, game,
            _versions.GetValueOrDefault("fabric"), forge, _versions.GetValueOrDefault("neoforge"), overrides,
            _versions.GetValueOrDefault("quilt"));
    }
}
