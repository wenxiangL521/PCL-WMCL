using CommunityToolkit.Mvvm.ComponentModel;

namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
    private readonly Dictionary<string, InstallComponentChoice> _pendingComponents = [];
    private (string Root, string Minecraft)? _pendingMinecraft;
    private int _pendingComponentRequest;
    private bool _applyingPendingChoice;

    [ObservableProperty]
    private bool _isRechoosingComponents;

    internal Task PendingComponentSelectionTask { get; private set; } = Task.CompletedTask;

    partial void OnIsRechoosingComponentsChanged(bool value) => NotifyInstallAction();

    private void CapturePendingComponents()
    {
        if (!IsExistingInstance) return;
        foreach (var card in AllComponentCards)
            if (CurrentComponentChoice(card.Kind) is { } choice) _pendingComponents[card.Kind] = choice;
    }

    private void ClearPendingComponents()
    {
        ++_pendingComponentRequest;
        _pendingMinecraft = null;
        _pendingComponents.Clear();
        IsRechoosingComponents = false;
    }

    private void CancelPendingComponent(string kind)
    {
        if (_applyingPendingChoice) return;
        _pendingComponents.Remove(kind);
        if (kind is "Forge" or "NeoForge" || IsFabricFamily(kind))
            foreach (var pending in _pendingComponents.Keys.Where(pending =>
                         pending is "Forge" or "NeoForge" || IsFabricFamily(pending)).ToArray())
                _pendingComponents.Remove(pending);
        OnPropertyChanged(nameof(CanApplyInstall));
    }

    private void ChangeExistingMinecraft(VersionItem version)
    {
        if (Download.SelectedVersion?.Id == version.Id) return;
        CapturePendingComponents();
        ++_pendingComponentRequest;
        _pendingMinecraft = null;
        IsRechoosingComponents = false;
        _suspendPreparation = true;
        try
        {
            SelectedBaseLoader = "无";
            InstallOptiFineWithBase = false;
            InstallFabricApi = false;
            InstallOptiFabric = false;
            SetLiteLoaderSelection(null);
            Mods.SelectedLoader = null;
            Mods.SelectedForgelikeVersion = null;
            Mods.SelectedOptiFineVersion = null;
            if (_instanceTarget is { } target) _instanceTarget = target with { Minecraft = version.Id };
            _hasChosenVersion = true;
            Download.SelectedVersion = version;
        }
        finally { _suspendPreparation = false; }
        _selectionLoad = PrepareSelectedVersionAsync();
        _pendingMinecraft = (_services.Settings.GameDirectory, version.Id);
        ResumePendingComponents();
    }

    internal void ResumePendingComponents()
    {
        if (IsRechoosingComponents || _suspendPreparation || IsRestoringMinecraft || !CanEditInstall ||
            _pendingMinecraft is not { } key || !IsCurrentComponentCatalog(key.Root, key.Minecraft) ||
            _pendingComponents.Count == 0) return;
        PendingComponentSelectionTask = RechoosePendingComponentsAsync(key, _pendingComponentRequest);
    }

    private async Task RechoosePendingComponentsAsync((string Root, string Minecraft) key, int request)
    {
        IsRechoosingComponents = true;
        try
        {
            foreach (var kind in new[] { "Fabric", "Quilt", "LegacyFabric", "Forge", "NeoForge", "LiteLoader", "OptiFine",
                         "Fabric API", "Quilted Fabric API / QSL", "Legacy Fabric API", "OptiFabric" })
            {
                if (!_pendingComponents.TryGetValue(kind, out var original)) continue;
                if (kind == "OptiFine" && _pendingComponents.Keys.Any(pending =>
                        pending is "Forge" or "NeoForge" or "LiteLoader" || IsFabricFamily(pending)) ||
                    IsApiKind(kind) && _pendingComponents.Keys.Any(IsFabricFamily) ||
                    kind == "OptiFabric" && _pendingComponents.Keys.Any(pending => IsFabricFamily(pending) || pending == "OptiFine")) continue;
                var card = AllComponentCards.FirstOrDefault(candidate => candidate.Kind == kind);
                if (card is null) continue;
                if (!IsComponentVisible(kind) || ComponentIncompatibility(kind) is not null)
                {
                    _pendingComponents.Remove(kind);
                    continue;
                }
                if (card.IsAdditional)
                    await (kind == "OptiFabric" ? OptiFabricSelection.PrepareAsync(key.Minecraft) : ApiSelection.PrepareAsync(key.Minecraft, SelectedBaseLoader));
                else
                    await Task.WhenAll(card.PrepareAsync(key.Root, key.Minecraft), Mods.PrepareInstallComponentAsync(kind));
                if (request != _pendingComponentRequest || !IsCurrentComponentCatalog(key.Root, key.Minecraft)) return;
                if (!_pendingComponents.TryGetValue(kind, out var pending) || !ReferenceEquals(pending, original)) continue;
                if (card.Error.Length > 0)
                {
                    if (kind == "Forge" && card.Error == "无") _pendingComponents.Remove(kind);
                    continue;
                }
                _pendingComponents.Remove(kind);
                if (ComponentIncompatibility(kind) is not null) continue;
                var choices = card.Entries.Select(entry => entry.Choice).ToArray();
                var exact = choices.FirstOrDefault(choice => PendingVersionMatches(original, choice));
                var fallback = IsApiKind(kind) ? choices.OrderByDescending(choice => choice.Api?.DatePublished).FirstOrDefault()
                    : kind == "OptiFabric" ? choices.OrderByDescending(choice => choice.OptiFabric?.Published).FirstOrDefault()
                    : choices.FirstOrDefault();
                if ((exact ?? fallback) is not { } selected) continue;
                _applyingPendingChoice = true;
                try
                {
                    await SelectComponentVersionAsync(kind, selected);
                    if (IsFabricFamily(kind))
                    {
                        InstallFabricApi = _pendingComponents.Keys.Any(IsApiKind);
                        InstallOptiFabric = _pendingComponents.ContainsKey("OptiFabric");
                    }
                }
                finally { _applyingPendingChoice = false; }
            }
        }
        finally
        {
            if (request == _pendingComponentRequest)
            {
                NotifyComponentSummaries();
                NotifyInstallHints();
                IsRechoosingComponents = false;
            }
        }
    }

    private static bool PendingVersionMatches(InstallComponentChoice original, InstallComponentChoice candidate) =>
        original.Api is { } api ? candidate.Api is { } otherApi &&
            (otherApi.VersionNumber == api.VersionNumber || otherApi.Id == api.Id)
        : original.OptiFabric is { } optifabric ? candidate.OptiFabric is { } otherOptifabric &&
            (otherOptifabric.Version == optifabric.Version || otherOptifabric.Id == optifabric.Id)
        : original.OptiFine is { } optifine ? candidate.OptiFine is { } otherOptifine &&
            otherOptifine.Type == optifine.Type && otherOptifine.Patch == optifine.Patch
        : original.Version == candidate.Version;
}
