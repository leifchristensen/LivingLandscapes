using System.Collections.Generic;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>The pure sea fill (step D2): mass closure, room, front advance, symmetry, determinism, waves.</summary>
public class SeaFillTests
{
    private const int SX = 40, SZ = 31;
    private const float Level = 0f;
    private const double NoWaves = 0.0; // capacity 0: everything is kept

    private static float[] Bed(float depth)
    {
        var b = new float[SX * SZ];
        for (int i = 0; i < b.Length; i++)
            b[i] = -depth;
        return b;
    }

    /// <summary>A jet from the middle of the left edge pointing along +x.</summary>
    private static List<(int cell, float weight, float d)> Kernel(float[] bed, float w = 3f, float reach = 30f)
    {
        var k = new List<(int cell, float weight, float d)>();
        JetKernel.Weights((SZ / 2) * SX, 1f, 0f, w, float.PositiveInfinity, bed, SX, SZ, 6f, reach, 0.12f, k);
        return k;
    }

    private static double Total(float[] bed, float before)
    {
        double t = 0;
        foreach (float b in bed)
            t += b - before;
        return t;
    }

    [Theory]
    [InlineData(5.0, 0.0)]
    [InlineData(5.0, 2.0)]
    [InlineData(500.0, 0.5)]
    [InlineData(0.3, 0.01)]
    public void MassCloses_DeliveredIsRetainedPlusRemovedPlusNoRoom(double delivered, double capacity)
    {
        float[] bed = Bed(0.2f);
        var r = SeaFill.Apply(bed, Level, Kernel(bed), delivered, capacity, 3.0, 3.0);
        Assert.Equal(delivered, r.Retained + r.RemovedByWaves + r.NoRoom, 9);
        Assert.Equal(r.Retained, Total(bed, -0.2f), 3);
    }

    [Fact]
    public void BedsNeverRiseAboveSeaLevel_AndOnlyRise()
    {
        float[] bed = Bed(0.5f);
        float[] start = (float[])bed.Clone();
        SeaFill.Apply(bed, Level, Kernel(bed), 1000.0, NoWaves, 3.0, 3.0);
        for (int i = 0; i < bed.Length; i++)
        {
            Assert.True(bed[i] <= Level);
            Assert.True(bed[i] >= start[i]);
        }
    }

    [Fact]
    public void NothingDelivered_ChangesNothing()
    {
        float[] bed = Bed(1f);
        var r = SeaFill.Apply(bed, Level, Kernel(bed), 0.0, NoWaves, 3.0, 3.0);
        Assert.All(bed, b => Assert.Equal(-1f, b));
        Assert.Equal(0.0, r.Retained);
        Assert.Equal(0.0, r.RemovedByWaves);
        Assert.Equal(0.0, r.NoRoom);
        Assert.Empty(r.Promoted);
    }

    [Fact]
    public void TheFrontAdvancesWhenSupplyGrows()
    {
        float last = -1f;
        foreach (double supply in new[] { 0.5, 2.0, 8.0, 30.0, 100.0 })
        {
            float[] bed = Bed(0.1f);
            var r = SeaFill.Apply(bed, Level, Kernel(bed), supply, NoWaves, 3.0, 3.0);
            Assert.True(r.FrontDistance >= last, $"front {r.FrontDistance} fell below {last} at supply {supply}");
            last = r.FrontDistance;
        }
        Assert.True(last > 5f);
    }

    [Fact]
    public void OverflowMovesOnAlongTheAxis_NearCellsFillFirst()
    {
        float[] bed = Bed(0.05f);
        var k = Kernel(bed);
        var r = SeaFill.Apply(bed, Level, k, 10.0, NoWaves, 3.0, 3.0);
        int mouth = (SZ / 2) * SX;
        Assert.Equal(Level, bed[mouth]);
        Assert.Contains(mouth, r.Promoted);
        Assert.True(r.FrontDistance > 3f);
        Assert.Equal(0.0, r.NoRoom, 6);
    }

    [Fact]
    public void SymmetricAxisAndFixture_GiveAMirrorSymmetricResult()
    {
        float[] bed = Bed(0.4f);
        SeaFill.Apply(bed, Level, Kernel(bed), 6.0, NoWaves, 3.0, 3.0);
        for (int z = 0; z < SZ; z++)
        {
            for (int x = 0; x < SX; x++)
                Assert.Equal(bed[z * SX + x], bed[(SZ - 1 - z) * SX + x], 4);
        }
    }

    [Fact]
    public void TwoRunsAreIdentical()
    {
        float[] a = Bed(0.3f), b = Bed(0.3f);
        var ra = SeaFill.Apply(a, Level, Kernel(a), 12.0, 1.5, 3.0, 4.0);
        var rb = SeaFill.Apply(b, Level, Kernel(b), 12.0, 1.5, 3.0, 4.0);
        Assert.Equal(a, b);
        Assert.Equal(ra.Retained, rb.Retained);
        Assert.Equal(ra.RemovedByWaves, rb.RemovedByWaves);
        Assert.Equal(ra.NoRoom, rb.NoRoom);
        Assert.Equal(ra.Promoted, rb.Promoted);
    }

