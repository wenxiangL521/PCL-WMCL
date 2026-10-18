namespace WMCL.Minecraft;

/// <summary>
/// Maven 坐标：group:artifact:version[:classifier][@extension]。
/// </summary>
public sealed record MavenName(
    string Group,
    string Artifact,
    string Version,
    string? Classifier,
    string Extension)
{
    public static MavenName Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Maven name is empty.", nameof(name));

        var extension = "jar";
        var at = name.LastIndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        if (parts.Length is < 3 or > 4)
            throw new FormatException($"Invalid Maven name: {name}");

        return new MavenName(
            parts[0],
            parts[1],
            parts[2],
            parts.Length == 4 ? parts[3] : null,
            extension);
    }

    public string Path
    {
        get
        {
            var file = $"{Artifact}-{Version}{(Classifier is null ? "" : "-" + Classifier)}.{Extension}";
            return string.Join('/', Group.Split('.').Append(Artifact).Append(Version).Append(file));
        }
    }

    /// <summary>用于去重的键（不含版本）。</summary>
    public string Key => $"{Group}:{Artifact}:{Classifier ?? ""}";
}
