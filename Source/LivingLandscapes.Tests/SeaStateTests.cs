using System;
using System.Collections.Generic;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Step D3 (the sea fills in the erosion model): the sea mask promotes a cell once and never back, a region-edge cell and the river's exit
/// stay sea, the beds never go above their room, the sea fill and the ledger close, and the yield divisor follows the outlets.
/// </summary>
public class SeaStateTests
{
    private const int GX = 40, GZ = 31;

    private static SeaState NewSea(float depth = 1f)
    {
        // Land on the left third, sea to the right of column 12.
        var depths = new float[GX * GZ];
        for (int z = 0; z < GZ; z++)
        {
            for (int x = 12; x < GX; x++)
                depths[z * GX + x] = depth;
        }
        return new SeaState(GX, GZ, depths);
    }

    [Fact]
    public void Promote_TurnsASeaCellIntoLandOnce()
    {
        SeaState sea = NewSea();
        int cell = 15 * GX + 20;
        Assert.True(sea.Sea[cell]);
        Assert.True(sea.Promote(cell));
        Assert.False(sea.Sea[cell]);
        Assert.Equal(1, sea.Promoted);
        int version = sea.Version;
        Assert.False(sea.Promote(cell)); // already land: refused, nothing counted twice
        Assert.Equal(1, sea.Promoted);
        Assert.Equal(version, sea.Version);
    }

    [Fact]
    public void Promote_RefusesRegionEdgeLandAndTheExit()
    {
        SeaState sea = NewSea();
        Assert.False(sea.Promote(15 * GX + GX - 1)); // region edge
        Assert.False(sea.Promote(0 * GX + 20));      // region edge
        Assert.False(sea.Promote(5 * GX + 3));       // land from the start
        Assert.False(sea.Promote(15 * GX + 20, keep: 15 * GX + 20)); // the river's exit stays an outlet
        Assert.True(sea.Sea[15 * GX + 20]);
        Assert.Equal(0, sea.Promoted);
    }

    /// <summary>A river dropping 'delivered' into the sea cell at (12, 15), draining east; every other cell is empty.</summary>
    private static (float[] delivered, bool[] outlet, int[] receiver, float[] area) River(SeaState sea, float delivered)
    {
        var d = new float[GX * GZ];
        var outlet = new bool[GX * GZ];
        var receiver = new int[GX * GZ];
        var area = new float[GX * GZ];
        for (int i = 0; i < receiver.Length; i++)
        {
            receiver[i] = -1;
            area[i] = 1f;
            outlet[i] = sea.Sea[i];
        }
        int mouth = 15 * GX + 12;
        receiver[15 * GX + 11] = mouth;
        area[15 * GX + 11] = 50f;
        d[mouth] = delivered;
        return (d, outlet, receiver, area);
    }

    private static SeaFillPass.Settings Settings(double wave = 0.0) => new SeaFillPass.Settings
    {
        WaveCapacityPerWidth = wave, SettleWidths = 5f, ReachLengths = 3f, SpreadRate = 0.12f,
        MapCellsPerModelCell = 2f, ReferenceWidthModelCells = 3f, KeepExit = -1,
    };

