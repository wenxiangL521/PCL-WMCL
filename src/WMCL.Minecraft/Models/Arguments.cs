using System.Text.Json;
using System.Text.Json.Serialization;

namespace WMCL.Minecraft.Models;

public sealed class Arguments
{
    [JsonPropertyName("game")] public List<ArgumentEntry> Game { get; set; } = [];
    [JsonPropertyName("jvm")] public List<ArgumentEntry> Jvm { get; set; } = [];
}

/// <summary>
/// 原版 arguments 里的元素既可能是字符串，也可能是带 rules 的对象。
/// </summary>
[JsonConverter(typeof(ArgumentEntryConverter))]
public sealed class ArgumentEntry
{
    public List<Rule>? Rules { get; set; }
    public List<string> Values { get; set; } = [];
    public bool IsLiteral { get; set; }
}

public sealed class ArgumentEntryConverter : JsonConverter<ArgumentEntry>
{
    public override ArgumentEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ArgumentEntry
            {
                IsLiteral = true,
                Values = [reader.GetString() ?? ""]
            };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Unexpected token {reader.TokenType} for argument entry.");

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var entry = new ArgumentEntry { IsLiteral = false };

        if (root.TryGetProperty("rules", out var rules))
            entry.Rules = rules.Deserialize<List<Rule>>(options);

        if (root.TryGetProperty("value", out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
                entry.Values.Add(value.GetString() ?? "");
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray())
                    entry.Values.Add(item.GetString() ?? "");
            else
                throw new JsonException($"Unexpected argument value kind {value.ValueKind}.");
        }

        return entry;
    }

    public override void Write(Utf8JsonWriter writer, ArgumentEntry value, JsonSerializerOptions options)
    {
        if (value.IsLiteral && value.Rules is null)
        {
            writer.WriteStringValue(value.Values.FirstOrDefault() ?? "");
            return;
        }

        writer.WriteStartObject();
        if (value.Rules is not null)
        {
            writer.WritePropertyName("rules");
            JsonSerializer.Serialize(writer, value.Rules, options);
        }
        writer.WritePropertyName("value");
        if (value.Values.Count == 1)
            writer.WriteStringValue(value.Values[0]);
        else
        {
            writer.WriteStartArray();
            foreach (var v in value.Values) writer.WriteStringValue(v);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}
