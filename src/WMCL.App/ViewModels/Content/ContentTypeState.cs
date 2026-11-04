namespace WMCL.App.ViewModels.Content;

/// <summary>资源类型对应的界面状态（从 InstanceContentViewModel 的布尔开关集中而来）。</summary>
public readonly record struct ContentTypeState(string Type, bool Shaders, bool Worlds, bool DataPacks)
{
    public bool IsPlugin => Type == "插件";

    public string Label => ContentTypeLabel.Of(Worlds, DataPacks, Shaders);

    public string SearchExample => ContentTypeLabel.SearchExample(Type);

    /// <summary>由导航键或类型名推导状态。</summary>
    public static ContentTypeState From(string? typeOrKey) => (typeOrKey ?? "") switch
    {
        "content-shader" or "光影" => new("光影", true, false, false),
        "content-world" or "世界" => new("世界", false, true, false),
        "content-datapack" or "数据包" => new("数据包", false, false, true),
        "插件" => new("插件", false, false, false),
        _ => new("资源包", false, false, false)
    };
}
