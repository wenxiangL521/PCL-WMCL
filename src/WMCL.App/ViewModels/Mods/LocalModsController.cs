using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WMCL.App.ViewModels.Mods;
using WMCL.Modding.Local;

namespace WMCL.App.ViewModels.Mods;

/// <summary>
/// 本地 Mod 管理域：扫描、过滤、启用/禁用、删除。
/// 从 ModsViewModel 整体搬出，VM 只保留 XAML 绑定转发。
/// </summary>
public sealed partial class LocalModsController : ObservableObject
{
    public ObservableCollection<LocalMod> Mods { get; } = [];
    public ObservableCollection<LocalMod> View { get; } = [];

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private string _status = "该实例 mods 目录为空";

    [ObservableProperty]
    private LocalMod? _selected;

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>同步扫描目录并重建视图。</summary>
    public void Scan(string directory) => Apply(LocalModService.List(directory));

    /// <summary>后台扫描（逐 jar 打开，避免卡 UI）。</summary>
    public async Task ScanAsync(string directory)
    {
        var mods = await Task.Run(() => LocalModService.List(directory)).ConfigureAwait(true);
        Apply(mods);
    }

    public void Apply(IReadOnlyList<LocalMod> mods)
    {
        Mods.Clear();
        foreach (var mod in mods) Mods.Add(mod);
        ApplyFilter();
        Selected = View.FirstOrDefault();
    }

    public void ApplyFilter()
    {
        var previous = Selected?.FilePath;
        var matches = LocalModFilter.Apply(Mods, Filter).ToArray();
        View.Clear();
        foreach (var mod in matches) View.Add(mod);
        Selected = matches.FirstOrDefault(mod => mod.FilePath == previous) ?? matches.FirstOrDefault();
        var enabled = Mods.Count(mod => mod.IsEnabled);
        Status = LocalModFilter.Describe(Mods.Count, matches.Length, enabled);
    }

    /// <summary>批量启用/禁用；返回实际处理的个数。</summary>
    public int SetEnabled(string directory, IReadOnlyList<LocalMod> selection, bool enabled)
    {
        var valid = Validate(directory, selection);
        foreach (var mod in valid) LocalModService.SetEnabled(directory, mod, enabled);
        if (valid.Length > 0)
        {
            Scan(directory);
            Status = $"已{(enabled ? "启用" : "禁用")} {valid.Length} 个 Mod";
        }
        return valid.Length;
    }

    /// <summary>删除选中的 Mod；返回实际删除的个数。</summary>
    public int Delete(string directory, IReadOnlyList<LocalMod> selection)
    {
        var valid = Validate(directory, selection);
        foreach (var mod in valid) LocalModService.Delete(mod);
        if (valid.Length > 0)
        {
            Scan(directory);
            Status = $"已删除 {valid.Length} 个 Mod";
        }
        return valid.Length;
    }

    /// <summary>只保留仍位于当前目录下的 Mod，防止跨实例误操作。</summary>
    public LocalMod[] Validate(string directory, IReadOnlyList<LocalMod> selection) =>
        selection
            .Where(mod => string.Equals(Path.GetDirectoryName(mod.FilePath), directory, StringComparison.Ordinal))
            .ToArray();
}
