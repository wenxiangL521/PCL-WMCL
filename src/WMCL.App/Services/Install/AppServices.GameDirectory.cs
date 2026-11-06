using WMCL.Core;

namespace WMCL.App.Services;

public sealed partial class AppServices
{
    public void ApplyInstanceChange(InstanceChangeEventArgs change)
    {
        if (change.Kind == InstanceChangeKind.Cloned) return;
        foreach (var list in new[] { GetFavoriteVersions(change.GameDirectory), GetHiddenVersions(change.GameDirectory) })
        {
            for (var index = list.Count - 1; index >= 0; index--)
            {
                var remapped = change.Remap(list[index]);
                if (remapped is null) list.RemoveAt(index);
                else list[index] = remapped;
            }
        }
        SaveSettings();
    }

    public void SetGameDirectory(string path)
    {
        path = WmclPaths.Expand(path);
        if (!Settings.GameFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            Settings.GameFolders.Add(path);
        var changed = !string.Equals(Settings.GameDirectory, path, StringComparison.OrdinalIgnoreCase);
        Settings.GameDirectory = path;
        Repository.SetGameDirectory(path);
        SaveSettings();
        if (changed) GameDirectoryChanged?.Invoke(this, path);
    }

    public List<string> GetFavoriteVersions(string folder)
    {
        folder = WmclPaths.Expand(folder);
        if (!Settings.FavoriteVersionsByFolder.TryGetValue(folder, out var list))
        {
            list = [];
            Settings.FavoriteVersionsByFolder[folder] = list;
        }
        return list;
    }

    public List<string> GetHiddenVersions(string folder)
    {
        folder = WmclPaths.Expand(folder);
        if (!Settings.HiddenVersionsByFolder.TryGetValue(folder, out var list))
        {
            list = [];
            Settings.HiddenVersionsByFolder[folder] = list;
        }
        return list;
    }

    public void RenameGameFolder(string path, string? name)
    {
        var full = WmclPaths.Expand(path);
        if (string.IsNullOrWhiteSpace(name))
            Settings.GameFolderNames.Remove(full);
        else
            Settings.GameFolderNames[full] = name.Trim();
        SaveSettings();
    }

    public void RemoveGameFolder(string path)
    {
        Settings.GameFolders.RemoveAll(folder =>
            string.Equals(folder, path, StringComparison.OrdinalIgnoreCase));
        if (Settings.GameFolders.Count == 0)
            Settings.GameFolders.Add(WmclPaths.DefaultGameDirectory);
        if (string.Equals(Settings.GameDirectory, path, StringComparison.OrdinalIgnoreCase))
        {
            SetGameDirectory(Settings.GameFolders[0]);
            return;
        }
        SaveSettings();
    }
}
