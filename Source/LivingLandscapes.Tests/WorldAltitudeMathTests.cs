using System;
using System.Linq;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class WorldAltitudeMathTests
{
    /// <summary>A row of tiles: tile 0 is the sea, the rest land, each touching the next.</summary>
    private static int[][] Chain(int n) => Enumerable.Range(0, n)
        .Select(i => new[] { i - 1, i + 1 }.Where(j => j >= 0 && j < n).ToArray()).ToArray();

    private static bool[] SeaAtStart(int n) => Enumerable.Range(0, n).Select(i => i == 0).ToArray();

    private static float[] Filled(int n, float value) => Enumerable.Repeat(value, n).ToArray();

    [Fact]
    public void Plain_RisesAwayFromTheCoast_AndStaysSmooth()
    {
        // A chain of flat land with no range: only the continental uplift. The sea end is 0; the far end is a no-flux
        // edge, so height rises monotonically inland and every step is small.
        int n = 12;
        float[] h = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), Filled(n, 0f), Filled(n, 0f), 2f);
        Assert.Equal(0f, h[0]);
        for (int i = 1; i < n; i++)
            Assert.True(h[i] > h[i - 1], $"tile {i} should stand higher than {i - 1}");
        Assert.True(h[1] < h[n - 1]);
        Assert.InRange(h[n - 1], 100f, 400f);
    }

    [Fact]
    public void Range_StandsHighest_AndFallsAwayToHillsAndPlains()
    {
        // Sea, plain, plain, hills, mountains, mountains, mountains, plain
        int n = 8;
        float[] target = { 0, 0, 0, 900, 3000, 3000, 3000, 0 };
        float[] stiff = { 0, 0, 0, 1, 2, 2, 2, 0 };
        float[] h = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), target, stiff, 2f);
        Assert.True(h[5] > h[4] && h[4] > h[3] && h[3] > h[2] && h[2] > h[1], "heights fall from the crest to the coast");
        Assert.True(h[5] > 2500f, "the middle of a range stands near its height");
        Assert.True(h[1] < h[5] / 3f, "the coastal plain stays low");
        Assert.True(h[2] < h[4], "an inland plain stands lower than the nearby range");
        Assert.True(h[1] < h[2], "a plain rises inland from the coast");
    }

    [Fact]
    public void Heights_StayBetweenTheSeaAndTheHighestSource()
    {
        int n = 10;
        float[] target = { 0, 0, 500, 3000, 0, 300, 0, 0, 4500, 0 };
        float[] stiff = { 0, 0, 1, 2, 0, 0.5f, 0, 0, 3, 0 };
        float[] h = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), target, stiff, 0f);
        Assert.All(h, v => Assert.InRange(v, 0f, 4500f));
        float[] withUplift = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), target, stiff, 2f);
        Assert.True(withUplift.Zip(h, (a, b) => a >= b - 0.05f).All(x => x), "uplift never lowers the land");
    }

    [Fact]
    public void ASolution_IsItsOwnFixedPoint()
    {
        int n = 9;
        float[] target = { 0, 0, 300, 3000, 3000, 900, 0, 0, 0 };
        float[] stiff = { 0, 0, 0.5f, 2, 2, 1, 0, 0, 0 };
        float[] solved = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), target, stiff, 2f, tolerance: 0.0001f);
        float[] again = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), target, stiff, 2f, solved, tolerance: 0.0001f);
        for (int i = 0; i < n; i++)
            Assert.Equal(solved[i], again[i], 0.05f);
    }

    [Fact]
    public void ALoneMountain_StandsLowerThanTheMiddleOfAWideRange()
    {
        int n = 15;
        var lone = Filled(n, 0f); var loneK = Filled(n, 0f);
        lone[7] = 3000f; loneK[7] = 2f;
        var wide = Filled(n, 0f); var wideK = Filled(n, 0f);
        for (int i = 5; i <= 9; i++) { wide[i] = 3000f; wideK[i] = 2f; }
        float[] a = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), lone, loneK, 0f);
        float[] b = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), wide, wideK, 0f);
        Assert.True(a[7] < b[7]);
        Assert.True(b[7] < 3000f);
    }

    [Fact]
    public void NoSea_NoSource_StaysAtZero()
    {
        int n = 5;
        float[] h = WorldAltitudeMath.Solve(Chain(n), SeaAtStart(n), Filled(n, 0f), Filled(n, 0f), 0f);
        Assert.All(h, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void EnforceDescent_LiftsUpstreamTilesOnly_AndKeepsAnAlreadyDescendingRiver()
    {
        // river 3 -> 2 -> 1 (downstream first): tile 2 lies lower than the tile it drains to, tile 3 already above its own
        float[] h = { 0f, 100f, 40f, 500f };
        WorldAltitudeMath.EnforceDescent(h, new[] { (2, 1), (3, 2) }, 1f);
        Assert.Equal(101f, h[2]);
        Assert.Equal(500f, h[3]);
        Assert.Equal(100f, h[1]);
        float[] again = (float[])h.Clone();
        WorldAltitudeMath.EnforceDescent(again, new[] { (2, 1), (3, 2) }, 1f);
        Assert.Equal(h, again);
    }

    [Fact]
    public void DistanceToSea_CountsTilesFromTheCoast()
    {
        int[] d = WorldAltitudeMath.DistanceToSea(Chain(5), SeaAtStart(5));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, d);
        int[] none = WorldAltitudeMath.DistanceToSea(Chain(3), new bool[3]);
        Assert.All(none, v => Assert.Equal(int.MaxValue, v));
    }
}
