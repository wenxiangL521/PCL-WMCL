using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft.Models;
using WMCL.Minecraft;
using WMCL.Modding.Forgelike;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.Loaders;

namespace WMCL.App.Features.Downloads;

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

    /// <summary>可见性判断用的版本：优先已选版本，其次实例自身记录的基座版本。</summary>
    private VersionItem? CurrentGameVersion =>
        Download.SelectedVersion ?? (_instanceTarget is { } target
            ? new VersionItem { Id = target.Minecraft, IsInstalled = true, Type = "release", ReleaseTime = "" }
            : null);

    internal bool IsCurrentComponentCatalog(string root, string minecraft) =>
        _services.Settings.GameDirectory == root && Download.SelectedVersion?.Id == minecraft;

    internal bool IsComponentVisible(string kind)
    {
        if (CurrentGameVersion is not { } game) return false;
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

    private readonly ComponentRowActionService _rowActions;
    private CancellationTokenSource? _componentRowActionCancellation;

    [ObservableProperty]
    private bool _isSavingComponentInstaller;
    [ObservableProperty]
    private string _componentInstallerName = "";
    [ObservableProperty]
    private string _componentInstallerStatus = "";
    [ObservableProperty]
    private double _componentInstallerProgress;

    [RelayCommand]
    private void CancelComponentRowActions() => _componentRowActionCancellation?.Cancel();

    internal IReadOnlyList<InstallComponentAction> CreateComponentRowActions(string kind,
        InstallComponentChoice choice, Func<bool> current)
    {
        var minecraft = Download.SelectedVersion?.Id;
        if (minecraft is null) return [];
        var requests = new List<ComponentRowActionRequest>();
        switch (kind)
        {
            case "OptiFine" when choice.OptiFine is { } optifine:
                requests.Add(new("另存为", OptiFine: optifine));
                requests.Add(new("更新日志", "https://optifine.net/changelog?f=" + Uri.EscapeDataString(optifine.FileName)));
                break;
            case "Forge":
                requests.Add(new("另存为", Artifact: ComponentInstallerSources.Forge(minecraft,
                    choice.Forge ?? new ForgeVersionInfo(choice.Version, null))));
                requests.Add(new("更新日志", ComponentInstallerSources.ForgeChangelog(minecraft, choice.Version)));
                break;
            case "NeoForge":
                requests.Add(new("另存为", Artifact: ComponentInstallerSources.NeoForge(minecraft, choice.Version)));
                requests.Add(new("更新日志", ComponentInstallerSources.NeoForgeBase(minecraft, choice.Version) + "-changelog.txt"));
                break;
            case "LiteLoader" when choice.LiteLoader is { } lite:
                requests.Add(new("保存安装器", Artifact: ComponentInstallerSources.LiteLoader(lite)));
                if (!lite.IsLegacy) requests.Add(new("查看全部版本", "https://jenkins.liteloader.com/view/" + lite.Minecraft));
                break;
        }
        return requests.Select(request => new InstallComponentAction(request.Title,
            request.Title == "更新日志" ? InstallComponentAction.InfoIcon :
                request.Title == "查看全部版本" ? InstallComponentAction.ListIcon : InstallComponentAction.SaveIcon,
            request.Title == "更新日志" ? 1.05 : 1,
            new AsyncRelayCommand(() => ExecuteComponentRowActionAsync(request, current),
                () => CanEditInstall && current()))).ToArray();
    }

    private async Task ExecuteComponentRowActionAsync(ComponentRowActionRequest request, Func<bool> current)
    {
        if (!CanEditInstall || !current()) return;
        if (request.Url is not null)
        {
            try { await _rowActions.ExecuteAsync(request, () => CanEditInstall && current(), CancellationToken.None); }
            catch (Exception exception)
            {
                if (current())
                {
                    CombinedStatus = "打开失败：" + exception.Message;
                    TaskFinished?.Invoke(this, new TaskEntry(request.Title, CombinedStatus, 0, "失败", null));
                }
            }
            return;
        }
        if (_componentRowActionCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _componentRowActionCancellation = cancellation;
        var savingStatus = "正在保存 " + request.SuggestedFileName + "……";
        bool CanReport() => CanEditInstall && current() && CombinedStatus == savingStatus;
        ComponentInstallerName = request.SuggestedFileName;
        ComponentInstallerStatus = savingStatus;
        ComponentInstallerProgress = 0;
        IsSavingComponentInstaller = true;
        try
        {
            CombinedStatus = savingStatus;
            var progress = new Progress<JobProgress>(update =>
            {
                if (!ReferenceEquals(_componentRowActionCancellation, cancellation) || cancellation.IsCancellationRequested) return;
                ComponentInstallerProgress = (update.Percent ?? 0) * 100;
                ComponentInstallerStatus = update.Stage + (update.Detail is null ? "" : " · " + update.Detail);
            });
            var saved = await _rowActions.ExecuteAsync(request, () => CanEditInstall && current(), cancellation.Token, progress);
            ComponentInstallerProgress = saved ? 100 : ComponentInstallerProgress;
            ComponentInstallerStatus = saved ? "已保存 " + request.SuggestedFileName : "已取消保存安装器";
            if (CanReport()) CombinedStatus = ComponentInstallerStatus;
        }
        catch (OperationCanceledException)
        {
            ComponentInstallerStatus = "已取消保存安装器";
            if (CanReport()) CombinedStatus = ComponentInstallerStatus;
        }
        catch (Exception exception)
        {
            ComponentInstallerStatus = "保存安装器失败：" + exception.Message;
            if (CanReport()) CombinedStatus = ComponentInstallerStatus;
        }
        finally
        {
            _componentRowActionCancellation = null;
            IsSavingComponentInstaller = false;
        }
    }

    public LiteLoaderVersion? SelectedLiteLoader { get; private set; }
    private string? _liteLoaderSelectionRoot;

    private void SetLiteLoaderSelection(LiteLoaderVersion? version)
    {
        SelectedLiteLoader = version;
        _liteLoaderSelectionRoot = version is null ? null : _services.Settings.GameDirectory;
        OnPropertyChanged(nameof(SelectedLiteLoader));
        NotifyComponentSummaries();
        NotifyInstallHints();
    }

    private void ClearStaleLiteLoaderSelection()
    {
        if (SelectedLiteLoader is { } selected &&
            (selected.Minecraft != Download.SelectedVersion?.Id || _liteLoaderSelectionRoot != _services.Settings.GameDirectory))
            SetLiteLoaderSelection(null);
    }

    private async Task<LiteLoaderVersion?> RestoreLiteLoaderAsync(string root, string minecraft,
        VersionJson installed, InstallManifest? manifest)
    {
        var library = installed.Libraries.Where(item => item.Name.StartsWith("com.mumfrey:liteloader:", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name.Split(':').ElementAtOrDefault(2)).Distinct(StringComparer.Ordinal).ToArray();
        var declared = manifest?.Components.GetValueOrDefault("liteloader");
        if (library.Length == 0 && declared is null) return null;
        if (library.Length != 1 || string.IsNullOrWhiteSpace(library[0]) || declared is not null && declared != library[0])
            throw new InvalidDataException("LiteLoader 安装清单与实际支持库不一致，未更改实例。");
        var actualVersion = library[0];
        var gameArguments = installed.Arguments?.Game is { Count: > 0 } arguments
            ? arguments.SelectMany(argument => argument.Values).ToArray()
            : installed.MinecraftArguments?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        bool Matches(LiteLoaderVersion candidate) => candidate.Minecraft == minecraft && candidate.Version == actualVersion &&
            gameArguments.Zip(gameArguments.Skip(1)).Any(pair => pair.First == "--tweakClass" && pair.Second == candidate.TweakClass);

        if (manifest?.ComponentMetadata.TryGetValue("liteloader", out var metadata) == true)
        {
            var saved = metadata.Deserialize<LiteLoaderVersion>();
            if (saved is null || !Matches(saved))
                throw new InvalidDataException("保存的 LiteLoader 元数据与当前实例不一致，未更改实例。");
            LiteLoaderService.CreateProfile(saved);
            return saved with { Libraries = saved.Libraries.Clone() };
        }

        await LiteLoaderCard.PrepareAsync(root, minecraft);
        var exact = LiteLoaderCard.Entries.Select(entry => entry.Choice.LiteLoader)
            .FirstOrDefault(candidate => candidate is not null && Matches(candidate));
        if (exact is null)
            throw new InvalidDataException($"缺少已安装 LiteLoader {actualVersion} 的原始元数据，无法安全恢复；未改选最新版本或更改实例。");
        LiteLoaderService.CreateProfile(exact);
        return exact with { Libraries = exact.Libraries.Clone() };
    }
}
