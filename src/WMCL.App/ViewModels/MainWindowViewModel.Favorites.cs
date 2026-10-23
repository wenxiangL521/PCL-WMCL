using WMCL.App.Services;

namespace WMCL.App.ViewModels;

public partial class MainWindowViewModel
{
    private async Task OpenFavoriteAsync(FavoriteResource resource)
    {
        if (resource.ResourceType == "mod")
        {
            var loading = Mods.OpenFavoriteAsync(resource);
            NavigateTo("mods");
            await loading;
        }
        else if (resource.ResourceType == "modpack")
        {
            var loading = Modpack.OpenFavoriteAsync(resource);
            NavigateTo("modpack");
            await loading;
        }
        else
        {
            var key = resource.ResourceType switch
            {
                "datapack" => "content-datapack",
                "shader" => "content-shader",
                "world" => "content-world",
                _ => "content-resource"
            };
            if (Content.IsDownloadBusy)
                throw new InvalidOperationException("请等待资源搜索或安装结束后再打开收藏。");
            NavigateTo(key);
            await Content.OpenFavoriteAsync(resource);
        }
    }
}
