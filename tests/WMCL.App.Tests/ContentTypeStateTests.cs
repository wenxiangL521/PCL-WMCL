using WMCL.App.ViewModels.Content;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class ContentTypeStateTests
{
    [Theory]
    [InlineData("content-shader", "光影", true, false, false)]
    [InlineData("content-world", "世界", false, true, false)]
    [InlineData("content-datapack", "数据包", false, false, true)]
    [InlineData("资源包", "资源包", false, false, false)]
    [InlineData(null, "资源包", false, false, false)]
    public void DerivesFlagsFromKeyOrType(string? input, string type, bool shaders, bool worlds, bool dataPacks)
    {
        var state = ContentTypeState.From(input);
        Assert.Equal(type, state.Type);
        Assert.Equal(shaders, state.Shaders);
        Assert.Equal(worlds, state.Worlds);
        Assert.Equal(dataPacks, state.DataPacks);
    }

    [Fact]
    public void PluginTypeIsDetected()
    {
        Assert.True(ContentTypeState.From("插件").IsPlugin);
        Assert.False(ContentTypeState.From("资源包").IsPlugin);
    }

    [Theory]
    [InlineData("世界", "存档", "搜索世界，例如 skyblock")]
    [InlineData("数据包", "数据包", "搜索数据包，例如 terralith")]
    [InlineData("光影", "光影", "搜索光影，例如 bsl")]
    [InlineData("资源包", "资源包", "搜索资源包，例如 faithful")]
    public void LabelAndExampleFollowType(string type, string label, string example)
    {
        var state = ContentTypeState.From(type);
        Assert.Equal(label, state.Label);
        Assert.Equal(example, state.SearchExample);
    }
}
