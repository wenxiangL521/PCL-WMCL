using System.Diagnostics;

namespace WMCL.App.Services;

public static class ExternalLauncher
{
    public static void Open(string target)
    {
        using var process = Process.Start(CreateStartInfo(target, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()));
    }

    internal static ProcessStartInfo CreateStartInfo(string target, bool windows, bool macOS)
    {
        var info = new ProcessStartInfo(windows ? "explorer.exe" : macOS ? "open" : "xdg-open")
        {
            UseShellExecute = false
        };
        info.ArgumentList.Add(target);
        return info;
    }
}
