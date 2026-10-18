namespace PCL.Core.Music;

public static class MusicPlaylist
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".ogg", ".wav", ".flac", ".m4a", ".aac", ".opus"
    };

    public static IReadOnlyList<string> Enumerate(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder)
            .Where(file => Extensions.Contains(Path.GetExtension(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
