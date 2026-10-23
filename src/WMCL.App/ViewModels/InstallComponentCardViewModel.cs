using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Modrinth;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.Forgelike;

namespace WMCL.App.ViewModels;

public sealed record InstallComponentChoice(string Version, string Title, string Info, OptiFineVersion? OptiFine = null,
    ModrinthVersion? Api = null, OptiFabricFile? OptiFabric = null, bool IsRecommended = false, LiteLoaderVersion? LiteLoader = null,
    ForgeVersionInfo? Forge = null);

public sealed class InstallComponentEntry(InstallComponentChoice choice, Bitmap icon, IRelayCommand selectCommand, string? summary = null,
    IReadOnlyList<InstallComponentAction>? actions = null)
{
    public InstallComponentChoice Choice { get; } = choice;
    public string Title => Choice.Title;
    public string Summary => summary ?? Choice.Info;
    public Bitmap IconImage { get; } = icon;
    public IRelayCommand SelectCommand { get; } = selectCommand;
    public IReadOnlyList<InstallComponentAction> Actions { get; } = actions ?? [];
}

public sealed record InstallComponentSeparator(int Count)
{
    public string Title => $"全部版本 ({Count})";
}

public sealed partial class InstallComponentCardViewModel : ViewModelBase
{
    private readonly InstallViewModel _owner;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<InstallComponentChoice>>> _load;
    private IReadOnlyList<InstallComponentChoice> _choices = [];
    private (string Root, string Minecraft, string Kind)? _key;
    private Task? _loading;
    private CancellationTokenSource? _loadCts;
    private int _request;

    public InstallComponentCardViewModel(InstallViewModel owner, string kind,
        Func<string, string, CancellationToken, Task<IReadOnlyList<InstallComponentChoice>>> load)
    {
        _owner = owner;
        _kind = kind;
        _load = load;
    }

    [ObservableProperty]
    private string _kind;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _error = "";

    public ObservableCollection<InstallComponentEntry> Entries { get; } = [];
    public ObservableCollection<object> DisplayItems { get; } = [];
    public double ContentHeight => DisplayItems.Sum(item => item is InstallComponentSeparator ? 35 : 42) + 15;
    public bool IsSelected => _owner.IsComponentSelected(Kind);
    public bool IsFavorite => _owner.IsFavoriteComponent(Kind is "Quilt" or "LegacyFabric" ? "Fabric" : Kind);
    public bool CanChangeKind => !_owner.IsFabricSelected;
    public bool IsFabricFamily => Kind is "Fabric" or "Quilt" or "LegacyFabric";
    public bool IsAdditional => Kind is "Fabric API" or "Quilted Fabric API / QSL" or "Legacy Fabric API" or "OptiFabric";
    public bool IsVisible => _owner.IsComponentVisible(Kind) || HasComparison && HasOriginalComponent;
    public string? Incompatibility => _owner.ComponentIncompatibility(Kind);
    public double SummaryIconSize => Kind == "LiteLoader" ? 20 : 18;
    public Thickness SummaryIconMargin => Kind == "LiteLoader" ? new(-1, 0, 6, 0) : new(0, 0, 7, 0);
    public bool CanExpand => _owner.IsComponentVisible(Kind) && !IsLoading && Error.Length == 0 && Incompatibility is null && Entries.Count > 0;
    public bool HasOriginalComponent => _owner.OriginalFor(Kind) is not null;
    public bool HasComparison => _owner.HasComponentBaseline &&
        !_owner.IsOriginalComponentSelection(Kind) &&
        !(!IsSelected && (IsLoading || Error.Length > 0));
    public bool ShowCurrentSummary => !HasComparison;
    public string OriginalSummary => _owner.OriginalFor(Kind)?.Summary ?? "无";
    public string ComparisonSummary => IsSelected ? Summary : Summary == "可以添加" ? "移除"
        : "移除（" + (Summary == "无" ? "无可用版本" : Summary) + "）";
    public bool CanRestore => HasComparison && _owner.CanRestoreComponent(this);
    public string ClearText => HasOriginalComponent ? "移除" : "取消安装";
    public Thickness RestoreMargin => new(0, 5, IsSelected ? 60 : 32, 0);
    public Thickness ComparisonMargin => new(132, 11, CanRestore && IsSelected ? 110 : 80, 0);
    public Thickness OriginalIconMargin => HasOriginalComponent ? new(0, 0, 7, 0) : new(0);
    public Thickness CurrentIconMargin => IsSelected ? new(0, 0, 7, 0) : new(0);
    public string Summary => IsSelected ? _owner.ComponentSelectedSummary(Kind)
        : Incompatibility ?? (IsLoading ? "获取中……" : Error.Length > 0 ? Error == "无" ? "无" : "获取失败：" + Error : Entries.Count == 0 ? "无" : "可以添加");
    public Bitmap IconImage => VersionCatalogEntry.LoadIcon($"avares://WMCL.App/Assets/Blocks/{Kind switch
    {
        "Forge" => "Anvil",
        "NeoForge" => "NeoForge",
        "OptiFine" => "GrassPath",
        "OptiFabric" => "OptiFabric",
        "LiteLoader" => "Egg",
        _ => "Fabric"
    }}.png");

