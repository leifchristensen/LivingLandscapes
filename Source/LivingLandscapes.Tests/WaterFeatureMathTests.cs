using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>The headwater basin and river island knob are inputs to the erosion model (W5, P23 and P2), laid as a disk.</summary>
public class WaterFeatureMathTests
{
    [Fact]
    public void ABasinTakesUpliftAwayAndLeavesTheRockAlone()
    {
        var disk = WaterFeatureMath.Disk(0.1f, 0.9f, 1f);
        Assert.Equal(-0.9f, disk.upliftInside, 5);
        Assert.Equal(1f, disk.erodibility, 5);
        Assert.Equal(0.1f, disk.size, 5);
    }

    [Fact]
    public void AKnobSoftensNothingAndRaisesNoUplift()
    {
        var disk = WaterFeatureMath.Disk(0.06f, 0f, 0.2f);
        Assert.Equal(0f, disk.upliftInside, 5);
        Assert.Equal(0.2f, disk.erodibility, 5);
    }

    [Fact]
    public void OutOfRangeInputsAreKeptSane()
    {
        var disk = WaterFeatureMath.Disk(-1f, 5f, -3f);
        Assert.Equal(0f, disk.size, 5);
        Assert.Equal(-1f, disk.upliftInside, 5);
        Assert.True(disk.erodibility > 0f);
    }

    [Fact]
    public void RiseAboveRingIsNegativeForABasinAndPositiveForAKnob()
    {
        var h = new float[21 * 21];
        for (int i = 0; i < h.Length; i++)
            h[i] = 10f;
        h[10 * 21 + 10] = 7f;
        Assert.Equal(-3f, WaterFeatureMath.RiseAboveRing(h, 21, 21, new Vector2(10, 10), 5f, 8f), 4);
        h[10 * 21 + 10] = 14f;
        Assert.Equal(4f, WaterFeatureMath.RiseAboveRing(h, 21, 21, new Vector2(10, 10), 5f, 8f), 4);
    }

    [Fact]
    public void EdgeTowardStopsAtTheMapEdge()
    {
        Vector2 edge = WaterFeatureMath.EdgeToward(new Vector2(10, 10), new Vector2(1, 0), 21, 21);
        Assert.True(edge.x > 19f && edge.x < 21f);
        Assert.Equal(10f, edge.y, 3);
    }
}
