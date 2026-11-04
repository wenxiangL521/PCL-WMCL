namespace WMCL.App.ViewModels.Content;

/// <summary>实例资源类型的中文名与搜索示例（从 InstanceContentViewModel 抽出）。</summary>
public static class ContentTypeLabel
{
    public static string Of(bool worlds, bool dataPacks, bool shaders) =>
        worlds ? "存档" : dataPacks ? "数据包" : shaders ? "光影" : "资源包";

    public static string SearchExample(string? type) => type switch
    {
        "插件" => "搜索服务端插件，例如 luckperms",
        "世界" => "搜索世界，例如 skyblock",
        "数据包" => "搜索数据包，例如 terralith",
        "光影" => "搜索光影，例如 bsl",
        _ => "搜索资源包，例如 faithful"
    };
}