    public Task PrepareAsync(string root, string minecraft, bool forceReload = false,
        CancellationToken ct = default)
    {
        var key = (root, minecraft, Kind);
        if (!forceReload && _key == key && _loading is not null) return _loading;
        _key = key;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        return _loading = LoadAsync(key, ++_request, _loadCts);
    }

    internal void Cancel()
    {
        _loadCts?.Cancel();
    }

    private async Task LoadAsync((string Root, string Minecraft, string Kind) key, int request,
        CancellationTokenSource cancellation)
    {
        IsExpanded = false;
        _choices = [];
        Entries.Clear();
        DisplayItems.Clear();
        Error = "";
        IsLoading = true;
        RefreshState();
        try
        {
            var choices = await _load(key.Kind, key.Minecraft, cancellation.Token);
            if (request != _request || !_owner.IsCurrentComponentCatalog(key.Root, key.Minecraft)) return;
            _choices = choices;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (request == _request) Error = "";
        }
        catch (Exception exception)
        {
            if (request == _request && _owner.IsCurrentComponentCatalog(key.Root, key.Minecraft)) Error = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cancellation)) _loadCts = null;
            cancellation.Dispose();
            if (request == _request)
            {
                IsLoading = false;
                RefreshState();
            }
        }
    }

    internal void UpdateExternalChoices(string root, string minecraft, IReadOnlyList<InstallComponentChoice> choices, bool loading, string error)
    {
        var key = (root, minecraft, Kind);
        if (_key != key) IsExpanded = false;
        _key = key;
        _choices = choices;
        IsLoading = loading;
        Error = error;
        RefreshState();
    }

    internal void RefreshState()
    {
        var available = _choices.Where(choice => _owner.CanChooseComponent(Kind, choice)).ToArray();
        if (!Entries.Select(entry => entry.Choice).SequenceEqual(available))
        {
            Entries.Clear();
            DisplayItems.Clear();
            foreach (var choice in available)
            {
                Entries.Add(CreateEntry(choice));
            }
            if (available.Length > 1 && Kind == "Forge")
            {
                var recommended = available.LastOrDefault(choice => choice.IsRecommended);
                if (recommended is not null) DisplayItems.Add(CreateEntry(recommended, "推荐版"));
                if (!ReferenceEquals(available[0], recommended)) DisplayItems.Add(CreateEntry(available[0], "最新版"));
                DisplayItems.Add(new InstallComponentSeparator(available.Length));
            }
            else if (available.Length > 1 && Kind == "NeoForge")
            {
                InstallComponentChoice? latestBeta = null;
                foreach (var choice in available)
                {
                    if (choice.Version.Contains("beta", StringComparison.OrdinalIgnoreCase)) latestBeta ??= choice;
                    else
                    {
                        DisplayItems.Add(CreateEntry(choice, "最新稳定版", replaceInfo: true));
                        break;
                    }
                }
                if (latestBeta is not null) DisplayItems.Add(CreateEntry(latestBeta, "最新测试版", replaceInfo: true));
                DisplayItems.Add(new InstallComponentSeparator(available.Length));
            }
            foreach (var entry in Entries) DisplayItems.Add(entry);
        }
        if (!CanExpand) IsExpanded = false;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(CanChangeKind));
        OnPropertyChanged(nameof(CanExpand));
        OnPropertyChanged(nameof(Summary));
        NotifyComparisonState();
    }

    internal void NotifyComparisonState()
    {
        OnPropertyChanged(nameof(HasOriginalComponent));
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(ShowCurrentSummary));
        OnPropertyChanged(nameof(OriginalSummary));
        OnPropertyChanged(nameof(ComparisonSummary));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(ClearText));
        OnPropertyChanged(nameof(RestoreMargin));
        OnPropertyChanged(nameof(ComparisonMargin));
        OnPropertyChanged(nameof(OriginalIconMargin));
        OnPropertyChanged(nameof(CurrentIconMargin));
        RestoreCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task RestoreAsync() => _owner.RestoreComponentAsync(this);

    private InstallComponentEntry CreateEntry(InstallComponentChoice choice, string? prefix = null, bool replaceInfo = false)
    {
        InstallComponentEntry? entry = null;
        var key = _key;
        entry = new InstallComponentEntry(choice, IconImage, new AsyncRelayCommand(async () =>
        {
            if (entry is null || !DisplayItems.Contains(entry) || _key is not { } key ||
                !_owner.IsCurrentComponentCatalog(key.Root, key.Minecraft) || !CanExpand) return;
            IsExpanded = false;
            await _owner.SelectComponentVersionAsync(Kind, choice);
        }), prefix is null ? null : replaceInfo || choice.Info.Length == 0 ? prefix : prefix + "，" + choice.Info,
            _owner.CreateComponentRowActions(Kind, choice, () => entry is not null && DisplayItems.Contains(entry) &&
                key is { } current && _key == key && _owner.IsCurrentComponentCatalog(current.Root, current.Minecraft)));
        return entry;
    }

    partial void OnKindChanged(string value)
    {
        OnPropertyChanged(nameof(IconImage));
        OnPropertyChanged(nameof(SummaryIconSize));
        OnPropertyChanged(nameof(SummaryIconMargin));
        OnPropertyChanged(nameof(IsFabricFamily));
        _key = null;
        if (!IsAdditional) _owner.PrepareComponentCards();
    }

    [RelayCommand]
    private void Toggle()
    {
        if (_owner.CanEditInstall && (IsExpanded || CanExpand)) IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void Clear()
    {
        if (!_owner.CanEditInstall) return;
        _owner.ClearComponent(Kind);
        IsExpanded = false;
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_key is not { } key || !_owner.CanEditInstall || IsLoading) return;
        if (IsAdditional)
        {
            await _owner.RetryAdditionalComponentAsync(Kind);
            _owner.ResumePendingComponents();
            return;
        }
        _loading = null;
        await PrepareAsync(key.Root, key.Minecraft);
        _owner.ResumePendingComponents();
    }

    [RelayCommand]
    private void ToggleFavorite() => _owner.ToggleComponentFavorite(Kind is "Quilt" or "LegacyFabric" ? "Fabric" : Kind);

    [RelayCommand]
    private void ChangeKind(string kind)
    {
        if (CanChangeKind && _owner.CanEditInstall && kind is "Fabric" or "Quilt" or "LegacyFabric") Kind = kind;
    }
}
