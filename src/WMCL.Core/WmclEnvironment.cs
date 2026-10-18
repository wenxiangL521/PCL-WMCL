namespace WMCL.Core;

/// <summary>WMCL_* 环境变量，回退兼容旧 PCL_* 名称。</summary>
public static class WmclEnvironment
{
    public static string? Get(string name) =>
        Environment.GetEnvironmentVariable("WMCL_" + name)
        ?? Environment.GetEnvironmentVariable("PCL_" + name);
}
