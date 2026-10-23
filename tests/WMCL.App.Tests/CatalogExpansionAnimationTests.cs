using WMCL.App.Animation;

namespace WMCL.App.Tests;

public sealed class CatalogExpansionAnimationTests
{
    [Theory]
    [InlineData(0, 92, 150)]
    [InlineData(92, 0, 150)]
    [InlineData(0, 800, 150)]
    [InlineData(0, 1000, 412.5)]
    [InlineData(0, 3200, 700)]
    [InlineData(3200, 0, 250)]
    [InlineData(0, 37808, 700)]
    [InlineData(37808, 0, 250)]
    [InlineData(92, 92, 150)]
    public void ExpansionStaysWithinBoundsAndFinishesAtPclDuration(double start, double target, double duration)
    {
        var animation = new CatalogExpansionAnimation(start, target);
        Assert.Equal(duration, animation.DurationMilliseconds, 6);
        Assert.Equal(start, animation.HeightAt(-1));
        var previous = start;
        for (var milliseconds = 0; milliseconds <= duration; milliseconds++)
        {
            var height = animation.HeightAt(milliseconds);
            Assert.InRange(height, Math.Min(start, target), Math.Max(start, target));
            Assert.True(target >= start ? height >= previous : height <= previous);
            previous = height;
        }
        Assert.Equal(target, animation.HeightAt(duration));
        Assert.Equal(target, animation.HeightAt(duration + 100));
    }

    [Theory]
    [InlineData(0, 1000, 212.5)]
    [InlineData(0, 3200, 300)]
    [InlineData(3200, 0, 100)]
    public void DecelerationKeepsTheIncomingTravelSpeed(double start, double target, double junction)
    {
        var animation = new CatalogExpansionAnimation(start, target);
        var incoming = animation.HeightAt(junction) - animation.HeightAt(junction - 0.001);
        var outgoing = animation.HeightAt(junction + 0.001) - animation.HeightAt(junction);
        Assert.InRange(outgoing / incoming, 0.999, 1.001);
    }

    [Fact]
    public void ShortExpansionUsesPclExtraStrongPower()
    {
        Assert.Equal(0.96875, new CatalogExpansionAnimation(0, 100).HeightAt(75) / 100);
        Assert.Equal(0.96875, CatalogExpansionAnimation.EaseOut(0.5));
    }
}
