using System.Text.Json;
using PCL.Minecraft.Models;

namespace PCL.Minecraft;

public static class VersionMerger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 把子版本合并到父版本上：标量以子为准，libraries/arguments 追加。
    /// 对应原版 ModMinecraft.McLibListGetWithJson 的 inheritsFrom 处理。
    /// </summary>
    public static VersionJson Merge(VersionJson parent, VersionJson child)
    {
        var result = Clone(parent);

        result.Id = child.Id;
        result.MainClass = child.MainClass ?? parent.MainClass;
        result.Assets = child.Assets ?? parent.Assets;
        result.AssetIndex = child.AssetIndex ?? parent.AssetIndex;
        result.JavaVersion = child.JavaVersion ?? parent.JavaVersion;
        result.Logging = child.Logging ?? parent.Logging;
        result.Type = child.Type ?? parent.Type;
        result.Time = child.Time ?? parent.Time;
        result.ReleaseTime = child.ReleaseTime ?? parent.ReleaseTime;
        result.MinimumLauncherVersion = child.MinimumLauncherVersion ?? parent.MinimumLauncherVersion;
        result.ComplianceLevel = child.ComplianceLevel ?? parent.ComplianceLevel;

        result.Libraries = [.. parent.Libraries, .. child.Libraries];

        var parentArgs = parent.Arguments;
        var childArgs = child.Arguments;
        if (parentArgs is not null || childArgs is not null)
        {
            result.Arguments = new Arguments
            {
                Game = [.. parentArgs?.Game ?? [], .. childArgs?.Game ?? []],
                Jvm = [.. parentArgs?.Jvm ?? [], .. childArgs?.Jvm ?? []]
            };
        }

        result.MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments;

        result.Downloads = MergeDownloads(parent.Downloads, child.Downloads);
        result.InheritsFrom = child.InheritsFrom;
        return result;
    }

    public static VersionJson Clone(VersionJson source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<VersionJson>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to clone version JSON.");
    }

    private static Dictionary<string, DownloadInfo>? MergeDownloads(
        Dictionary<string, DownloadInfo>? parent,
        Dictionary<string, DownloadInfo>? child)
    {
        if (parent is null) return child;
        if (child is null) return parent;

        var result = new Dictionary<string, DownloadInfo>(parent, StringComparer.Ordinal);
        foreach (var (key, value) in child) result[key] = value;
        return result;
    }
}
