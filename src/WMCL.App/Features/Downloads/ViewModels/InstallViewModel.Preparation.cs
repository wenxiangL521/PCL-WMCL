using WMCL.App.Services;
using WMCL.Minecraft;

namespace WMCL.App.Features.Downloads;

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

    private static Dictionary<string, byte[]?> CaptureModificationTarget(string root, string id)
    {
        var directory = Path.Combine(root, "versions", id);
        return new[]
        {
            Path.Combine(directory, id + ".json"),
            InstallManifestStore.GetPath(root, id),
            Path.Combine(directory, "PCL", "install.json"),
            InstanceSettingsStore.PathFor(directory),
            Path.Combine(directory, "PCL", "instance.json")
        }.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
    }

    private static bool ModificationTargetMatches(Dictionary<string, byte[]?> snapshot) => snapshot.All(file =>
        file.Value is null ? !Path.Exists(file.Key) : File.Exists(file.Key) && File.ReadAllBytes(file.Key).AsSpan().SequenceEqual(file.Value));

    private IReadOnlyList<string> ModificationWarnings(CombinedInstallPlan plan)
    {
        var warnings = new List<string>();
        if (_instanceUsesGlobalDirectory && (IsFabricFamily(plan.BaseLoader) || plan.BaseLoader is "Forge" or "NeoForge"))
            warnings.Add("该版本尚未开启版本隔离，可能与别的 MC 版本共用同一个 Mod 和存档文件夹。修改组件也会影响使用此目录的其他实例。");
        if (!IsOriginalMinecraftReliable && _originalMinecraft == plan.Minecraft)
            warnings.Add($"WMCL 未能精准识别该版本，请确认它是不是 Minecraft {_originalMinecraft}，如果不正确，请取消并选择正确的版本！");
        if (!_services.Settings.HintModifyInstanceWarningAcknowledged)
            warnings.Add("如果该版本安装了 WMCL 不兼容的加载器，或经过第三方修改，这些改动将会丢失！");
        var original = _originalComponents.Values.Where(component =>
            component.Kind is "Forge" or "NeoForge" or "LiteLoader" || IsFabricFamily(component.Kind))
            .ToDictionary(component => component.Kind, component => component.Version);
        var next = new Dictionary<string, string>();
        if (plan.BaseLoader is not ("无" or "OptiFine") && plan.LoaderVersion is { } loader) next[plan.BaseLoader] = loader;
        if (plan.LiteLoader is { } lite) next["LiteLoader"] = lite.Version;
        if (original.Count > 0 && (_originalMinecraft != plan.Minecraft || original.Count != next.Count ||
            original.Any(pair => next.GetValueOrDefault(pair.Key) != pair.Value)))
            warnings.Add("现有的 Mod、资源包等在修改后可能会不兼容！");
        return warnings;
    }
}
