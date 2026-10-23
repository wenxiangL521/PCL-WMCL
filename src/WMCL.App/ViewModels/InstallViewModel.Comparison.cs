using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Minecraft;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
    internal sealed record OriginalComponent(string Kind, string Version, string Summary, InstallComponentChoice? Choice);

    private readonly Dictionary<string, OriginalComponent> _originalComponents = [];
    private string? _originalMinecraft;
    private MinecraftVersionIdentity? _originalMinecraftIdentity;
    private VersionItem? _originalMinecraftVersion;
    private int _minecraftRestoreRequest;

    [ObservableProperty]
    private bool _isRestoringMinecraft;

    public string MinecraftVersionText => Download.SelectedVersion?.Id ?? "";
    public string OriginalMinecraftVersionText => _originalMinecraft ?? "";
    public bool HasMinecraftComparison => HasComponentBaseline && MinecraftVersionText != _originalMinecraft;
    public bool ShowMinecraftSummary => !HasMinecraftComparison;
    public Bitmap MinecraftIcon => MinecraftImage(Download.SelectedVersion);
    public Bitmap OriginalMinecraftIcon => MinecraftImage(_originalMinecraftVersion);
    public bool CanRestoreMinecraft => HasMinecraftComparison && CanEditInstall &&
        _originalMinecraftVersion is not null && _originalComponents.Values.All(component => component.Choice is not null);
    public string MinecraftRestoreHint => _originalComponents.Values.Any(component => component.Choice is null)
        ? "原组件版本信息不完整，无法恢复原始选择" : "撤销更改";

    private static Bitmap MinecraftImage(VersionItem? version) => VersionCatalogEntry.LoadIcon(
        $"avares://WMCL.App/Assets/Blocks/{(version is null ? "Grass" : VersionCatalogViewModel.Classify(version) switch
        {
            "正式版" => "Grass",
            "预览版" => "CommandBlock",
            "愚人节版" => "GoldBlock",
            _ => "CobbleStone"
        })}.png");

    private static VersionItem MinecraftSnapshot(VersionItem? version, string minecraft) => new()
    {
        Id = minecraft,
        Type = version?.Id == minecraft ? version.Type : "release",
        ReleaseTime = version?.Id == minecraft ? version.ReleaseTime : null,
        IsInstalled = version?.Id == minecraft && version.IsInstalled
    };

    partial void OnIsRestoringMinecraftChanged(bool value) => NotifyInstallAction();

    private void NotifyMinecraftComparison()
    {
        OnPropertyChanged(nameof(MinecraftVersionText));
        OnPropertyChanged(nameof(OriginalMinecraftVersionText));
        OnPropertyChanged(nameof(HasMinecraftComparison));
        OnPropertyChanged(nameof(ShowMinecraftSummary));
        OnPropertyChanged(nameof(MinecraftIcon));
        OnPropertyChanged(nameof(OriginalMinecraftIcon));
        OnPropertyChanged(nameof(CanRestoreMinecraft));
        OnPropertyChanged(nameof(MinecraftRestoreHint));
        RestoreMinecraftCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<InstallComponentCardViewModel> AllComponentCards => [.. ComponentCards, ApiCard, OptiFabricCard];
    internal bool HasComponentBaseline => IsExistingInstance && _originalMinecraft is not null;
    internal bool IsOriginalMinecraftReliable => _originalMinecraftIdentity?.IsReliable == true;
    private bool IsOriginalSelection => HasComponentBaseline && Download.SelectedVersion?.Id == _originalMinecraft &&
        AllComponentCards.All(card => IsOriginalComponentSelection(card.Kind));

    internal bool IsOriginalComponentSelection(string kind)
    {
        var original = OriginalFor(kind);
        if (ComponentVersion(kind) != original?.Version || original is not null && original.Kind != kind) return false;
        var current = CurrentComponentChoice(kind);
        if (original?.Choice?.Api is { } api)
            return current?.Api is { } selected && selected.Id == api.Id && selected.ProjectId == api.ProjectId &&
                selected.Files.SequenceEqual(api.Files);
        if (original?.Choice?.OptiFabric is { } optifabric)
            return current?.OptiFabric is { } selected && selected.Id == optifabric.Id &&
                selected.FileName == optifabric.FileName && selected.Size == optifabric.Size &&
                string.Equals(selected.Sha1, optifabric.Sha1, StringComparison.OrdinalIgnoreCase);
        if (original?.Choice?.OptiFine is { } optifine)
            return current?.OptiFine is { } selected && selected.Minecraft == optifine.Minecraft &&
                selected.Type == optifine.Type && selected.FileName == optifine.FileName;
        return true;
    }

    private static string ComparisonKey(string kind) => IsFabricFamily(kind) ? "Fabric" : IsApiKind(kind) ? "Fabric API" : kind;
    internal OriginalComponent? OriginalFor(string kind) => HasComponentBaseline
        ? _originalComponents.GetValueOrDefault(ComparisonKey(kind)) : null;

    internal string? ComponentVersion(string kind) => !IsComponentSelected(kind) ? null : kind switch
    {
        "OptiFine" => Mods.SelectedOptiFineVersion?.Patch,
        "OptiFabric" => OptiFabricSelection.SelectedVersion?.Version,
        "LiteLoader" => SelectedLiteLoader?.Version,
        "Forge" or "NeoForge" => Mods.SelectedForgelikeVersion,
        _ when IsApiKind(kind) => ApiSelection.SelectedVersion?.VersionNumber,
        _ => Mods.SelectedLoader
    };

    private InstallComponentChoice? CurrentComponentChoice(string kind) => ComponentVersion(kind) is not { } version ? null :
        new(version, ComponentSelectedSummary(kind), "",
            OptiFine: kind == "OptiFine" ? Mods.SelectedOptiFineVersion : null,
            Api: IsApiKind(kind) && ApiSelection.SelectedVersion is { } api
                ? WMCL.Modding.Fabric.ApiComponentService.Snapshot(api) : null,
            OptiFabric: kind == "OptiFabric" ? OptiFabricSelection.SelectedVersion : null,
            LiteLoader: kind == "LiteLoader" && SelectedLiteLoader is { } lite
                ? lite with { Libraries = lite.Libraries.Clone() } : null);

    private void ClearComponentBaseline()
    {
        _originalMinecraftIdentity = null;
        ClearPendingComponents();
        ++_minecraftRestoreRequest;
        _originalMinecraftVersion = null;
        _originalMinecraft = null;
        _originalComponents.Clear();
        IsRestoringMinecraft = false;
    }

    private void CaptureComponentBaseline(string minecraft, string loader, string? loaderVersion,
        IReadOnlyDictionary<string, string> components, LiteLoaderVersion? liteLoader, MinecraftVersionIdentity identity)
    {
        ClearComponentBaseline();
        _originalMinecraftIdentity = identity;
        _originalMinecraft = minecraft;
        _originalMinecraftVersion = MinecraftSnapshot(Download.SelectedVersion, minecraft);
        if (loader is not ("无" or "OptiFine")) Capture(loader, loaderVersion ?? "未知版本");
        if (components.TryGetValue("optifine", out var optifine)) Capture("OptiFine", optifine);
        if (components.TryGetValue(ApiProjectFor(loader), out var api)) Capture(ApiName, api);
        if (components.TryGetValue("optifabric", out var optifabric)) Capture("OptiFabric", optifabric);
        if (liteLoader is not null) Capture("LiteLoader", liteLoader.Version);
        NotifyInstallAction();

        void Capture(string kind, string version)
        {
            var choice = ComponentVersion(kind) == version ? CurrentComponentChoice(kind) : null;
            _originalComponents[ComparisonKey(kind)] = new(kind, version, choice?.Title ?? version.Split('+')[0], choice);
        }
    }

    private void CaptureComponentBaseline(CombinedInstallPlan plan)
    {
        ClearComponentBaseline();
        _originalMinecraftIdentity = new(plan.Minecraft, true, MinecraftVersionIdentitySource.InstallManifest);
        _originalMinecraft = plan.Minecraft;
        _originalMinecraftVersion = MinecraftSnapshot(Download.SelectedVersion, plan.Minecraft);
        if (plan.HasBaseLoader && plan.BaseLoader != "OptiFine" && plan.LoaderVersion is { } loader)
            Save(plan.BaseLoader, new(loader, loader, ""));
        if (plan.OptiFine is { } optifine)
            Save("OptiFine", new(optifine.Patch, optifine.DisplayName.Replace(plan.Minecraft + " ", "", StringComparison.Ordinal), "", OptiFine: optifine));
        if (plan.InstallApi && plan.ApiVersion is { } api)
            Save(plan.ApiName, new(api.VersionNumber, api.VersionNumber.Split('+')[0], "", Api: api));
        if (plan.InstallOptiFabric && plan.OptiFabric is { } optifabric)
            Save("OptiFabric", new(optifabric.Version, optifabric.Version, "", OptiFabric: optifabric));
        if (plan.LiteLoader is { } lite)
            Save("LiteLoader", new(lite.Version, lite.Minecraft, "", LiteLoader: lite));
        NotifyInstallAction();

        void Save(string kind, InstallComponentChoice choice) =>
            _originalComponents[ComparisonKey(kind)] = new(kind, choice.Version, choice.Title, choice);
    }

    internal bool CanRestoreComponent(InstallComponentCardViewModel card)
    {
        if (!CanEditInstall || !HasComponentBaseline) return false;
        if (OriginalFor(card.Kind) is not { } original) return card.IsSelected;
        if (original.Choice is not { } choice || card.IsLoading || card.Error.Length > 0 ||
            card.Entries.Count == 0 && choice.Api is null && choice.OptiFabric is null && choice.OptiFine is null ||
            !IsComponentVisible(original.Kind) ||
            ComponentIncompatibility(original.Kind) is not null) return false;
        if (IsFabricFamily(original.Kind))
            return card.Kind == original.Kind && card.Entries.Any(entry => entry.Choice.Version == original.Version);
        if (Download.SelectedVersion?.Id != _originalMinecraft) return false;
        return CanChooseComponent(original.Kind, choice);
    }

    internal async Task RestoreComponentAsync(InstallComponentCardViewModel card)
    {
        if (!card.HasComparison || !CanRestoreComponent(card)) return;
        if (OriginalFor(card.Kind)?.Choice is { } choice)
            await SelectComponentVersionAsync(card.Kind, choice);
        else ClearComponent(card.Kind);
        card.IsExpanded = false;
    }

    [RelayCommand(CanExecute = nameof(CanRestoreMinecraft))]
    private async Task RestoreMinecraftAsync()
    {
        if (!CanRestoreMinecraft || _originalMinecraftVersion is not { } minecraft || _instanceTarget is not { } target) return;
        var components = _originalComponents.Values.ToArray();
        ClearPendingComponents();
        var request = ++_minecraftRestoreRequest;
        IsRestoringMinecraft = true;
        try
        {
            _suspendPreparation = true;
            try
            {
                Download.SelectedVersion = MinecraftSnapshot(minecraft, minecraft.Id);
                _instanceTarget = target with { Minecraft = minecraft.Id };
                SelectedBaseLoader = components.FirstOrDefault(component =>
                    component.Kind is "Forge" or "NeoForge" || IsFabricFamily(component.Kind))?.Kind
                    ?? (components.Any(component => component.Kind == "OptiFine") ? "OptiFine" : "无");
                InstallOptiFineWithBase = CanAddOptiFine && components.Any(component => component.Kind == "OptiFine");
                InstallFabricApi = components.Any(component => IsApiKind(component.Kind));
                InstallOptiFabric = components.Any(component => component.Kind == "OptiFabric");
            }
            finally { _suspendPreparation = false; }
            await (_selectionLoad = PrepareSelectedVersionAsync());
            if (request != _minecraftRestoreRequest || _services.Settings.GameDirectory != target.Root) return;
            await Task.WhenAll(ComponentCards.Select(card => card.PrepareAsync(target.Root, minecraft.Id)));
            if (request != _minecraftRestoreRequest || _instanceTarget?.Id != target.Id ||
                _services.Settings.GameDirectory != target.Root) return;
            _suspendPreparation = true;
            try
            {
                var loader = components.FirstOrDefault(component =>
                    component.Kind is "Forge" or "NeoForge" || IsFabricFamily(component.Kind));
                Mods.SelectedLoader = loader is not null && IsFabricFamily(loader.Kind) ? loader.Version : null;
                Mods.SelectedForgelikeVersion = loader?.Kind is "Forge" or "NeoForge" ? loader.Version : null;
                Mods.SelectedOptiFineVersion = components.FirstOrDefault(component => component.Kind == "OptiFine")?.Choice?.OptiFine;
                ApiSelection.SelectedVersion = components.FirstOrDefault(component => IsApiKind(component.Kind))?.Choice?.Api;
                OptiFabricSelection.SelectedVersion = components.FirstOrDefault(component => component.Kind == "OptiFabric")?.Choice?.OptiFabric;
                SetLiteLoaderSelection(components.FirstOrDefault(component => component.Kind == "LiteLoader")?.Choice?.LiteLoader);
                foreach (var card in AllComponentCards) card.IsExpanded = false;
            }
            finally { _suspendPreparation = false; }
            NotifyComponentSummaries();
            NotifyInstallHints();
        }
        finally
        {
            if (request == _minecraftRestoreRequest) IsRestoringMinecraft = false;
        }
    }
}
