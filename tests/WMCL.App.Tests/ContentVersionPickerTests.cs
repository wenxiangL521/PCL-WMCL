using WMCL.App.ViewModels.Content;

namespace WMCL.App.Tests;

public sealed class ContentVersionPickerTests
{
    private sealed record Version(string Id);

    [Fact]
    public void KeepsPreviousWhenStillVisible()
    {
        var a = new Version("a");
        var b = new Version("b");
        Assert.Same(b, ContentVersionPicker.Pick([a, b], b));
    }

    [Fact]
    public void FallsBackToFirstWhenPreviousFilteredOut()
    {
        var a = new Version("a");
        Assert.Same(a, ContentVersionPicker.Pick([a], new Version("gone")));
    }

    [Fact]
    public void ReturnsNullForEmptyList()
    {
        Assert.Null(ContentVersionPicker.Pick<Version>([], new Version("x")));
    }

    [Theory]
    [InlineData(0, "全部", "当前筛选没有版本")]
    [InlineData(4, "全部", "共 4 个版本")]
    [InlineData(4, "", "共 4 个版本")]
    [InlineData(4, "1.20", "共 4 个版本（筛选：1.20）")]
    public void DescribesStatus(int count, string filter, string expected)
    {
        Assert.Equal(expected, ContentVersionPicker.Describe(count, filter));
    }
}
