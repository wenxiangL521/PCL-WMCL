using System.Text.Json.Serialization;

namespace WMCL.Minecraft.Models;

public sealed class Rule
{
    [JsonPropertyName("action")] public string Action { get; set; } = "allow";
    [JsonPropertyName("os")] public RuleOs? Os { get; set; }
    [JsonPropertyName("features")] public Dictionary<string, bool>? Features { get; set; }
}

public sealed class RuleOs
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("arch")] public string? Arch { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}
