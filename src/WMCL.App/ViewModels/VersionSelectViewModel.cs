using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageSelect：左侧 Minecraft 文件夹列表，右侧按加载器分组的已安装版本。</summary>
public partial class VersionSelectViewModel : ViewModelBase
{
    private readonly LaunchViewModel _launch;
    private readonly AppServices _services;
    private readonly Action _close;
    private readonly Action _download;
    private readonly Action<string> _openPath;
    private readonly Func<string, ModpackImportWorkflow> _createImports;
    private CancellationTokenSource? _importCancellation;
    private bool _suppressFolderChange;
    private string? _pendingSelectionId;

    public VersionSelectViewModel(
        LaunchViewModel launch,
        AppServices services,
        Action close,
        Action download,
        Action<string>? openPath = null,
        Func<string, ModpackImportWorkflow>? createImports = null)
    {
        _launch = launch;
        _services = services;
        _close = close;
        _download = download;
        _openPath = openPath ?? ExternalLauncher.Open;
        _createImports = createImports ?? services.CreateModpackImports;
        SelectedVersion = launch.SelectedVersion is { IsInstalled: true } selected
            ? selected
            : Versions.FirstOrDefault();
        Versions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasVersions));
            OnPropertyChanged(nameof(CountText));
            RebuildGroups();
            RefreshSelection();
        };
        launch.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(LaunchViewModel.IsBusy) && !launch.IsBusy) RefreshSelection();
        };
        RebuildGroups();
        ReloadFolders();
    }

    public ObservableCollection<VersionItem> Versions => _launch.InstalledVersions;
    public ObservableCollection<VersionGroup> Groups { get; } = [];
    public ObservableCollection<GameFolderItem> Folders { get; } = [];
    public bool HasVersions => Versions.Count > 0;
    public bool HasGroups => Groups.Count > 0;
    public string EmptyTitle => ShowHidden ? "无隐藏版本" : "无可用版本";
    public string EmptyMessage => ShowHidden
        ? "没有版本被隐藏，你可以在版本设置的版本分类选项中隐藏版本。再次按下 F11 即可退出隐藏版本查看模式。"
        : "未找到任何已安装版本的游戏。请先下载任意版本，或从左侧切换到其他 Minecraft 文件夹。";
    public bool CanRemoveFolder => Folders.Count > 1;
    public string CountText => $"已安装 {Versions.Count} 个版本";
    public string GameDirectory => _services.Settings.GameDirectory;
    public string VersionsDirectory => Path.Combine(GameDirectory, "versions");

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string _importStatus = "";

    public string ImportButtonText => IsImporting ? "取消导入" : "导入整合包";

    partial void OnIsImportingChanged(bool value) => OnPropertyChanged(nameof(ImportButtonText));

    [RelayCommand]
    private void CancelImport() => _importCancellation?.Cancel();

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    [ObservableProperty]
    private GameFolderItem? _selectedFolder;

    [ObservableProperty]
    private bool _showHidden;

    public string HiddenToggleText => ShowHidden ? "返回普通版本" : "查看隐藏版本";

    partial void OnShowHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(HiddenToggleText));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        RebuildGroups();
    }

    partial void OnSelectedFolderChanged(GameFolderItem? value)
    {
        if (_suppressFolderChange || value is null) return;
        if (string.Equals(value.Path, _services.Settings.GameDirectory, StringComparison.OrdinalIgnoreCase))
            return;
        _services.SetGameDirectory(value.Path);
    }

    public void ReloadFolders()
    {
        _suppressFolderChange = true;
        try
        {
            Folders.Clear();
            foreach (var path in _services.Settings.GameFolders)
            {
                var full = WmclPaths.Expand(path);
                var defaultName = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
                var name = _services.Settings.GameFolderNames.TryGetValue(full, out var custom) &&
                           !string.IsNullOrWhiteSpace(custom)
                    ? custom
                    : defaultName is { Length: > 0 } ? defaultName : full;
                Folders.Add(new GameFolderItem(name, full));
            }
            SelectedFolder = Folders.FirstOrDefault(folder =>
                                 string.Equals(folder.Path, _services.Settings.GameDirectory,
                                     StringComparison.OrdinalIgnoreCase))
                             ?? Folders.FirstOrDefault();
            OnPropertyChanged(nameof(CanRemoveFolder));
            OnPropertyChanged(nameof(GameDirectory));
            OnPropertyChanged(nameof(VersionsDirectory));
        }
        finally
        {
            _suppressFolderChange = false;
        }
    }

    public async Task RefreshVersionsAsync() => await _launch.LoadAsync();

    public void OpenVersionFolder(VersionItem item)
    {
        try
        {
            var path = Path.Combine(_services.Settings.GameDirectory, "versions", item.Id);
            _openPath(path);
        }
        catch
        {
            // 打开失败不改变选择状态
        }
    }

    public void OpenGameFolder(GameFolderItem item)
    {
        try
        {
            Directory.CreateDirectory(item.Path);
            _openPath(item.Path);
        }
        catch
        {
            // 打开失败不改变选择状态
        }
    }

    public void RenameFolder(GameFolderItem item, string name)
    {
        _services.RenameGameFolder(item.Path, name);
        ReloadFolders();
    }

    public void RemoveFolder(GameFolderItem item)
    {
        if (Folders.Count <= 1) return;
        _services.RemoveGameFolder(item.Path);
        ReloadFolders();
    }

    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = WmclPaths.Expand(path);
        if (!Directory.Exists(Path.Combine(full, "versions")))
        {
            var nested = Path.Combine(full, ".minecraft");
            if (Directory.Exists(Path.Combine(nested, "versions")))
                full = nested;
        }
        _services.SetGameDirectory(full);
        ReloadFolders();
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        if (!CanRemoveFolder || SelectedFolder is null) return;
        _services.RemoveGameFolder(SelectedFolder.Path);
        ReloadFolders();
    }

    partial void OnSelectedVersionChanged(VersionItem? value) => _pendingSelectionId = null;

    internal void RemapSelection(InstanceChangeEventArgs change)
    {
        var id = change.Remap(SelectedVersion?.Id);
        if (id == SelectedVersion?.Id) return;
        if (id is null) SelectedVersion = null;
        else _pendingSelectionId = id;
    }

    public void RefreshSelection()
    {
        var matching = Versions.FirstOrDefault(version => version.Id == (_pendingSelectionId ?? SelectedVersion?.Id));
        if (matching is not null) SelectedVersion = matching;
        else if (!_launch.IsBusy)
        {
            _pendingSelectionId = null;
            SelectedVersion = _launch.SelectedVersion is { IsInstalled: true } selected && Versions.Contains(selected)
                ? selected : Versions.FirstOrDefault();
        }
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        var hidden = Versions.Where(v =>
            v.IsHidden || v.CardTypeOverride == VersionCardPlacement.Hidden).ToList();
        if (ShowHidden)
        {
            if (hidden.Count > 0) Groups.Add(new VersionGroup("隐藏的版本", hidden, true));
            OnPropertyChanged(nameof(HasGroups));
            return;
        }

        // PCL2 PageSelectRight: Star -> Error -> Fool -> API -> OriginalLike -> Rubbish.
        AddGroup("收藏夹", Versions.Where(v =>
            v.IsFavorite && !v.IsHidden && v.CardTypeOverride != VersionCardPlacement.Hidden), true);
        var remaining = Versions.Where(v =>
            !v.IsFavorite && !v.IsHidden && v.CardTypeOverride != VersionCardPlacement.Hidden).ToList();

        // PCL2 applies DisplayType after Star/Hidden filtering; favorites always stay in Star.
        var forcedApi = remaining.Where(v => v.CardTypeOverride == VersionCardPlacement.Api).ToList();
        var forcedRegular = remaining.Where(v => v.CardTypeOverride == VersionCardPlacement.Regular).ToList();
        var forcedUncommon = remaining.Where(v => v.CardTypeOverride == VersionCardPlacement.Uncommon).ToList();
        var forcedFool = remaining.Where(v => v.CardTypeOverride == VersionCardPlacement.Fool).ToList();
        remaining.RemoveAll(v => v.CardTypeOverride is >= VersionCardPlacement.Api and <= VersionCardPlacement.Fool);

        var errors = remaining.Where(v => v.IsErrorVersion).ToList();
        var fools = remaining.Except(errors).Where(v => v.IsFoolVersion).ToList();
        fools.AddRange(forcedFool);
        var available = remaining.Except(errors).Except(fools).ToList();
        var api = available.Where(v => v.IsApiVersion).ToList();
        api.AddRange(forcedApi);
        AddGroup(ApiGroupTitle(api), api, true);

        var working = available.Where(v => !v.IsApiVersion).ToList();
        var useful = new List<VersionItem>(forcedRegular);
        var rubbish = working.Where(v => v.IsOldVersion).ToList();
        rubbish.AddRange(forcedUncommon);
        working.RemoveAll(v => v.IsOldVersion);

        // 仅保留发布时间最新的原版/快照；若它恰好是快照，则快照进入常规版本。
        var largest = working.Where(v => !v.EffectiveHasOptiFine)
            .OrderByDescending(ReleaseDate).FirstOrDefault();
        if (largest is not null && largest.IsSnapshotVersion)
        {
            useful.Add(largest);
            working.Remove(largest);
        }
        rubbish.AddRange(working.Where(v => v.IsSnapshotVersion));
        working.RemoveAll(v => v.IsSnapshotVersion);

        // 每个 Drop 只保留一个 OptiFine（按构建号）或一个最新原版。
        foreach (var drop in working.Where(v => Drop(v) is not null).GroupBy(v => Drop(v)!.Value))
        {
            var optifine = drop.Where(v => v.EffectiveHasOptiFine)
                .OrderByDescending(OptiFineCode).ThenByDescending(ReleaseDate).FirstOrDefault();
            var original = drop.Where(v => !v.EffectiveHasOptiFine)
                .OrderByDescending(ReleaseDate).FirstOrDefault();
            if (optifine is not null)
            {
                useful.Add(optifine);
                working.Remove(optifine);
                if (original is not null)
                {
                    working.Remove(original);
                    rubbish.Add(original);
                }
            }
            else if (original is not null)
            {
                useful.Add(original);
                working.Remove(original);
            }
        }

        // 无法解析 Drop 的基座、未被选中的同代版本都归入不常用，而不是猜测成快照。
        rubbish.AddRange(working);
        AddGroup("常规版本", useful, true);
        AddGroup("不常用版本", rubbish, false);
        AddGroup("愚人节版本", fools, false);
        AddGroup("错误的版本", errors, false, sort: false);
        if (Groups.Count == 1) Groups[0].IsExpanded = true;
        OnPropertyChanged(nameof(HasGroups));
    }

    private void AddGroup(string title, IEnumerable<VersionItem> versions, bool expanded, bool sort = true)
    {
        var items = sort ? OrderPcl(versions).ToList() : versions.ToList();
        if (items.Count > 0) Groups.Add(new VersionGroup(title, items, expanded));
    }

    private static string ApiGroupTitle(IReadOnlyList<VersionItem> versions)
    {
        var loaders = versions.Aggregate(VersionLoaderKind.None, (result, version) => result | version.EffectiveLoaders);
        var count = 0;
        if (loaders.HasFlag(VersionLoaderKind.Fabric)) count++;
        if (loaders.HasFlag(VersionLoaderKind.Forge)) count++;
        if (loaders.HasFlag(VersionLoaderKind.NeoForge)) count++;
        if (loaders.HasFlag(VersionLoaderKind.LiteLoader)) count++;
        if (loaders.HasFlag(VersionLoaderKind.Cleanroom)) count++;
        if (loaders.HasFlag(VersionLoaderKind.LabyMod)) count++;
        return count > 1 ? "可安装 Mod" :
            loaders.HasFlag(VersionLoaderKind.Cleanroom) ? "Cleanroom 版本" :
            loaders.HasFlag(VersionLoaderKind.LabyMod) ? "LabyMod 版本" :
            loaders.HasFlag(VersionLoaderKind.Forge) ? "Forge 版本" :
            loaders.HasFlag(VersionLoaderKind.NeoForge) ? "NeoForge 版本" :
            loaders.HasFlag(VersionLoaderKind.LiteLoader) ? "LiteLoader 版本" :
            "Fabric 版本";
    }

    private static IOrderedEnumerable<VersionItem> OrderPcl(IEnumerable<VersionItem> versions) =>
        versions.OrderBy(version => version, PclVersionComparer.Instance);

    private static int? Drop(VersionItem version)
    {
        if (!Version.TryParse(version.BaseGameVersion ?? version.Id, out var parsed)) return null;
        return parsed.Major == 1 ? parsed.Minor * 10 : parsed.Major >= 26 ? parsed.Major * 10 + parsed.Minor : null;
    }

    private static int OptiFineCode(VersionItem version)
    {
        var text = version.OptiFineVersion;
        if (string.IsNullOrWhiteSpace(text) || text == "未知版本" || text.Length == 0) return 0;
        var result = char.ToUpperInvariant(text[0]) - 'A' + 1;
        var mainMatch = Regex.Match(text[1..], @"\d+", RegexOptions.CultureInvariant);
        result = result * 100 + (mainMatch.Success && int.TryParse(mainMatch.Value, out var main) ? main : 0);
        result *= 100;
        var preview = Regex.Match(text, @"(?:pre|beta)\d*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!preview.Success) return result + 99;
        var sequence = Regex.Match(preview.Value, @"\d+", RegexOptions.CultureInvariant);
        return result +
               (preview.Value.StartsWith("pre", StringComparison.OrdinalIgnoreCase) ? 50 : 0) +
               (sequence.Success && int.TryParse(sequence.Value, out var value) ? value : 1);
    }

    private static int ForgelikeCode(VersionItem version)
    {
        var text = version.ForgeVersion ?? version.NeoForgeVersion;
        if (string.IsNullOrWhiteSpace(text) || text == "未知版本") return 0;
        var segments = Regex.Matches(text, @"\d+", RegexOptions.CultureInvariant)
            .Select(match => int.TryParse(match.Value, out var value) ? value : 0).ToArray();
        return segments.Length switch
        {
            >= 4 => segments[0] * 1000000 + segments[1] * 10000 + segments[3],
            3 => segments[0] * 1000000 + segments[1] * 10000 + segments[2],
            2 => segments[0] * 1000000 + segments[1] * 10000,
            1 => segments[0] * 1000000,
            _ => 0
        };
    }

    private static DateTimeOffset ReleaseDate(VersionItem version) =>
        DateTimeOffset.TryParse(version.ReleaseTime, out var date) ? date : DateTimeOffset.MinValue;

    private sealed class PclVersionComparer : IComparer<VersionItem>
    {
        public static PclVersionComparer Instance { get; } = new();

        public int Compare(VersionItem? left, VersionItem? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;
            if (right is null) return -1;

            var leftDate = ReleaseDate(left);
            var rightDate = ReleaseDate(right);
            if ((leftDate.Year >= 2000 || rightDate.Year >= 2000) && leftDate != rightDate)
                return rightDate.CompareTo(leftDate);

            var flag = CompareFlag(left.EffectiveLoaders.HasFlag(VersionLoaderKind.Fabric),
                right.EffectiveLoaders.HasFlag(VersionLoaderKind.Fabric));
            if (flag != 0) return flag;
            flag = CompareFlag(left.EffectiveLoaders.HasFlag(VersionLoaderKind.NeoForge),
                right.EffectiveLoaders.HasFlag(VersionLoaderKind.NeoForge));
            if (flag != 0) return flag;
            flag = CompareFlag(left.EffectiveLoaders.HasFlag(VersionLoaderKind.Forge),
                right.EffectiveLoaders.HasFlag(VersionLoaderKind.Forge));
            if (flag != 0) return flag;
            flag = CompareFlag(left.EffectiveHasOptiFine, right.EffectiveHasOptiFine);
            if (flag != 0) return flag;
            flag = CompareFlag(left.EffectiveLoaders.HasFlag(VersionLoaderKind.LiteLoader),
                right.EffectiveLoaders.HasFlag(VersionLoaderKind.LiteLoader));
            if (flag != 0) return flag;

            var leftCode = ForgelikeCode(left);
            var rightCode = ForgelikeCode(right);
            if (leftCode == 0) leftCode = OptiFineCode(left);
            if (rightCode == 0) rightCode = OptiFineCode(right);
            if (leftCode != rightCode) return rightCode.CompareTo(leftCode);

            return string.Compare(right.Id, left.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareFlag(bool left, bool right) =>
            right.CompareTo(left);
    }

    public event EventHandler? InstanceInstalled;

    public async Task ImportLocalAsync(string path)
    {
        if (IsImporting || string.IsNullOrWhiteSpace(path)) return;
        var root = GameDirectory;
        using var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;
        IsImporting = true;
        ImportStatus = "正在导入本地整合包…";
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "导入本地整合包");
            var imports = _createImports(root);
            var progress = new Progress<WMCL.Core.Jobs.JobProgress>(update =>
            {
                if (_importCancellation != cancellation || GameDirectory != root || cancellation.IsCancellationRequested) return;
                ImportStatus = update.Percent is null ? update.Stage : $"{update.Stage} {update.Percent * 100:0}%";
            });
            var id = await imports.InstallLocalAsync(path, null, true, progress, cancellation.Token);
            if (GameDirectory == root) ImportStatus = "已导入实例：" + id;
            InstanceInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (GameDirectory == root) ImportStatus = "已取消导入";
        }
        catch (Exception ex)
        {
            if (GameDirectory == root) ImportStatus = "导入失败：" + ex.Message;
        }
        finally
        {
            _importCancellation = null;
            IsImporting = false;
        }
    }

    public void ToggleHidden(VersionItem item)
    {
        if (item.CardTypeOverride == VersionCardPlacement.Hidden)
        {
            // “从版本列表中隐藏”是本实例设置的一部分；在 F11 页面点显示时同步落盘回“自动”。
            item.CardTypeOverride = VersionCardPlacement.Auto;
            item.IsHidden = false;
            var directory = Path.Combine(GameDirectory, "versions", item.Id);
            var settings = InstanceSettingsStore.Load(directory);
            if (settings.VersionCardType == VersionCardPlacement.Hidden)
            {
                settings.VersionCardType = VersionCardPlacement.Auto;
                InstanceSettingsStore.Save(directory, settings);
            }
        }
        else
        {
            item.IsHidden = !item.IsHidden;
        }
        var hidden = _services.GetHiddenVersions(_services.Settings.GameDirectory);
        hidden.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (item.IsHidden) hidden.Add(item.Id);
        _services.SaveSettings();
        RebuildGroups();
    }

    [RelayCommand]
    private void ToggleHiddenVersions() => ShowHidden = !ShowHidden;

    public void ToggleFavorite(VersionItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        var favorites = _services.GetFavoriteVersions(_services.Settings.GameDirectory);
        favorites.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (item.IsFavorite) favorites.Add(item.Id);
        _services.SaveSettings();
        RebuildGroups();
    }

    public event EventHandler<string>? VersionSettingsRequested;

    public void ActivateVersion(VersionItem item)
    {
        if (_launch.IsBusy || !item.IsInstalled || !Versions.Contains(item)) return;
        if (item.Category == "error")
        {
            OpenVersionFolder(item);
            return;
        }
        SelectedVersion = item;
        ConfirmCommand.Execute(null);
    }

    public void OpenVersionSettings(VersionItem item)
    {
        if (_launch.IsBusy || !Versions.Contains(item)) return;
        if (item.Category == "error") OpenVersionFolder(item);
        else VersionSettingsRequested?.Invoke(this, item.Id);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (_launch.IsBusy || SelectedVersion is not { IsInstalled: true, Category: not "error" } version ||
            !Versions.Contains(version)) return;
        _launch.SelectedVersion = SelectedVersion;
        _close();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(VersionsDirectory);
            _openPath(VersionsDirectory);
        }
        catch
        {
            // 打开失败不改变选择状态
        }
    }

    [RelayCommand]
    private void Download()
    {
        _close();
        _download();
    }
}
