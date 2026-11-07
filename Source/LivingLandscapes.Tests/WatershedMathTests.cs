using System;
using System.Collections.Generic;
using System.Linq;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class WatershedMathTests
{
    // A small catchment. Tile 0 is the sea. Main stem 1 -> 2 -> 3 -> 4 -> 0 is wholly above the threshold. Tributary 5 -> 6 -> 3
    // is above it; a small side stream 7 -> 8 -> 2 is below it. Tile 9 -> 10 -> 11 drains into a closed basin (11 drains nowhere).
    private static readonly int[] Down = { -1, 2, 3, 4, 0, 6, 3, 8, 2, 10, 11, -1 };
    private static readonly float[] Flow = { 0, 60, 90, 200, 400, 55, 70, 10, 20, 80, 90, 100 };
    private static readonly bool[] Water = { true, false, false, false, false, false, false, false, false, false, false, false };

    private static bool[] None() => new bool[Down.Length];
    private static bool Never(int a, int b) => false;

    [Fact]
    public void ADrainageForestAboveTheThresholdBecomesRivers_WithItsTributaries()
    {
        List<int> add = WatershedMath.LinksToAdd(Down, Flow, Water, None(), 50f, Never);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, add.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void AChainThatEndsInAClosedBasin_IsNotDrawn()
    {
        List<int> add = WatershedMath.LinksToAdd(Down, Flow, Water, None(), 50f, Never);
        Assert.DoesNotContain(9, add);
        Assert.DoesNotContain(10, add);
        Assert.DoesNotContain(11, add);
    }

    [Fact]
    public void ClosedBasinChains_AreDrawnOnlyWhenAsked()
    {
        var withBasins = WatershedMath.LinksToAdd(Down, Flow, Water, None(), 50f, Never, endInBasins: true);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 9, 10 }, withBasins.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void LowerThreshold_AddsASupersetOfTheLinks()
    {
        var high = WatershedMath.LinksToAdd(Down, Flow, Water, None(), 80f, Never);
        var low = WatershedMath.LinksToAdd(Down, Flow, Water, None(), 50f, Never);
        Assert.All(high, i => Assert.Contains(i, low));
        Assert.True(low.Count > high.Count);
    }

    [Fact]
    public void NoRivers_WhenTheThresholdIsInfinite_OrDensityIsZero()
    {
        Assert.Empty(WatershedMath.LinksToAdd(Down, Flow, Water, None(), float.PositiveInfinity, Never));
        Assert.True(float.IsPositiveInfinity(WatershedMath.Threshold(50000f, 0f)));
    }

    [Fact]
    public void Threshold_ScalesInverselyWithDensity()
    {
        Assert.Equal(50000f, WatershedMath.Threshold(50000f, 1f));
        Assert.Equal(100000f, WatershedMath.Threshold(50000f, 0.5f));
        Assert.Equal(25000f, WatershedMath.Threshold(50000f, 2f));
    }

    [Fact]
    public void ExistingLinks_AreNotDuplicated_AndExistingRiversAreSinks()
    {
        // Tiles 3 and 4 already carry a river (a mainstem from the sea to 3). Tile 3 is a sink for the chains above it; its own link is not added again.
        var onRiver = None();
        onRiver[3] = true;
        onRiver[4] = true;
        Func<int, int, bool> linked = (a, b) => (a == 3 && b == 4) || (a == 4 && b == 0);
        List<int> add = WatershedMath.LinksToAdd(Down, Flow, Water, onRiver, 50f, linked);
        Assert.Equal(new[] { 1, 2, 5, 6 }, add.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void RunningTwice_AddsNothingTheSecondTime()
    {
        var first = WatershedMath.LinksToAdd(Down, Flow, Water, None(), 50f, Never);
        var onRiver = None();
        var linkSet = new HashSet<(int, int)>();
        foreach (int i in first)
        {
            onRiver[i] = true;
            onRiver[Down[i]] = true;
            linkSet.Add((i, Down[i]));
        }
        Assert.Empty(WatershedMath.LinksToAdd(Down, Flow, Water, onRiver, 50f, (a, b) => linkSet.Contains((a, b)) || linkSet.Contains((b, a))));
    }

    [Fact]
    public void ACycle_IsNotDrawn()
    {
        int[] down = { -1, 2, 3, 1 };
        float[] flow = { 0, 100, 100, 100 };
        bool[] water = { true, false, false, false };
        Assert.Empty(WatershedMath.LinksToAdd(down, flow, water, new bool[4], 50f, Never));
    }

    [Fact]
    public void EveryAddedLink_FollowsTheDrainage_AndReachesASink()
    {
        var onRiver = None();
        List<int> add = WatershedMath.LinksToAdd(Down, Flow, Water, onRiver, 50f, Never);
        var set = new HashSet<int>(add);
        foreach (int i in add)
        {
            int t = i, steps = 0;
            while (set.Contains(t) && steps++ < 100)
                t = Down[t];
            Assert.True(Water[t] || onRiver[t], $"chain from {i} ends at {t}");
        }
    }

    [Fact]
    public void NonDecreasing_CarriesATributarysFlowPastADip()
    {
        // 1 -> 2 -> 3 -> 0 (sea); tile 2 is a lake outlet reporting less than tile 1; a side stream 4 joins tile 3 with more than tile 2.
        int[] down = { -1, 2, 3, 0, 3 };
        float[] flow = { 0, 100, 40, 60, 80 };
        float[] env = WatershedMath.NonDecreasing(down, flow);
        Assert.Equal(new float[] { 100, 100, 100, 100, 80 }, env); // the sea receives it too
        for (int i = 1; i < down.Length; i++)
            Assert.True(env[down[i]] >= env[i] || down[i] == 0);
    }

    [Fact]
    public void StrahlerOrders_CountTributaryJunctions()
    {
        // Two first-order streams (5 and 7) meet at 6: order 2. A first-order stream (8) joins 6's stream below at 9: stays 2.
        // Another order-2 stream (10) meets it at 11: order 3.
        int[] down = { -1, -1, -1, -1, -1, 6, 9, 6, 9, 11, 11, 0 };
        bool[] inForest = Enumerable.Range(0, 12).Select(i => i >= 5).ToArray();
        // Make 10 an order-2 stream of its own: children 12 and 13.
        down = down.Concat(new[] { 10, 10 }).ToArray();
        inForest = inForest.Concat(new[] { true, true }).ToArray();
        int[] order = WatershedMath.StrahlerOrders(down, inForest);
        Assert.Equal(1, order[5]);
        Assert.Equal(2, order[6]);
        Assert.Equal(2, order[9]);
        Assert.Equal(2, order[10]);
        Assert.Equal(3, order[11]);
    }

    [Fact]
    public void DownstreamFirst_PutsEveryTileAfterTheOneItDrainsTo()
    {
        List<int> add = new List<int> { 1, 2, 3, 4, 5, 6 };
        List<int> order = WatershedMath.DownstreamFirst(add, Down);
        foreach (int i in add)
        {
            if (add.Contains(Down[i]))
                Assert.True(order.IndexOf(Down[i]) < order.IndexOf(i), $"{i} before {Down[i]}");
        }
        Assert.Equal(add.OrderBy(x => x), order.OrderBy(x => x));
    }
}
