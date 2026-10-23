using System.Text;

namespace WMCL.Modding.Forgelike;

/// <summary>
/// Cleanroom 1.12.2 的 Forge splash 线程会卡在 org.lwjgl.opengl.Display.sync，
/// 与当前 LWJGL 后端不兼容。安装后把官方 splash 配置的 disableSplash 设为 true；
/// 这是让实例可启动的兼容处理，不改其他 splash 选项。
/// </summary>
public static class CleanroomSplashConfig
{
    public static void Ensure(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, "config", "splash.properties");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "disableSplash=true\n", new UTF8Encoding(false));
            return;
        }

        var lines = File.ReadAllLines(path).ToList();
        var replaced = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            if (!line[..separator].Trim().Equals("disableSplash", StringComparison.OrdinalIgnoreCase))
                continue;
            lines[index] = "disableSplash=true";
            replaced = true;
            break;
        }
        if (!replaced) lines.Add("disableSplash=true");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
    }
}