    [Fact]
    public void Run_MassClosesAndBedsNeverExceedTheirRoom()
    {
        SeaState sea = NewSea(0.5f);
        var (delivered, outlet, receiver, area) = River(sea, 400f);
        var landHeight = new float[GX * GZ];
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, 1f, Settings(0.2), landHeight);
        Assert.Equal(400.0, r.Retained + r.RemovedByWaves + r.NoRoom, 6);
        Assert.Equal(r.Delivered, r.Retained + r.RemovedByWaves + r.NoRoom, 6);
        // Room is never negative: a bed rises only to the mouth's depth (the promotion height), never past it by more than float rounding.
        float depth = Math.Max(SeaFillPass.MinMouthDepth, SeaFillPass.MouthDepthPerRootWidth * (float)Math.Sqrt(3f * 2f));
        for (int i = 0; i < sea.Bed.Length; i++)
        {
            Assert.True(sea.Bed[i] <= depth + 1e-4f, $"bed {sea.Bed[i]} above the promotion height {depth}");
            if (!sea.Sea[i] && i % GX >= 12)
                Assert.True(sea.Bed[i] >= depth - 1e-4f, "a promoted cell reached the promotion height");
        }
        Assert.True(r.PromotedCells > 0);
        foreach (int c in r.Cells)
            Assert.True(landHeight[c] > 0f);
    }

    [Fact]
    public void Run_PromotedCellsStayLandOverRepeatedPasses()
    {
        SeaState sea = NewSea(0.3f);
        var landHeight = new float[GX * GZ];
        var promoted = new HashSet<int>();
        for (int pass = 0; pass < 6; pass++)
        {
            var (delivered, outlet, receiver, area) = River(sea, 3f);
            SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, 1f, Settings(), landHeight);
            foreach (int c in r.Cells)
                Assert.True(promoted.Add(c), "a cell was promoted twice");
            foreach (int c in promoted)
                Assert.False(sea.Sea[c], "a promoted cell went back to sea");
        }
        Assert.Equal(promoted.Count, sea.Promoted);
    }

    [Fact]
    public void Run_WavesStrongerThanTheRiverKeepNothingMuch()
    {
        SeaState sea = NewSea(0.5f);
        var (delivered, outlet, receiver, area) = River(sea, 0.01f);
        var landHeight = new float[GX * GZ];
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, 1f, Settings(50.0), landHeight);
        Assert.True(r.Retained < 0.01 * 0.001, $"retained {r.Retained}");
        Assert.Equal(0, r.PromotedCells);
    }

    [Fact]
    public void Run_NothingDeliveredChangesNothing()
    {
        SeaState sea = NewSea();
        var (delivered, outlet, receiver, area) = River(sea, 0f);
        float[] before = (float[])sea.Bed.Clone();
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, 1f, Settings(), new float[GX * GZ]);
        Assert.Equal(before, sea.Bed);
        Assert.Equal(0.0, r.Delivered);
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        float[] Once()
        {
            SeaState sea = NewSea(0.4f);
            var (delivered, outlet, receiver, area) = River(sea, 2f);
            SeaFillPass.Run(sea, delivered, outlet, receiver, area, 1f, Settings(0.1), new float[GX * GZ]);
            return sea.Bed;
        }
        Assert.Equal(Once(), Once());
    }

    [Fact]
    public void Ledger_ClosesWhenEverythingIsAccountedFor()
    {
        var l = new ErosionLedger { Eroded = 10, Yield = 5, Kept = 6, Lake = 1, RetainedAtSea = 3, RemovedByWaves = 2, NoRoom = 0.5, LandEdgeOutlet = 1.5, Lost = 1 };
        Assert.True(Math.Abs(1.0 - l.Closure) < 1e-5);
        l.Lost += 0.01;
        Assert.True(Math.Abs(1.0 - l.Closure) > 1e-5);
    }

    [Fact]
    public void Ledger_OfASeaFillPassClosesToTheDelivery()
    {
        SeaState sea = NewSea(0.5f);
        var (delivered, outlet, receiver, area) = River(sea, 7f);
        var landHeight = new float[GX * GZ];
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, 3f, Settings(0.3), landHeight);
        var l = new ErosionLedger { Eroded = 7, RetainedAtSea = r.Retained, RemovedByWaves = r.RemovedByWaves, NoRoom = r.NoRoom };
        Assert.True(Math.Abs(1.0 - l.Closure) < 1e-5, $"closure {l.Closure}");
    }

    [Fact]
    public void YieldDivisor_CountsOnlyCellsThatAreNotOutlets()
    {
        var eroded = new float[] { 1f, 2f, 3f, 4f };
        var outlet = new[] { false, false, true, true };
        Assert.Equal(1.5, ErosionLedger.YieldDivisor(eroded, outlet, out int land), 6);
        Assert.Equal(2, land);
        outlet[2] = false; // a sea cell filled to land: one more land cell shares the same eroded total
        Assert.Equal(2.0, ErosionLedger.YieldDivisor(eroded, outlet, out land), 6);
        Assert.Equal(3, land);
    }

    [Fact]
    public void ErosionDeposit_YieldFollowsTheOutletsEachPass()
    {
        // Two land cells in a row draining to a base-level cell; a world river brings 10 cells of inflow into the first.
        float[] Run(bool[] outlet)
        {
            var h = new float[] { 3f, 2f, 0f };
            var eroded = new float[] { 0.5f, 0.9f, 0f };
            var load = new float[3];
            var inflow = new float[] { 10f, 0f, 0f };
            var receiver = new[] { 1, 2, -1 };
            var area = new float[] { 11f, 12f, 13f };
            var stack = new[] { 2, 1, 0 };
            var delivered = new float[3];
            ErosionDeposit.ApplyWithLedger(h, eroded, load, inflow, receiver, area, stack, 3, outlet, new bool[3], new float[3], new float[3],
                1f, delivered, out _, out _);
            return delivered;
        }
        float a = Run(new[] { false, false, true })[2];
        float b = Run(new[] { false, true, true })[2]; // the second cell is an outlet now: fewer land cells, so a bigger yield per cell
        Assert.NotEqual(a, b);
    }
}
