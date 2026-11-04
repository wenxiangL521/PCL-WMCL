using WMCL.App.ViewModels.Content;
using WMCL.App.ViewModels.Launching;

namespace WMCL.App.Tests;

public sealed class LaunchButtonTextTests
{
    [Theory]
    [InlineData(true, false, "正在加载")]
    [InlineData(false, true, "启动游戏")]
    [InlineData(false, false, "下载游戏")]
    public void TitlesFollowState(bool loading, bool installed, string expected)
    {
        Assert.Equal(expected, LaunchButtonText.Title(loading, installed));
    }

    [Theory]
    [InlineData(false, true, "1.20.1", "1.20.1")]
    [InlineData(false, true, "", "未选择版本")]
    [InlineData(false, false, "1.20.1", "未找到可用的游戏版本")]
    [InlineData(true, true, "1.20.1", "正在加载版本列表，请稍候")]
    public void SubtitlesFollowState(bool loading, bool installed, string version, string expected)
    {
        Assert.Equal(expected, LaunchButtonText.Subtitle(loading, installed, version));
    }

    [Theory]
    [InlineData("插件", "搜索服务端插件，例如 luckperms")]
    [InlineData("光影", "搜索光影，例如 bsl")]
    [InlineData("资源包", "搜索资源包，例如 faithful")]
    public void ContentExamplesMatchType(string type, string expected)
    {
        Assert.Equal(expected, ContentTypeLabel.SearchExample(type));
    }

    [Fact]
    public void ContentLabelPrefersWorldsThenDataPacksThenShaders()
    {
        Assert.Equal("存档", ContentTypeLabel.Of(true, true, true));
        Assert.Equal("数据包", ContentTypeLabel.Of(false, true, true));
        Assert.Equal("光影", ContentTypeLabel.Of(false, false, true));
        Assert.Equal("资源包", ContentTypeLabel.Of(false, false, false));
    }
}
