using System.Text.Json;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
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
