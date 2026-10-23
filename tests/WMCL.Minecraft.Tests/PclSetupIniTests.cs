using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public sealed class PclSetupIniTests
{
    [Fact]
    public void ReadsPclFieldsAndPreservesCacheOnSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-setup-ini-" + Guid.NewGuid().ToString("N"));
        var pcl = Path.Combine(root, "PCL");
        Directory.CreateDirectory(pcl);
        var logo = Path.Combine(pcl, "Logo.png");
        File.WriteAllBytes(logo, [1, 2, 3]);
        File.WriteAllText(Path.Combine(pcl, "Setup.ini"), """
            DisplayType:4
            CustomInfo:来自 PCL 的描述
            Logo:PCL\Logo.png
            LogoCustom:True
            State:1
            Info:cache
            """);
        try
        {
            var settings = InstanceSettingsStore.Load(root);
            Assert.Equal(4, settings.VersionCardType);
            Assert.Equal("来自 PCL 的描述", settings.Description);
            Assert.Equal(logo, settings.LogoPath);

            settings.VersionCardType = 3;
            settings.Description = "WMCL 修改";
            InstanceSettingsStore.Save(root, settings);
            var ini = File.ReadAllLines(Path.Combine(pcl, "Setup.ini"));
            Assert.Contains("DisplayType:3", ini);
            Assert.Contains("CustomInfo:WMCL 修改", ini);
            Assert.Contains("State:1", ini);
            Assert.Contains("Logo:PCL\\Logo.png", ini);
            Assert.Contains("LogoCustom:True", ini);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(logo));

            InstanceSettingsStore.Reset(root);
            var reset = PclSetupIni.Read(root);
            Assert.DoesNotContain("DisplayType", reset.Keys);
            Assert.DoesNotContain("CustomInfo", reset.Keys);
            Assert.Contains("State", reset.Keys);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CopiesCustomLogoIntoTargetPclFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-setup-logo-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "custom.png");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(source, [9, 8, 7]);
        try
        {
            var settings = new InstanceSettings { VersionCardType = 2, Description = "custom", LogoPath = source };
            InstanceSettingsStore.Save(root, settings);
            var copied = Path.Combine(root, "PCL", "Logo.png");
            Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(copied));
            Assert.Contains("LogoCustom:True", File.ReadAllLines(Path.Combine(root, "PCL", "Setup.ini")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
