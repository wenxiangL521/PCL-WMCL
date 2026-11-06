using System.Runtime.CompilerServices;

namespace WMCL.Tests;

internal static class TestEnvironment
{
    internal static string Root { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        Root = Path.Combine(Path.GetTempPath(), "wmcl-tests-" + Guid.NewGuid().ToString("N"));
        // 测试进程绝不弹 pkexec：联机相关用例只验证状态，不真正提权
        Environment.SetEnvironmentVariable("WMCL_NO_ELEVATION", "1");
        foreach (var (variable, directory) in new[]
                 {
                     ("XDG_CONFIG_HOME", "config"),
                     ("XDG_DATA_HOME", "data"),
                     ("XDG_CACHE_HOME", "cache")
                 })
        {
            var path = Path.Combine(Root, directory);
            Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable(variable, path);
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
