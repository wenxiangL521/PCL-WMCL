using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft.Tests;

public class RuleEvaluatorTests
{
    private static RuleContext Linux(Dictionary<string, bool>? features = null) =>
        new("linux", "x86_64", "6.8.0", features ?? new Dictionary<string, bool>());

    [Fact]
    public void NoRulesMeansAllowed()
    {
        Assert.True(RuleEvaluator.IsAllowed(null, Linux()));
        Assert.True(RuleEvaluator.IsAllowed([], Linux()));
    }

    [Fact]
    public void AllowsMatchingLinuxRule()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new RuleOs { Name = "linux" } }
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, Linux()));
    }

    [Theory]
    [InlineData("x64", "x86_64", true)]
    [InlineData("x86_64", "x64", true)]
    [InlineData("x86", "x86_64", false)]
    [InlineData("arm64", "aarch64", true)]
    public void ArchitectureAliasesMatch(string ruleArch, string contextArch, bool expected)
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new RuleOs { Name = "linux", Arch = ruleArch } }
        };
        Assert.Equal(expected, RuleEvaluator.IsAllowed(rules,
            new RuleContext("linux", contextArch, "6.8.0", new Dictionary<string, bool>())));
    }

    [Fact]
    public void DeniesWindowsOnlyRuleOnLinux()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new RuleOs { Name = "windows" } }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules, Linux()));
    }

    [Fact]
    public void LastMatchingRuleWins()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new RuleOs { Name = "linux" } }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules, Linux()));
    }

    [Fact]
    public void CustomResolutionFeatureMustMatch()
    {
        var rules = new List<Rule>
        {
            new()
            {
                Action = "allow",
                Features = new Dictionary<string, bool> { ["has_custom_resolution"] = true }
            }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules, Linux()));
        Assert.True(RuleEvaluator.IsAllowed(rules,
            Linux(new Dictionary<string, bool> { ["has_custom_resolution"] = true })));
    }

    [Fact]
    public void OsVersionIsRegex()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new RuleOs { Name = "linux", Version = "^6\\." } }
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, Linux()));
        Assert.False(RuleEvaluator.IsAllowed(rules,
            new RuleContext("linux", "x86_64", "5.15.0", new Dictionary<string, bool>())));
    }
}
