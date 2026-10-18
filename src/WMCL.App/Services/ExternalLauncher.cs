using System.Diagnostics;

namespace WMCL.App.Services;

public static class ExternalLauncher
{
    public static void Open(string target)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", target)
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", target)
                : new ProcessStartInfo("xdg-open", target);
        info.UseShellExecute = false;
        Process.Start(info);
    }
}