    [Fact]
    public void WaveCapacityFarAboveSupply_KeepsNothingMeaningful()
    {
        float[] bed = Bed(1f);
        var r = SeaFill.Apply(bed, Level, Kernel(bed), 1.0, 1e6, 3.0, 3.0);
        Assert.True(r.Retained < 1e-5);
        Assert.Equal(1.0, r.Retained + r.RemovedByWaves + r.NoRoom, 9);
        Assert.True(r.RemovedByWaves > 0.999999);
    }

    [Fact]
    public void RetainedLoad_IsSmoothBelowSupply_AndUsesTheMouthWidthAsAFloor()
    {
        double qs = 4.0;
        double kept = SeaFill.RetainedLoad(qs, 1.0, 2.0, 4.0); // Q_w = 4
        Assert.Equal(qs * (1.0 - System.Math.Exp(-1.0)), kept, 12);
        Assert.Equal(kept, SeaFill.RetainedLoad(qs, 1.0, 0.0, 4.0), 12); // front width 0 floors at the mouth width
        Assert.True(SeaFill.RetainedLoad(qs, 1.0, 2.0, 1.0) < qs);
        Assert.Equal(qs, SeaFill.RetainedLoad(qs, 0.0, 3.0, 3.0), 12);
        Assert.Equal(0.0, SeaFill.RetainedLoad(0.0, 1.0, 3.0, 3.0));
        Assert.True(SeaFill.RetainedLoad(8.0, 1.0, 3.0, 3.0) > SeaFill.RetainedLoad(4.0, 1.0, 3.0, 3.0));
    }

    [Fact]
    public void CellsWithNoRoomGetNothing()
    {
        float[] bed = Bed(0.5f);
        var k = Kernel(bed);
        int full = (SZ / 2) * SX + 2;
        bed[full] = Level; // already at the sea level
        var r = SeaFill.Apply(bed, Level, k, 2.0, NoWaves, 3.0, 3.0);
        Assert.Equal(Level, bed[full]);
        Assert.DoesNotContain(full, r.Promoted);
        Assert.Equal(2.0, r.Retained + r.NoRoom, 9);
    }

    [Fact]
    public void WhenNoCellHasRoom_AllKeptLoadIsNoRoom()
    {
        float[] bed = Bed(0f);
        var r = SeaFill.Apply(bed, Level, Kernel(bed), 3.0, NoWaves, 3.0, 3.0);
        Assert.Equal(0.0, r.Retained);
        Assert.Equal(3.0, r.NoRoom, 12);
        Assert.Empty(r.Promoted);
    }

    [Fact]
    public void EmptyKernel_ReportsEverythingKeptAsNoRoom()
    {
        float[] bed = Bed(1f);
        var r = SeaFill.Apply(bed, Level, new List<(int cell, float weight, float d)>(), 5.0, NoWaves, 3.0, 3.0);
        Assert.Equal(5.0, r.NoRoom, 12);
        Assert.Equal(0.0, r.Retained);
        Assert.All(bed, b => Assert.Equal(-1f, b));
    }

    [Fact]
    public void MoreThanTheShelfCanHold_FillsEveryReachableCellAndReportsTheRest()
    {
        float[] bed = Bed(0.1f);
        var k = Kernel(bed);
        double room = k.Count * 0.1;
        var r = SeaFill.Apply(bed, Level, k, room + 5.0, NoWaves, 3.0, 3.0);
        foreach (var (cell, _, _) in k)
            Assert.True(bed[cell] > -1e-5f);
        Assert.Equal(5.0, r.NoRoom, 3);
        Assert.Equal(room + 5.0, r.Retained + r.RemovedByWaves + r.NoRoom, 9);
    }

    [Fact]
    public void PromotionNeedsTheBedWithinEpsTOfTheLevel()
    {
        float[] strict = Bed(0.2f), tidal = Bed(0.2f);
        var rs = SeaFill.Apply(strict, Level, Kernel(strict), 0.5, NoWaves, 3.0, 3.0, 0f);
        var rt = SeaFill.Apply(tidal, Level, Kernel(tidal), 0.5, NoWaves, 3.0, 3.0, 0.15f);
        Assert.True(rt.Promoted.Count >= rs.Promoted.Count);
        Assert.All(rt.Promoted, c => Assert.True(tidal[c] >= Level - 0.15f));
    }

    [Fact]
    public void Room_IsTheGapToSeaLevel_AndNeverNegative()
    {
        Assert.Equal(2.0, SeaFill.Room(0.0, -2.0));
        Assert.Equal(0.0, SeaFill.Room(0.0, 1.0));
        Assert.Equal(0.0, SeaFill.Room(0.0, 0.0));
    }
}
