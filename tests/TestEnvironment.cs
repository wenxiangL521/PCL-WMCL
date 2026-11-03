using System.Runtime.CompilerServices;

namespace WMCL.Tests;

internal static class TestEnvironment
{
    internal static string Root { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        Root = Path.Combine(Path.GetTempPath(), "wmcl-tests-" + Guid.NewGuid().ToString("N"));
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
