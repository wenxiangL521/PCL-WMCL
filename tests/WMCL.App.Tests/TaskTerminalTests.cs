using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class TaskTerminalTests
{
    [Theory]
    [InlineData("安装完成", 100, TaskTerminalState.Completed)]
    [InlineData("安装失败：校验", 40, TaskTerminalState.Failed)]
    [InlineData("已取消安装", 40, TaskTerminalState.Canceled)]
    [InlineData("正在安装…", 40, TaskTerminalState.Ended)]
    public void MapsTerminalStateForHistoryGlyph(string detail, double progress, TaskTerminalState expected)
    {
        Assert.Equal(expected, TaskManagerViewModel.TerminalState(detail, progress));
    }

    [Theory]
    [InlineData("整合包安装完成：Pack", 100, "完成")]
    [InlineData("安装完成", 100, "完成")]
    [InlineData("安装失败：校验不通过", 42, "失败")]
    [InlineData("已取消安装", 37, "已取消")]
    [InlineData("正在安装…", 42, "结束")]
    [InlineData("已导入实例 Pack", 0, "完成")]
    [InlineData("", 100, "完成")]
    public void InfersTerminalStateFromFinalStatus(string detail, double progress, string expected)
    {
        Assert.Equal(expected, TaskManagerViewModel.TerminalOutcome(detail, progress));
    }
}
