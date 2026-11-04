using WMCL.App.ViewModels.Instances;

namespace WMCL.App.Tests;

public sealed class InstanceWindowStateTests
{
    [Fact]
    public void NullSizeMeansFollowGlobal()
    {
        var state = InstanceWindowState.From(null, null, 1200, 800);
        Assert.True(state.UseGlobal);
        Assert.False(state.CanEdit);
        Assert.Equal((null, null), state.ToSettings());
    }

    [Fact]
    public void ExplicitSizeOverridesGlobal()
    {
        var state = InstanceWindowState.From(1000, 700, 1200, 800);
        Assert.False(state.UseGlobal);
        Assert.True(state.CanEdit);
        Assert.Equal((1000, 700), state.ToSettings());
    }

    [Fact]
    public void PartialSizeFallsBackPerAxis()
    {
        var state = InstanceWindowState.From(1000, null, 1200, 800);
        Assert.False(state.UseGlobal);
        Assert.Equal(1000m, state.Width);
        Assert.Equal(800m, state.Height);
        Assert.Equal((1000, 800), state.ToSettings());
    }

    [Fact]
    public void RoundTripsGlobalAndExplicit()
    {
        var global = InstanceWindowState.From(null, null, 1280, 720);
        var (w1, h1) = global.ToSettings();
        Assert.True(InstanceWindowState.From(w1, h1, 1280, 720).UseGlobal);

        var explicitSize = InstanceWindowState.From(900, 600, 1280, 720);
        var (w2, h2) = explicitSize.ToSettings();
        var again = InstanceWindowState.From(w2, h2, 1280, 720);
        Assert.False(again.UseGlobal);
        Assert.Equal(900m, again.Width);
        Assert.Equal(600m, again.Height);
    }
}
