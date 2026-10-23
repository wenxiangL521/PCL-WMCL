using System.Text.Json.Serialization;

namespace WMCL.Minecraft.Models;

public sealed class VersionJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; set; }
    [JsonPropertyName("clientVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseGameVersion { get; set; }
    [JsonIgnore] public MinecraftVersionIdentity? GameVersionIdentity { get; set; }
    [JsonPropertyName("mainClass")] public string? MainClass { get; set; }
    [JsonPropertyName("assets")] public string? Assets { get; set; }
    [JsonPropertyName("assetIndex")] public AssetIndexInfo? AssetIndex { get; set; }
    [JsonPropertyName("downloads")] public Dictionary<string, DownloadInfo>? Downloads { get; set; }
    [JsonPropertyName("libraries")] public List<Library> Libraries { get; set; } = [];
    [JsonPropertyName("arguments")] public Arguments? Arguments { get; set; }
    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; set; }
    [JsonPropertyName("javaVersion")] public JavaVersionInfo? JavaVersion { get; set; }
    [JsonPropertyName("labymod_data")] public LabyModData? LabyModData { get; set; }
    [JsonPropertyName("logging")] public LoggingInfo? Logging { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("time")] public string? Time { get; set; }
    [JsonPropertyName("releaseTime")] public string? ReleaseTime { get; set; }
    [JsonPropertyName("minimumLauncherVersion")] public int? MinimumLauncherVersion { get; set; }
    [JsonPropertyName("complianceLevel")] public int? ComplianceLevel { get; set; }
}

public sealed class LabyModData
{
    [JsonPropertyName("version")] public string? Version { get; set; }
}

public sealed class AssetIndexInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("totalSize")] public long? TotalSize { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed class DownloadInfo
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public sealed class Library
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; set; }
    [JsonPropertyName("rules")] public List<Rule>? Rules { get; set; }
    [JsonPropertyName("extract")] public ExtractInfo? Extract { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")] public DownloadInfo? Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

public sealed class ExtractInfo
{
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }
}

public sealed class JavaVersionInfo
{
    [JsonPropertyName("component")] public string? Component { get; set; }
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; }
}

public sealed class LoggingInfo
{
    [JsonPropertyName("client")] public LoggingClient? Client { get; set; }
}

public sealed class LoggingClient
{
    [JsonPropertyName("argument")] public string? Argument { get; set; }
    [JsonPropertyName("file")] public DownloadInfo? File { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}
