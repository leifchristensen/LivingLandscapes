using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>HYDRO-S8 (#186): the rule for the water the map's own channels may carry on a tile without a world river (#141 purple 19774).</summary>
public class BoundaryMathTests
{
    // Purple 19774: width cap 3906.25, world flow 908 of a creek's 18160, a creek is 5625 map units.
    [Fact]
    public void WorldFlowBoundsTheTargetWithoutClimateSurplus() =>
        Assert.Equal(281.25f, BoundaryMath.OwnTarget(3906.25f, 908f, 18160f, 5625f, false), 2);

    [Fact] // #141: where rain beats evaporation the world's coarse flow must not cut the map's water to 2%
    public void ClimateSurplusKeepsTheWidthCapOnly() =>
        Assert.Equal(3906.25f, BoundaryMath.OwnTarget(3906.25f, 908f, 18160f, 5625f, true), 2);

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    [InlineData(-5f)]
    [InlineData(float.PositiveInfinity)]
    public void UnknownWorldFlowIsTheWidthCap(float flow)
    {
        Assert.Equal(3906.25f, BoundaryMath.OwnTarget(3906.25f, flow, 18160f, 5625f, false), 2);
        Assert.Equal(3906.25f, BoundaryMath.OwnTarget(3906.25f, flow, 18160f, 5625f, true), 2);
    }

    [Fact]
    public void ScaleBringsTheLargestChannelToTheTarget() =>
        Assert.Equal(0.2778f, BoundaryMath.ClampScale(3906.25f, 14062f), 3);

    [Fact]
    public void ScaleIsNeverAboveOneNorNaN()
    {
        Assert.Equal(1f, BoundaryMath.ClampScale(1000f, 0f));
        Assert.Equal(1f, BoundaryMath.ClampScale(float.NaN, 5f));
        Assert.Equal(1f, BoundaryMath.ClampScale(0f, 5f));
        Assert.Equal(1f, BoundaryMath.ClampScale(-3f, 5f));
        Assert.Equal(1f, BoundaryMath.ClampScale(1e9f, 5f));
    }
}
