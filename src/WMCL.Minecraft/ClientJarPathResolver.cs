using WMCL.Core.IO;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

public static class ClientJarPathResolver
{
    public static string Resolve(string minecraftDirectory, string versionId, VersionJson version)
    {
        InstanceManager.ValidateName(versionId);
        DownloadInfo? client = null;
        version.Downloads?.TryGetValue("client", out client);
        var relative = string.IsNullOrWhiteSpace(client?.Path) ? versionId + ".jar" : client.Path;
        return FileUtil.SafeChildPath(Path.Combine(minecraftDirectory, "versions", versionId), relative);
    }
}
