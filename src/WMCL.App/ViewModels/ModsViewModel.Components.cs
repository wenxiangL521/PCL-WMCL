using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Modding.Forgelike;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.ViewModels;

public partial class ModsViewModel
{
    private string? _loaderCatalogError;
    private string? _forgelikeCatalogError;
    private string? _optiFineCatalogError;

    internal string? ComponentCatalogError => _loaderCatalogError ?? _forgelikeCatalogError ?? _optiFineCatalogError;

    internal Task PrepareInstallComponentAsync(string kind)
    {
        if (kind is "Forge" or "NeoForge")
        {
            SelectedForgelikeKind = kind;
            return LoadForgelikeVersionsAsync();
        }
        if (kind is "Fabric" or "Quilt" or "LegacyFabric")
        {
            SelectedFabricKind = kind;
            return LoadLoadersAsync();
        }
        return kind == "OptiFine" ? LoadOptiFineVersionsAsync() : Task.CompletedTask;
    }

    internal async Task<IReadOnlyList<InstallComponentChoice>> GetInstallComponentChoicesAsync(
        string kind, string minecraft, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (kind == "LiteLoader")
            return !LiteLoaderService.SupportsMinecraft(minecraft) ? [] : (await _getLiteLoaderVersionsCt(minecraft, ct))
                .Where(version => version.Minecraft == minecraft)
                .Select(version => new InstallComponentChoice(version.Version, version.Minecraft,
                    (version.IsPreview ? "测试版" : "稳定版") +
                    (version.Published is { } published ? $"，发布于 {published.LocalDateTime:yyyy/MM/dd HH:mm}" : ""), LiteLoader: version)).ToArray();
        if (kind == "OptiFine")
            return (await _getOptiFineVersionsCt(minecraft, ct)).Select(version => new InstallComponentChoice(
                version.Patch, version.DisplayName, (version.IsPreview ? "测试版" : "正式版") +
                (version.Published is { } published ? $"，发布于 {published:yyyy/MM/dd}" : "") +
                (version.ForgeVersion is null ? "，不兼容 Forge" :
                    string.IsNullOrWhiteSpace(version.ForgeVersion) ? "" : "，兼容 Forge " + version.ForgeVersion), version)).ToArray();
        if (kind == "Forge")
            return (await _getForgeVersionDetailsCt(minecraft, ct)).Select(version => new InstallComponentChoice(
                version.Version, version.Version,
                version.Published is { } published ? $"发布于 {published.LocalDateTime:yyyy/MM/dd HH:mm}" : "",
                IsRecommended: version.IsRecommended, Forge: version)).ToArray();
        if (kind == "NeoForge")
            return (await _getForgelikeVersionsCt(kind == "Forge" ? ForgelikeKind.Forge : ForgelikeKind.NeoForge, minecraft, ct))
                .Select(version => new InstallComponentChoice(version, version,
                    kind == "NeoForge" ? version.Contains("beta", StringComparison.OrdinalIgnoreCase) ? "测试版" : "稳定版" : "")).ToArray();
        return (await _getLoadersCt(kind, minecraft, ct)).Select(version => new InstallComponentChoice(version.Version,
            version.Version.Replace("+build", "", StringComparison.Ordinal), version.Stable ? "稳定版" : "测试版")).ToArray();
    }

    private sealed record ComponentGameVersion(string? Minecraft, string? Error);
    private Dictionary<string, ComponentGameVersion> _componentGameVersions = [];
    private readonly Func<Task<IReadOnlyList<string>>> _getOfficialVersions;
    private readonly Func<StandaloneLoaderInstallRequest, IProgress<JobProgress>, IProgress<string>?, CancellationToken, Task<string>> _installComponent;
    private string? _componentGameRoot;
    private bool _preparingCatalogGame;

    [ObservableProperty]
    private string? _selectedVanillaInstance;

    [ObservableProperty]
    private string _componentTargetInfo = "选择原版实例以读取 Minecraft 版本";

    private bool HasCurrentComponentGame => !string.IsNullOrWhiteSpace(SelectedGameVersion) &&
                                           IsCurrentComponentRoot(_componentGameRoot);

    partial void OnSelectedVanillaInstanceChanged(string? value)
    {
        if (!_preparingCatalogGame) UpdateComponentGameVersion();
    }

    private void UpdateComponentGameVersion()
    {
        var target = SelectedVanillaInstance is { } id ? _componentGameVersions.GetValueOrDefault(id) : null;
        SelectedGameVersion = target?.Minecraft;
        ComponentTargetInfo = target is { Minecraft: { } minecraft }
            ? $"{SelectedVanillaInstance} · Minecraft {minecraft} · 新建加载器实例"
            : target?.Error ?? "选择原版实例以读取 Minecraft 版本";
        if (target?.Error is { } error)
        {
            if (_cts is null) Status = error;
            if (_forgelikeCts is null) ForgelikeStatus = error;
            if (_optiFineCts is null) OptiFineStatus = error;
        }
        UpdateCommands();
        UpdateOptiFineCommands();
    }

    private bool IsCurrentComponentRoot(string? root) => root is not null &&
        root == _componentGameRoot && root == _services.Settings.GameDirectory;

    private void EnsureCurrentComponentRoot([NotNull] string? root)
    {
        if (root is null || !IsCurrentComponentRoot(root))
            throw new InvalidOperationException("游戏目录已切换，请刷新并重新选择原版实例。");
    }

    private async Task SelectInstalledComponentAsync(string root, string id)
    {
        if (_services.Settings.GameDirectory != root) return;
        if (!ModInstances.Contains(id)) ModInstances.Add(id);
        SelectedModInstance = id;
        await UpdateModTargetAsync();
    }
}
