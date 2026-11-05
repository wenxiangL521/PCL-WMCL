using WMCL.Core;

namespace WMCL.App.Services;

/// <summary>离线账号的本地皮肤：按玩家名存放在数据目录，供皮肤编辑器与启动时使用。</summary>
public static class OfflineSkinStore
{
    private static string Directory => Path.Combine(WmclPaths.DataDirectory, "skins", "offline");

    public static string PathFor(string? playerName)
    {
        var name = string.IsNullOrWhiteSpace(playerName) ? "player" : playerName.Trim();
        // 固定一套跨平台非法字符，保证在 Windows 实例上同步时也安全
        const string invalid = "\\/:*?\"<>|";
        var safe = string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
        return Path.Combine(Directory, safe + ".png");
    }

    public static bool Exists(string? playerName) => File.Exists(PathFor(playerName));

    /// <summary>把选中的皮肤复制为当前账号的离线皮肤。</summary>
    public static string Save(string? playerName, string sourcePath)
    {
        var target = PathFor(playerName);
        System.IO.Directory.CreateDirectory(Directory);
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }

    /// <summary>写入皮肤字节（编辑器保存时用）。</summary>
    public static string Save(string? playerName, byte[] content)
    {
        var target = PathFor(playerName);
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllBytes(target, content);
        return target;
    }

    public static void Delete(string? playerName)
    {
        try { File.Delete(PathFor(playerName)); } catch (IOException) { }
    }

    /// <summary>
    /// 启动前把离线皮肤同步到实例的 CustomSkinLoader 本地皮肤目录（装了该 Mod 时生效）。
    /// 返回是否写入成功。
    /// </summary>
    public static bool TryApplyToInstance(string? playerName, string instanceDirectory)
    {
        if (!Exists(playerName)) return false;
        var localSkinDir = Path.Combine(instanceDirectory, "CustomSkinLoader", "LocalSkin", "skins");
        try
        {
            System.IO.Directory.CreateDirectory(localSkinDir);
            File.Copy(PathFor(playerName), Path.Combine(localSkinDir, (playerName ?? "player").Trim() + ".png"), overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
