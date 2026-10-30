using System.Text.Json.Nodes;

namespace WMCL.App.Services;

/// <summary>
/// 共享资源冲突判定。资源索引是 JSON，整合包经常只改缩进/排版，
/// 字节哈希不同但对象映射完全一致；这种情况应保留现有文件而不是中止安装。
/// </summary>
internal static class SharedAssetComparer
{
    public static bool IsAssetIndex(string relativePath) =>
        relativePath.StartsWith("assets/indexes/", StringComparison.OrdinalIgnoreCase) &&
        relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> SemanticallyEqualAsync(
        string left, string right, CancellationToken ct = default)
    {
        try
        {
            var leftText = await File.ReadAllTextAsync(left, ct);
            var rightText = await File.ReadAllTextAsync(right, ct);
            var leftNode = JsonNode.Parse(leftText);
            var rightNode = JsonNode.Parse(rightText);
            return leftNode is not null && rightNode is not null &&
                   JsonNode.DeepEquals(leftNode, rightNode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
