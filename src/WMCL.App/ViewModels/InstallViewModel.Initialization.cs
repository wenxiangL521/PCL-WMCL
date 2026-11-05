namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
    private sealed record InitializationCatalog(string Name, Task Loading, Func<string?> Error);

    private async Task PrepareInstanceCatalogsAsync(string minecraft)
    {
        var root = _services.Settings.GameDirectory;
        var request = _instanceRequest;
        var minecraftCatalog = Download.LoadInstanceCatalogAsync();
        var catalogs = ComponentCards.Select(card => new InitializationCatalog(card.Kind,
            card.PrepareAsync(root, minecraft, forceReload: true),
            () => card.Kind == "Forge" && card.Error == "无" ? null : card.Error)).ToList();
        catalogs.Insert(0, new("Minecraft", minecraftCatalog, () => null));
        catalogs.Add(new("组件", Mods.PrepareGameVersionAsync(minecraft, forceReload: true), () => Mods.ComponentCatalogError));
        catalogs.Add(new(ApiName, ApiSelection.PrepareAsync(minecraft, IsFabricBase ? SelectedBaseLoader : "Fabric", forceReload: true),
            () => ApiSelection.HasError ? ApiSelection.Status : null));
        catalogs.Add(new("OptiFabric", OptiFabricSelection.PrepareAsync(minecraft, forceReload: true),
            () => OptiFabricSelection.HasError ? OptiFabricSelection.Status : null));
        var total = catalogs.Count;
        var pending = catalogs.Select(ObserveCatalogAsync).ToList();
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var error = await completed;
            if (request != _instanceRequest || root != _services.Settings.GameDirectory ||
                Download.SelectedVersion?.Id != minecraft) return;
            if (error is not null)
            {
                // 新契约：只有基座（Minecraft）列表失败才终止初始化；
                // 组件（Forge/NeoForge/Fabric/OptiFine/API 等）不可用属于正常情况，
                // 保留卡片自身的错误提示，页面照常可用。
                if (error.Message.StartsWith("获取 Minecraft ", StringComparison.Ordinal)) throw error;
            }
            InstanceInitializationProgress = 0.7 * (total - pending.Count) / total;
        }
        var versions = await minecraftCatalog;
        var official = versions.FirstOrDefault(version => version.Id == minecraft ||
            version.Id.Replace("∞", "infinite", StringComparison.Ordinal) == minecraft.Replace("∞", "infinite", StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Minecraft 列表中没有已安装的 {minecraft}");
        var selected = Download.SelectedVersion!;
        _suspendPreparation = true;
        try
        {
            Download.AcceptInstanceCatalog(versions);
            Download.SelectedVersion = new VersionItem
            {
                Id = minecraft,
                Type = official.Type,
                ReleaseTime = official.ReleaseTime,
                IsLatest = official.IsLatest,
                IsInstalled = selected.IsInstalled
            };
        }
        finally { _suspendPreparation = false; }
    }

    private static async Task<Exception?> ObserveCatalogAsync(InitializationCatalog catalog)
    {
        try
        {
            await catalog.Loading;
            var error = catalog.Error();
            return string.IsNullOrEmpty(error) ? null : new InvalidDataException($"获取 {catalog.Name} 版本列表失败：{error}");
        }
        catch (Exception exception)
        {
            return new InvalidDataException($"获取 {catalog.Name} 版本列表失败：{exception.Message}", exception);
        }
    }
}
