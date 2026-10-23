using WMCL.App.Services;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
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
