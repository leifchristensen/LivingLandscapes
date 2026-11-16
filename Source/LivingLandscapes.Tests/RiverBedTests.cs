using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class RiverBedTests
{
    private static void AssertNeverClimbs(float[] bed)
    {
        for (int k = 1; k < bed.Length; k++)
            Assert.True(bed[k] < bed[k - 1], $"bed climbs or stalls at {k}");
    }

    [Theory]
    [InlineData(new float[] { 10, 9, 8, 7, 6 })]        // falling ground
    [InlineData(new float[] { 5, 6, 7, 8, 9 })]         // rising ground
    [InlineData(new float[] { 8, 3, 9, 2, 10, 1, 7 })]  // bumpy ground
    public void BedNeverClimbsAndLiesBelowTheGround(float[] ground)
    {
        float[] bed = RiverBed.Grade(ground, 0.5f, 0.1f);
        AssertNeverClimbs(bed);
        for (int k = 0; k < ground.Length; k++)
            Assert.True(bed[k] <= ground[k] - 0.5f - 0.1f + 1e-5f);
    }

    [Fact]
    public void FlatGroundFallsByExactlyTheFall()
    {
        float[] bed = RiverBed.Grade(new float[] { 4, 4, 4, 4 }, 1f, 0.25f);
        for (int k = 1; k < bed.Length; k++)
            Assert.Equal(0.25f, bed[k - 1] - bed[k], 4);
    }

    [Fact]
    public void FallFollowsTheGradientAndFallsBackWhenItIsNotUsable()
    {
        Assert.Equal(0.02f, RiverBed.FallPerCell(0.002f, 10f), 5);
        Assert.Equal(RiverBed.MinFallPerCell, RiverBed.FallPerCell(0f, 10f));
        Assert.Equal(RiverBed.MinFallPerCell, RiverBed.FallPerCell(float.NaN, 10f));
        Assert.Equal(RiverBed.MinFallPerCell, RiverBed.FallPerCell(-1f, 10f));
    }

    [Fact]
    public void NanGradientStillGivesAFallingBed() =>
        AssertNeverClimbs(RiverBed.Grade(new float[] { 3, 6, 2, 8 }, 0.2f, RiverBed.FallPerCell(float.NaN, 5f)));
}
