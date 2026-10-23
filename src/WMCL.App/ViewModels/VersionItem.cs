using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

[Flags]
public enum VersionLoaderKind
{
    None = 0,
    Fabric = 1,
    Forge = 2,
    NeoForge = 4,
    LiteLoader = 8,
    Cleanroom = 16,
    LabyMod = 32
}

public static class VersionCardPlacement
{
    public const int Auto = 0;
    public const int Hidden = 1;
    public const int Api = 2;
    public const int Regular = 3;
    public const int Uncommon = 4;
    public const int Fool = 5;
}

public sealed partial class VersionItem : ObservableObject
{
    private static readonly ConcurrentDictionary<string, Bitmap> DefaultIcons = new();
    private static readonly Regex OldVersionPattern = new(
        @"^(?:[ab]\d+(?:\.\d+)+[a-z]?|c\d+\.[0-9a-z_]+|(?:rd|inf)-\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SnapshotVersionPattern = new(
        @"^(?:\d{2}w\d{2}[a-z]|(?:1|[2-9]\d)\.\d+(?:\.\d+)?-(?:pre|rc|snapshot-?)\d*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public required string Id { get; init; }
    public string Type { get; init; } = "release";
    public string? ReleaseTime { get; init; }
    public string? BaseGameVersion { get; init; }
    public bool IsBaseGameVersionReliable { get; init; }
    public MinecraftVersionIdentitySource IdentitySource { get; init; } = MinecraftVersionIdentitySource.Unknown;
    public string? OptiFineVersion { get; init; }
    public bool HasOptiFine { get; init; }
    public VersionLoaderKind Loaders { get; init; }
    public string? ForgeVersion { get; init; }
    public string? NeoForgeVersion { get; init; }
    public string? FabricVersion { get; init; }
    public string? LiteLoaderVersion { get; init; }
    public string? CleanroomVersion { get; init; }
    public string? LabyModVersion { get; init; }
    public bool IsLatest { get; init; }

    [ObservableProperty]
    private int _cardTypeOverride;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isHidden;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private Bitmap? _logo;

    public bool HasLogo => Logo is not null;

    public bool IsErrorVersion => Category.Equals("error", StringComparison.OrdinalIgnoreCase);

    public bool IsOldVersion =>
        Type is "old_alpha" or "old_beta" ||
        string.Equals(BaseGameVersion, "Old", StringComparison.OrdinalIgnoreCase) ||
        OldVersionPattern.IsMatch(BaseGameVersion ?? "") ||
        (DateTimeOffset.TryParse(ReleaseTime, out var date) && date.Year is > 2000 and < 2013);

    public bool IsSnapshotVersion =>
        Type is "snapshot" or "pending" ||
        SnapshotVersionPattern.IsMatch(BaseGameVersion ?? "");

    public bool IsFoolVersion =>
        Category.Equals("fool", StringComparison.OrdinalIgnoreCase) ||
        Type is "fool" or "special" ||
        IsKnownFoolName(BaseGameVersion ?? Id) ||
        (Type == "snapshot" && IsFoolDate(ReleaseTime));

    public VersionLoaderKind EffectiveLoaders
    {
        get
        {
            if (Loaders != VersionLoaderKind.None) return Loaders;
            return Category.ToLowerInvariant() switch
            {
                "fabric" => VersionLoaderKind.Fabric,
                "quilt" => VersionLoaderKind.Fabric,
                "forge" => VersionLoaderKind.Forge,
                "neoforge" => VersionLoaderKind.NeoForge,
                "liteloader" => VersionLoaderKind.LiteLoader,
                _ => VersionLoaderKind.None
            };
        }
    }

    // PCL2 classifies Old/Error/Fool before API and then never treats them as API cards.
    public bool IsApiVersion =>
        !IsOldVersion && !IsFoolVersion && !IsErrorVersion &&
        EffectiveLoaders != VersionLoaderKind.None;

    public int LoaderKindCount
    {
        get
        {
            var loaders = EffectiveLoaders;
            var count = 0;
            if (loaders.HasFlag(VersionLoaderKind.Fabric)) count++;
            if (loaders.HasFlag(VersionLoaderKind.Forge)) count++;
            if (loaders.HasFlag(VersionLoaderKind.NeoForge)) count++;
            if (loaders.HasFlag(VersionLoaderKind.LiteLoader)) count++;
            if (loaders.HasFlag(VersionLoaderKind.Cleanroom)) count++;
            if (loaders.HasFlag(VersionLoaderKind.LabyMod)) count++;
            return count;
        }
    }

    public bool EffectiveHasOptiFine =>
        HasOptiFine || Category.Equals("optifine", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(OptiFineVersion);

    public string VersionDisplayText
    {
        get
        {
            if (IsErrorVersion)
                return string.IsNullOrWhiteSpace(Description) ? "无法读取版本配置" : Description;
            if (IsFoolVersion) return (FoolDisplayName() + LoaderDisplayText()).Replace("_", "-");
            if (IsOldVersion) return ("远古版本" + LoaderDisplayText()).Replace("_", "-");

            var baseVersion = BaseGameVersion?.Replace("_unobfuscated", "") ?? "";
            var reliable = IsBaseGameVersionReliable && !string.IsNullOrWhiteSpace(baseVersion);
            var info = baseVersion.Contains("pre", StringComparison.OrdinalIgnoreCase)
                ? $"预发布版 {baseVersion}"
                : baseVersion.Contains("rc", StringComparison.OrdinalIgnoreCase)
                    ? $"发布候选 {baseVersion}"
                    : baseVersion.Contains("experimental", StringComparison.OrdinalIgnoreCase)
                        ? $"实验性快照{baseVersion}"
                        : baseVersion == "pending"
                            ? "实验性快照"
                            : IsSnapshotVersion
                                ? reliable ? $"快照版 {baseVersion.Replace("-snapshot", "")}" : "快照版"
                                : reliable ? $"正式版 {baseVersion}" : "正式版";
            return (info + LoaderDisplayText()).Replace("_", "-");
        }
    }

    public string InfoText => HasCustomDescription ? Description : VersionDisplayText;

    public string TitleSuffix => HasCustomDescription ? $"  |  {VersionDisplayText}" : "";

    public string FavoriteTooltip => IsFavorite ? "取消收藏" : "收藏";

    public string TypeText => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "旧测试版",
        "old_alpha" => "旧预览版",
        _ => Type
    };

    public string Subtitle => $"{TypeText} · {FormatTime(ReleaseTime)}";

    public string StatusText => IsInstalled ? "已安装" : "";
    public string FavoriteMark => IsFavorite ? "★" : "";
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string HiddenGlyph => IsHidden ? "显示" : "隐藏";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasCustomDescription => !string.IsNullOrWhiteSpace(Description);

    public Bitmap IconImage => Logo ?? DefaultIcons.GetOrAdd(DefaultIconName, LoadDefaultIcon);

    private string DefaultIconName =>
        IsErrorVersion ? "RedstoneBlock" :
        IsFoolVersion ? "GoldBlock" :
        IsOldVersion ? "CobbleStone" :
        EffectiveLoaders.HasFlag(VersionLoaderKind.Fabric) ? "Fabric" :
        EffectiveLoaders.HasFlag(VersionLoaderKind.Forge) ? "Anvil" :
        EffectiveLoaders.HasFlag(VersionLoaderKind.NeoForge) ? "NeoForge" :
        EffectiveLoaders.HasFlag(VersionLoaderKind.LiteLoader) ? "Egg" :
        EffectiveLoaders.HasFlag(VersionLoaderKind.Cleanroom) ? "Cleanroom" :
        EffectiveLoaders.HasFlag(VersionLoaderKind.LabyMod) ? "LabyMod" :
        Category.Equals("optifine", StringComparison.OrdinalIgnoreCase) || EffectiveHasOptiFine ? "GrassPath" :
        IsSnapshotVersion ? "CommandBlock" : "Grass";

    partial void OnLogoChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasLogo));
        OnPropertyChanged(nameof(IconImage));
    }

    partial void OnCategoryChanged(string value)
    {
        OnPropertyChanged(nameof(IsErrorVersion));
        OnPropertyChanged(nameof(IsFoolVersion));
        OnPropertyChanged(nameof(IsOldVersion));
        OnPropertyChanged(nameof(IsApiVersion));
        OnPropertyChanged(nameof(LoaderKindCount));
        OnPropertyChanged(nameof(EffectiveLoaders));
        OnPropertyChanged(nameof(EffectiveHasOptiFine));
        OnPropertyChanged(nameof(VersionDisplayText));
        OnPropertyChanged(nameof(InfoText));
        OnPropertyChanged(nameof(TitleSuffix));
        OnPropertyChanged(nameof(IconImage));
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteMark));
        OnPropertyChanged(nameof(FavoriteGlyph));
        OnPropertyChanged(nameof(FavoriteTooltip));
    }

    partial void OnIsHiddenChanged(bool value) => OnPropertyChanged(nameof(HiddenGlyph));

    partial void OnDescriptionChanged(string value)
    {
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(HasCustomDescription));
        OnPropertyChanged(nameof(InfoText));
        OnPropertyChanged(nameof(TitleSuffix));
        OnPropertyChanged(nameof(VersionDisplayText));
    }

    private string LoaderDisplayText()
    {
        var parts = new List<string>(5);
        var loaders = EffectiveLoaders;
        if (loaders.HasFlag(VersionLoaderKind.Forge))
            parts.Add("Forge" + VersionSuffix(ForgeVersion));
        if (loaders.HasFlag(VersionLoaderKind.NeoForge))
            parts.Add("NeoForge" + VersionSuffix(NeoForgeVersion));
        if (loaders.HasFlag(VersionLoaderKind.Fabric))
            parts.Add("Fabric" + VersionSuffix(FabricVersion));
        if (EffectiveHasOptiFine)
            parts.Add("OptiFine" + (string.IsNullOrWhiteSpace(OptiFineVersion) || OptiFineVersion == "未知版本"
                ? "" : " " + OptiFineVersion.Replace('-', ' ').Replace('_', ' ')));
        if (loaders.HasFlag(VersionLoaderKind.LiteLoader))
            parts.Add("LiteLoader");
        if (loaders.HasFlag(VersionLoaderKind.Cleanroom))
            parts.Add("Cleanroom" + VersionSuffix(CleanroomVersion));
        if (loaders.HasFlag(VersionLoaderKind.LabyMod))
            parts.Add("LabyMod" + VersionSuffix(LabyModVersion));
        return parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
    }

    private static string VersionSuffix(string? version) =>
        string.IsNullOrWhiteSpace(version) || version == "未知版本" ? "" : " " + version;

    private static bool IsFoolDate(string? releaseTime) =>
        DateTimeOffset.TryParse(releaseTime, out var date) &&
        date.ToUniversalTime().AddHours(2) is { Month: 4, Day: 1 };

    private static bool IsKnownFoolName(string name)
    {
        name = name.ToLowerInvariant();
        return name.StartsWith("2.0", StringComparison.Ordinal) || name is "15w14a" or "1.rv-pre1" or
            "3d shareware v1.34" or "20w14∞" or "20w14infinite" or "22w13oneblockatatime" or
            "23w13a_or_b" or "24w14potato" or "25w14craftmine" or "26w14a" ||
            name.StartsWith("20w14inf", StringComparison.Ordinal);
    }

    private string FoolDisplayName()
    {
        var name = (BaseGameVersion ?? Id).ToLowerInvariant();
        if (name.StartsWith("2.0", StringComparison.Ordinal))
            return "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！";
        return name switch
        {
            "15w14a" => "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。",
            "1.rv-pre1" => "2016 | 是时候将现代科技带入 Minecraft 了！",
            "3d shareware v1.34" => "2019 | 我们从地下室的废墟里找到了这个开发于 1994 年的杰作！",
            "20w14∞" or "20w14infinite" => "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！",
            "22w13oneblockatatime" => "2022 | 一次一个方块更新！迎接全新的挖掘、合成与骑乘玩法吧！",
            "23w13a_or_b" => "2023 | 研究表明：玩家喜欢作出选择——越多越好！",
            "24w14potato" => "2024 | 毒马铃薯一直都被大家忽视和低估，于是我们超级加强了它！",
            "25w14craftmine" => "2025 | 你可以合成任何东西——包括合成你的世界！",
            "26w14a" => "2026 | 为什么需要物品栏？让方块们跟着你走吧！",
            _ when name.StartsWith("20w14inf", StringComparison.Ordinal) =>
                "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！",
            _ => $"愚人节版本 {BaseGameVersion ?? Id}"
        };
    }

    private static Bitmap LoadDefaultIcon(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://WMCL.App/Assets/Blocks/{name}.png"));
        return new Bitmap(stream);
    }

    private static string FormatTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未知时间";
        return DateTimeOffset.TryParse(value, out var time)
            ? time.LocalDateTime.ToString("yyyy-MM-dd")
            : value;
    }
}
