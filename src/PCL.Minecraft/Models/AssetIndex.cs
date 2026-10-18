using System.Text.Json.Serialization;

namespace PCL.Minecraft.Models;

public sealed class AssetIndex
{
    [JsonPropertyName("objects")] public Dictionary<string, AssetObject> Objects { get; set; } = [];
    [JsonPropertyName("virtual")] public bool Virtual { get; set; }
    [JsonPropertyName("map_to_resources")] public bool MapToResources { get; set; }
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}
