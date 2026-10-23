using System.IO.Compression;
using System.Text;
using WMCL.Modding.Local;

namespace WMCL.Minecraft.Tests;

public sealed class JarInJarServiceTests
{
    [Fact]
    public void ScansDeclaredAndDirectoryNestedJars()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-jij-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "outer.jar");
        try
        {
            var inner = BuildJar(new Dictionary<string, string>
            {
                ["fabric.mod.json"] = """{"id":"inner","name":"Inner Mod","version":"1.0"}"""
            });
            using (var outer = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(outer, "fabric.mod.json",
                    """{"id":"outer","jars":[{"file":"META-INF/jars/inner.jar"}]}""");
                Write(outer, "META-INF/jars/inner.jar", inner);
            }

            var entry = Assert.Single(JarInJarService.Scan(path));
            Assert.Equal("META-INF/jars/inner.jar", entry.Path);
            Assert.Equal("inner", entry.ModId);
            Assert.Equal("Inner Mod", entry.Name);
            Assert.Equal("1.0", entry.Version);
            Assert.Empty(entry.Children);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FlagsDuplicateNestedModIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-jij-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "outer.jar");
        try
        {
            using (var outer = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(outer, "META-INF/jars/one.jar", BuildJar(new Dictionary<string, string>
                {
                    ["fabric.mod.json"] = """{"id":"dup","version":"1.0"}"""
                }));
                Write(outer, "META-INF/jars/two.jar", BuildJar(new Dictionary<string, string>
                {
                    ["fabric.mod.json"] = """{"id":"dup","version":"2.0"}"""
                }));
            }
            var result = JarInJarService.ScanDetailed(path);
            Assert.Equal(2, result.Entries.Count);
            var warning = Assert.Single(result.Warnings);
            Assert.Contains("重复 Mod ID：dup", warning);
            Assert.Contains("1.0", warning);
            Assert.Contains("2.0", warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FlagsMissingDependenciesAndHostConflicts()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-jij-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "outer.jar");
        try
        {
            using (var outer = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(outer, "fabric.mod.json", """{"id":"host"}""");
                Write(outer, "META-INF/jars/a.jar", BuildJar(new Dictionary<string, string>
                {
                    ["fabric.mod.json"] = """{"id":"a","depends":{"missing":"*"},"breaks":{"host":"*"}}"""
                }));
                Write(outer, "META-INF/jars/b.jar", BuildJar(new Dictionary<string, string>
                {
                    ["fabric.mod.json"] = """{"id":"b"}"""
                }));
            }
            var result = JarInJarService.ScanDetailed(path);
            Assert.Contains(result.Warnings, warning => warning.Contains("依赖 missing"));
            Assert.Contains(result.Warnings, warning => warning.Contains("与 host 不兼容"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsNonZip()
    {
        var path = Path.Combine(Path.GetTempPath(), "wmcl-jij-" + Guid.NewGuid().ToString("N") + ".jar");
        File.WriteAllText(path, "not a zip");
        try
        {
            Assert.Throws<InvalidDataException>(() => JarInJarService.Scan(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildJar(Dictionary<string, string> files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
                Write(archive, name, content);
        return memory.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content) =>
        Write(archive, name, Encoding.UTF8.GetBytes(content));

    private static void Write(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
