using System.Text.Json;

namespace WMCL.Core;

/// <summary>
/// 解析 EasyTier Uptime 的公共节点列表。格式与 PCL2 使用的
/// `https://uptime.easytier.cn/api/nodes` 兼容：data.items[]。
/// </summary>
public static class EasyTierNodeList
{
    public static IReadOnlyList<string> Parse(string json, int max = 12)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            JsonElement items;
            if (root.ValueKind == JsonValueKind.Array)
                items = root;
            else if (root.TryGetProperty("data", out var data) &&
                     data.ValueKind == JsonValueKind.Object &&
                     data.TryGetProperty("items", out var dataItems))
                items = dataItems;
            else if (root.TryGetProperty("items", out var rootItems))
                items = rootItems;
            else
                return [];

            var nodes = new List<(double Load, string Address)>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!IsTrue(item, "is_active") || !IsTrue(item, "is_approved")) continue;
                if (!item.TryGetProperty("address", out var addressElement)) continue;
                var address = addressElement.GetString();
                if (string.IsNullOrWhiteSpace(address)) continue;
                if (!address.Contains("://", StringComparison.Ordinal))
                    address = "tcp://" + address;

                var tags = new List<string>();
                if (item.TryGetProperty("tags", out var tagsElement) &&
                    tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tagsElement.EnumerateArray())
                        if (tag.GetString() is { Length: > 0 } value) tags.Add(value);
                }
                if (tags.Count > 0 &&
                    (!tags.Contains("国内") || !tags.Contains("MC中继")))
                    continue;

                var usage = item.TryGetProperty("usage_percentage", out var usageElement) &&
                            usageElement.TryGetDouble(out var usageValue)
                    ? usageValue
                    : 0;
                var health = item.TryGetProperty("health_percentage_24h", out var healthElement) &&
                             healthElement.TryGetDouble(out var healthValue)
                    ? healthValue
                    : 100;
                nodes.Add((usage * (110 - health), address));
            }

            return nodes
                .OrderBy(node => node.Load)
                .Select(node => node.Address)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, max))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsTrue(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) ||
        value.ValueKind == JsonValueKind.True;
}
