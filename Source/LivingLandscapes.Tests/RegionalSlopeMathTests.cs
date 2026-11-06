using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class RegionalSlopeMathTests
{
    // relief by hilliness as shipped (EnvironmentLayers.xml): flat, small hills, large hills, mountainous
    private const float Flat = 4f, SmallHills = 20f, LargeHills = 70f, Mountainous = 180f;

    [Fact]
    public void SameRuggedness_InheritsNoSlope()
    {
        foreach (float relief in new[] { Flat, SmallHills, LargeHills, Mountainous })
            Assert.Equal(0f, RegionalSlopeMath.NeighbourWeight(relief, relief, 0f));
    }

    [Fact]
    public void ABoundary_InheritsTheSlope_MoreTheSharperTheContrast()
    {
        float plainToHills = RegionalSlopeMath.NeighbourWeight(Flat, SmallHills, 0f);
        float plainToMountains = RegionalSlopeMath.NeighbourWeight(Flat, Mountainous, 0f);
        Assert.InRange(plainToHills, 0.5f, 1f);
        Assert.True(plainToMountains >= plainToHills);
        Assert.InRange(RegionalSlopeMath.NeighbourWeight(LargeHills, Mountainous, 0f), 0.5f, 1f);
        // the same from either side: the foot slope belongs to both tiles
        Assert.Equal(plainToHills, RegionalSlopeMath.NeighbourWeight(SmallHills, Flat, 0f));
    }

    [Fact]
    public void UniformShare_IsAFloor_AndWeightsStayInRange()
    {
        Assert.Equal(0.25f, RegionalSlopeMath.NeighbourWeight(Flat, Flat, 0.25f));
        Assert.Equal(1f, RegionalSlopeMath.NeighbourWeight(Flat, Flat, 1f));
        Assert.Equal(0f, RegionalSlopeMath.NeighbourWeight(0f, 0f, 0f));
        Assert.Equal(0f, RegionalSlopeMath.NeighbourWeight(Flat, Flat, -1f));
        Assert.InRange(RegionalSlopeMath.NeighbourWeight(Flat, Mountainous, 0.9f), 0.9f, 1f);
    }
}
