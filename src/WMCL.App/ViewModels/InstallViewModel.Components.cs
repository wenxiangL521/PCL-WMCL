using System.Globalization;
using System.Text.RegularExpressions;
using WMCL.Modding.Loaders;

namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
    public InstallComponentCardViewModel ForgeCard { get; private set; } = null!;
    public InstallComponentCardViewModel NeoForgeCard { get; private set; } = null!;
    public InstallComponentCardViewModel FabricCard { get; private set; } = null!;
    public InstallComponentCardViewModel OptiFineCard { get; private set; } = null!;
    public InstallComponentCardViewModel ApiCard { get; private set; } = null!;
    public InstallComponentCardViewModel OptiFabricCard { get; private set; } = null!;
    public InstallComponentCardViewModel LiteLoaderCard { get; private set; } = null!;

    private void InitializeComponentCards()
    {
        ForgeCard = new(this, "Forge", Mods.GetInstallComponentChoicesAsync);
        NeoForgeCard = new(this, "NeoForge", Mods.GetInstallComponentChoicesAsync);
        FabricCard = new(this, "Fabric", Mods.GetInstallComponentChoicesAsync);
        OptiFineCard = new(this, "OptiFine", Mods.GetInstallComponentChoicesAsync);
        ApiCard = new(this, "Fabric API", Mods.GetInstallComponentChoicesAsync);
        OptiFabricCard = new(this, "OptiFabric", Mods.GetInstallComponentChoicesAsync);
        LiteLoaderCard = new(this, "LiteLoader", Mods.GetInstallComponentChoicesAsync);
    }

    private IEnumerable<InstallComponentCardViewModel> ComponentCards => [ForgeCard, NeoForgeCard, FabricCard, OptiFineCard, LiteLoaderCard];

    internal bool IsCurrentComponentCatalog(string root, string minecraft) =>
        _services.Settings.GameDirectory == root && Download.SelectedVersion?.Id == minecraft;

    internal bool IsComponentVisible(string kind)
    {
        if (Download.SelectedVersion is not { } game) return false;
        if (kind == "LiteLoader")
        {
            var name = game.Id.ToLowerInvariant().Replace("_unobfuscated", "", StringComparison.Ordinal)
                .Replace(" unobfuscated", "", StringComparison.Ordinal);
            if (name.StartsWith("2.0", StringComparison.Ordinal) || name == "15w14a" || name.Contains(".rv-pre", StringComparison.Ordinal)) return true;
            var major = Regex.Match(name, @"^1\.(\d+)");
            return major.Success && int.TryParse(major.Groups[1].Value, out var minor) && minor < 13;
        }
        if (kind == "Forge")
            return Regex.IsMatch(game.Id, @"^1\.\d") ||
                double.TryParse(Regex.Match(game.Id, @"^[2-9]\d\.\d+").Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var modernVersion) && modernVersion >= 26;
        if (kind == "NeoForge")
            return !DateTimeOffset.TryParse(game.ReleaseTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var released) ||
                released >= new DateTimeOffset(2023, 6, 11, 0, 0, 0, TimeSpan.Zero);
        if (kind is "Fabric" or "Quilt")
        {
            var name = game.Id.ToLowerInvariant().Replace("_unobfuscated", "", StringComparison.Ordinal)
                .Replace(" unobfuscated", "", StringComparison.Ordinal);
            if (name.StartsWith("2.0", StringComparison.Ordinal) || name == "15w14a" ||
                name.Contains(".rv-pre", StringComparison.Ordinal) || name.Contains("shareware", StringComparison.Ordinal)) return false;
            var oldVersion = Regex.Match(name, @"^1\.(\d+)");
            return !oldVersion.Success || int.TryParse(oldVersion.Groups[1].Value, out var minor) && minor > 13;
        }
        return kind == "OptiFabric" ? ShowOptiFabricOptions : IsApiKind(kind) ? IsFabricBase : true;
    }

    internal void PrepareComponentCards()
    {
        if (Download.SelectedVersion is { } version && _hasChosenVersion && !_suspendPreparation)
            foreach (var card in ComponentCards) _ = card.PrepareAsync(_services.Settings.GameDirectory, version.Id);
    }

    private void RefreshComponentCards()
    {
        if (FabricCard is null) return;
        if (IsFabricBase && FabricCard.Kind != SelectedBaseLoader) FabricCard.Kind = SelectedBaseLoader;
        foreach (var card in ComponentCards) card.RefreshState();
        RefreshAdditionalCards();
    }

    private void RefreshAdditionalCards()
    {
        if (ApiCard is null || Download.SelectedVersion is not { } game) return;
        ApiCard.Kind = ApiName;
        var root = _services.Settings.GameDirectory;
        ApiCard.UpdateExternalChoices(root, game.Id, ApiSelection.Versions.Select(version =>
            new InstallComponentChoice(version.Id, version.VersionNumber.Split('+')[0],
                (version.VersionType == "release" ? "正式版" : "测试版") + $"，发布于 {version.DatePublished.LocalDateTime:yyyy/MM/dd HH:mm}", Api: version)).ToArray(),
            ApiSelection.IsBusy, ApiSelection.HasError ? ApiSelection.Status : "");
        OptiFabricCard.UpdateExternalChoices(root, game.Id, OptiFabricSelection.Versions.Select(version =>
            new InstallComponentChoice(version.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), version.Version,
                $"发布于 {version.Published.LocalDateTime:yyyy/MM/dd HH:mm}", OptiFabric: version)).ToArray(),
            OptiFabricSelection.IsBusy, OptiFabricSelection.HasError ? OptiFabricSelection.Status : "");
    }

    internal Task RetryAdditionalComponentAsync(string kind) => kind == "OptiFabric"
        ? OptiFabricSelection.RetryCommand.ExecuteAsync(null) : ApiSelection.RetryCommand.ExecuteAsync(null);

    private static bool IsApiKind(string kind) => kind is "Fabric API" or "Quilted Fabric API / QSL" or "Legacy Fabric API";

    internal bool IsComponentSelected(string kind) => kind == "OptiFine" ? IsOptiFineBase || InstallOptiFineWithBase
        : kind == "LiteLoader" ? SelectedLiteLoader is not null
        : kind == "OptiFabric" ? ShowOptiFabricOptions && InstallOptiFabric && OptiFabricSelection.SelectedVersion is not null
        : IsApiKind(kind) ? IsFabricBase && InstallFabricApi && ApiSelection.SelectedVersion is not null
        : SelectedBaseLoader == kind;

    internal string ComponentSelectedSummary(string kind)
    {
        return kind switch
        {
            "LiteLoader" => SelectedLiteLoader?.Minecraft ?? "未选择",
            "OptiFabric" => OptiFabricSelection.SelectedVersion?.Version ?? "未选择",
            "Fabric API" or "Quilted Fabric API / QSL" or "Legacy Fabric API" => ApiSelection.SelectedVersion?.VersionNumber.Split('+')[0] ?? "未选择",
            "OptiFine" => Mods.SelectedOptiFineVersion?.DisplayName.Replace((Download.SelectedVersion?.Id ?? "") + " ", "", StringComparison.Ordinal).Trim() ?? "未选择",
            "Forge" or "NeoForge" => Mods.SelectedForgelikeVersion ?? "未选择",
            _ => Mods.SelectedLoader?.Replace("+build", "", StringComparison.Ordinal) ?? "未选择"
        };
    }

    internal string? ComponentIncompatibility(string kind)
    {
        if (IsApiKind(kind)) return IsFabricBase ? null : "请先选择 Fabric";
        if (kind == "OptiFabric") return ShowOptiFabricOptions ? null : "请先选择 Fabric 和 OptiFine";
        if (kind == "LiteLoader") return SelectedBaseLoader is "无" or "Forge" or "OptiFine" ? null : $"与 {SelectedBaseLoader} 不兼容";
        if (SelectedLiteLoader is not null && kind is "Fabric" or "Quilt" or "LegacyFabric" or "NeoForge") return "与 LiteLoader 不兼容";
        if (IsOptiFineSelected && kind is "NeoForge" or "Quilt" or "LegacyFabric") return "与 OptiFine 不兼容";
        if (IsOptiFineSelected && kind == "Fabric" && !LoaderCompatibility.IsAtMost(Download.SelectedVersion?.Id ?? "", "1.20.4"))
            return "与 OptiFine 不兼容";
        if (SelectedBaseLoader is "无" or "OptiFine" || SelectedBaseLoader == kind) return null;
        if (kind == "OptiFine")
        {
            if (!CanAddOptiFine) return $"与 {SelectedBaseLoader} 不兼容";
            if (SelectedBaseLoader == "Fabric" && !LoaderCompatibility.IsAtMost(Download.SelectedVersion?.Id ?? "", "1.20.4"))
                return "与 Fabric 不兼容";
            return null;
        }
        return $"与 {SelectedBaseLoader} 不兼容";
    }

    internal bool CanChooseComponent(string kind, InstallComponentChoice choice)
    {
        if (IsApiKind(kind)) return IsFabricBase && choice.Api is not null &&
            WMCL.Modding.Fabric.ApiComponentService.IsCompatible(choice.Api, Download.SelectedVersion?.Id ?? "", SelectedBaseLoader);
        if (kind == "OptiFabric") return ShowOptiFabricOptions && choice.OptiFabric is not null &&
            WMCL.Modding.OptiFine.OptiFabricService.IsCompatible(choice.OptiFabric, Download.SelectedVersion?.Id ?? "");
        var families = new List<LoaderFamily> { Enum.Parse<LoaderFamily>(kind) };
        if (kind == "LiteLoader")
        {
            if (choice.LiteLoader is not { } lite || lite.Minecraft != Download.SelectedVersion?.Id) return false;
            if (SelectedBaseLoader is not ("无" or "OptiFine")) families.Add(Enum.Parse<LoaderFamily>(SelectedBaseLoader));
        }
        else if (SelectedLiteLoader is not null) families.Add(LoaderFamily.LiteLoader);
        var optifine = kind == "OptiFine" ? choice.OptiFine : IsOptiFineSelected ? Mods.SelectedOptiFineVersion : null;
        if (kind == "OptiFine" && SelectedBaseLoader is not ("无" or "OptiFine"))
            families.Add(Enum.Parse<LoaderFamily>(SelectedBaseLoader));
        else if (kind != "OptiFine" && optifine is not null) families.Add(LoaderFamily.OptiFine);
        var forge = kind == "Forge" ? choice.Version : SelectedBaseLoader == "Forge" ? Mods.SelectedForgelikeVersion : null;
        return LoaderCompatibility.Validate(families, Download.SelectedVersion?.Id ?? "", optifine?.ForgeVersion, forge) is null;
    }

    internal Task SelectComponentVersionAsync(string kind, InstallComponentChoice choice)
    {
        if (!CanEditInstall || ComponentIncompatibility(kind) is not null || !CanChooseComponent(kind, choice)) return Task.CompletedTask;
        CancelPendingComponent(kind);
        if (kind == "LiteLoader")
        {
            SetLiteLoaderSelection(choice.LiteLoader);
            return Task.CompletedTask;
        }
        if (IsApiKind(kind))
        {
            ApiSelection.SelectedVersion = choice.Api;
            InstallFabricApi = true;
            RefreshAdditionalCards();
            return Task.CompletedTask;
        }
        if (kind == "OptiFabric")
        {
            var selected = choice.OptiFabric;
            if (selected is { Sha1: null } && Download.SelectedVersion?.Id == _originalMinecraft &&
                OriginalFor(kind)?.Choice?.OptiFabric is { } original && original.Id == selected.Id &&
                original.FileName == selected.FileName && original.Size == selected.Size)
                selected = selected with { Sha1 = original.Sha1 };
            OptiFabricSelection.SelectedVersion = selected;
            InstallOptiFabric = true;
            RefreshAdditionalCards();
            return Task.CompletedTask;
        }
        var keepOptiFine = IsOptiFineSelected;
        var previousOptiFine = Mods.SelectedOptiFineVersion;
        if (kind == "OptiFine")
        {
            if (SelectedBaseLoader == "无") SelectedBaseLoader = "OptiFine";
            else if (CanAddOptiFine) InstallOptiFineWithBase = true;
            Mods.SelectedOptiFineVersion = choice.OptiFine;
        }
        else
        {
            SelectedBaseLoader = kind;
            InstallOptiFineWithBase = keepOptiFine && CanAddOptiFine;
            if (IsFabricBase)
            {
                Mods.SelectedLoader = choice.Version;
                InstallFabricApi = true;
            }
            else Mods.SelectedForgelikeVersion = choice.Version;
            if (keepOptiFine) Mods.SelectedOptiFineVersion = previousOptiFine;
        }
        NotifyComponentSummaries();
        NotifyInstallHints();
        return Task.CompletedTask;
    }

    internal void ClearComponent(string kind)
    {
        CancelPendingComponent(kind);
        if (!IsComponentSelected(kind)) return;
        if (kind == "LiteLoader")
        {
            SetLiteLoaderSelection(null);
            return;
        }
        if (IsApiKind(kind))
        {
            ClearApi();
            return;
        }
        if (kind == "OptiFabric")
        {
            InstallOptiFabric = false;
            RefreshAdditionalCards();
            return;
        }
        if (kind == "OptiFine")
        {
            if (IsOptiFineBase) SelectedBaseLoader = "无";
            InstallOptiFineWithBase = false;
        }
        else
        {
            var keepOptiFine = InstallOptiFineWithBase;
            if (IsFabricBase)
            {
                InstallFabricApi = false;
                ApiCard.IsExpanded = false;
            }
            SelectedBaseLoader = keepOptiFine ? "OptiFine" : "无";
        }
        NotifyComponentSummaries();
        NotifyInstallHints();
    }
}
