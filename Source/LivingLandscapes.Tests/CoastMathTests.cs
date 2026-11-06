using System;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class CoastMathTests
{
    // A 12 x 1 strip: cells 0..4 sea (cell 4 beside the land), 5..11 land climbing away from the sea.
    private static (float[] height, bool[] water, float[] fromLand) Strip()
    {
        var height = new float[12];
        var water = new bool[12];
        for (int i = 0; i < 12; i++)
        {
            water[i] = i < 5;
            height[i] = 10f + 0.5f * i;
        }
        float[] fromLand = GridMath.DistanceTo(i => !water[i], 12, 1);
        return (height, water, fromLand);
    }

    [Fact]
    public void ShapeSeaFloor_LeavesEveryLandCellUntouched()
    {
        var (height, water, fromLand) = Strip();
        float[] before = (float[])height.Clone();
        CoastMath.ShapeSeaFloor(height, water, fromLand, 11f, cells => 0.1f * cells, null, null, 0f);
        for (int i = 5; i < 12; i++)
            Assert.Equal(before[i], height[i]);
    }

    [Fact]
    public void ShapeSeaFloor_PutsTheSeaBelowItsLevel_DeeperOffshore()
    {
        var (height, water, fromLand) = Strip();
        int sea = CoastMath.ShapeSeaFloor(height, water, fromLand, 11f, cells => 0.1f * cells, null, null, 0f);
        Assert.Equal(5, sea);
        for (int i = 0; i < 5; i++)
            Assert.True(height[i] < 11f);
        for (int i = 1; i < 5; i++)
            Assert.True(height[i - 1] < height[i], "the floor rises toward the shore");
        Assert.Equal(11f - 0.1f - CoastMath.FloorBelowShore, height[4], 4);
    }

    [Fact]
    public void ShapeSeaFloor_StacksStandAboveTheSea_AndAreNotCountedAsSea()
    {
        var (height, water, fromLand) = Strip();
        var resistant = new float[12];
        resistant[2] = 0.5f;
        int sea = CoastMath.ShapeSeaFloor(height, water, fromLand, 11f, cells => 0.1f * cells, i => i == 2, resistant, 6f);
        Assert.Equal(4, sea);
        Assert.Equal(14f, height[2], 4);
    }

    [Fact]
    public void ModelBaseLevel_IsOneHeight_InMetres()
    {
        // Model range 0..8 with the tile's typical ground (level 0.5) at 100 m and 9 m of relief: base level 0 sits half a relief below.
        Assert.Equal(95.5f, CoastMath.ModelToMetres(0f, 0f, 8f, 100f, 0.5f, 9f), 4);
        Assert.Equal(100f, CoastMath.ModelToMetres(4f, 0f, 8f, 100f, 0.5f, 9f), 4);
    }

    [Fact]
    public void RegionalRise_ClimbsFromTheSea_WhateverTheShoreDirection()
    {
        Assert.Equal(0f, CoastMath.RegionalRise(0.02f, 1f, 0f));
        Assert.Equal(0.4f, CoastMath.RegionalRise(0.02f, 1f, 20f), 5);
        Assert.Equal(0f, CoastMath.RegionalRise(0.02f, 1f, 20f, 0f));
        // a flat region still gets a lowland slope across the plain, then its own gradient beyond it
        Assert.Equal(0.3f, CoastMath.RegionalRise(0.0005f, 1f, 50f, 1f, 50f, 0.006f), 5);
        Assert.Equal(0.3f + 0.0005f * 50f, CoastMath.RegionalRise(0.0005f, 1f, 100f, 1f, 50f, 0.006f), 5);
        // a steeper region keeps its own slope on the plain
        Assert.Equal(0.02f * 30f, CoastMath.RegionalRise(0.02f, 1f, 30f, 1f, 50f, 0.006f), 5);
        // monotone in the distance from the sea
        Assert.True(CoastMath.RegionalRise(0.02f, 1f, 30f) > CoastMath.RegionalRise(0.02f, 1f, 20f));
    }

    [Fact]
    public void LandBesideTheSea_DrainsToIt_NoLipAtTheShore()
    {
        // The #185 shape: ground graded to base level (model height, rising from 0 at the sea) never stands above the next cell toward the sea.
        float seaLevel = CoastMath.ModelToMetres(0f, 0f, 8f, 100f, 0.5f, 9f);
        float last = float.MaxValue;
        for (int cells = 40; cells >= 0; cells--)
        {
            float modelHeight = 0.03f * cells;   // graded land: nothing below the base level
            float h = CoastMath.ModelToMetres(modelHeight, 0f, 8f, 100f, 0.5f, 9f) + CoastMath.RegionalRise(0.02f, 1f, cells);
            Assert.True(h <= last);
            Assert.True(h >= seaLevel);
            last = h;
        }
    }

    [Fact]
    public void PlainUplift_FadesToNothingAtTheSea_AndIsWholeBeyondThePlain()
    {
        Assert.Equal(0f, CoastMath.PlainUplift(0f, 50f));
        Assert.Equal(1f, CoastMath.PlainUplift(50f, 50f));
        Assert.Equal(1f, CoastMath.PlainUplift(300f, 50f));
        Assert.Equal(0.5f, CoastMath.PlainUplift(25f, 50f), 4);
        float last = -1f;
        for (int d = 0; d <= 60; d++)
        {
            float f = CoastMath.PlainUplift(d, 50f);
            Assert.True(f >= last, "never steeper uplift closer to the sea");
            last = f;
        }
    }
}
