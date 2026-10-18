using WMCL.Core;

namespace WMCL.Core.Tests;

public class EasyTierNodeListTests
{
    [Fact]
    public void ParsesAndFiltersNodes()
    {
        var json = """
            {"data":{"items":[
              {"address":"tcp://a.example:11010","is_active":true,"is_approved":true,
               "usage_percentage":50,"health_percentage_24h":100,"tags":["国内","MC中继"]},
              {"address":"b.example:11010","is_active":true,"is_approved":true,
               "usage_percentage":0,"health_percentage_24h":100,"tags":[]},
              {"address":"c.example:11010","is_active":false,"is_approved":true,"tags":[]},
              {"address":"d.example:11010","is_active":true,"is_approved":true,"tags":["其他"]}
            ]}}
            """;
        var nodes = EasyTierNodeList.Parse(json);
        Assert.Equal(2, nodes.Count);
        Assert.Contains("tcp://a.example:11010", nodes);
        Assert.Contains("tcp://b.example:11010", nodes);
        Assert.DoesNotContain("c.example:11010", nodes);
    }
}
