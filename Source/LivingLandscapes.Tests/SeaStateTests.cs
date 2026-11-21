using System.Linq;
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

    /// <summary>Model heights: the land (left of column 12) stands 'land' high, the sea is 0.</summary>
    private static float[] Heights(float land)
    {
        var h = new float[GX * GZ];
        for (int z = 0; z < GZ; z++)
        {
            for (int x = 0; x < 12; x++)
                h[z * GX + x] = land;
        }
        return h;
    }

    private static SeaFillPass.Settings Settings(double wave = 0.0, float metresPerRiver = 200f, float referenceWidth = 6f) => new SeaFillPass.Settings
    {
        WaveShare = wave, SettleWidths = 5f, ReachLengths = 3f, SpreadRate = 0.12f,
        MapCellsPerModelCell = 2f, ReferenceWidthModelCells = 3f, KeepExit = -1, MetresPerReferenceRiver = metresPerRiver, ReferenceRiverWidthMapCells = referenceWidth,
    };

    [Fact]
    public void Run_MassClosesAndBedsNeverExceedTheirRoom()
    {
        SeaState sea = NewSea(0.5f);
        var (delivered, outlet, receiver, area) = River(sea, 400f);
        var landHeight = new float[GX * GZ];
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(0.3), landHeight);
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
    public void Exit_RunsOnAcrossNewLandToTheFirstOpenSea()
    {
        SeaState sea = NewSea();
        var (_, _, receiver, area) = River(sea, 1f);
        int exit = 15 * GX + 12;
        for (int x = 13; x <= 16; x++)
            Assert.True(sea.Promote(15 * GX + x, keep: exit));
        var walked = new List<int>();
        int built = SeaExit.Extend(sea, receiver, area, Heights(0f).Select((_, i) => sea.Sea[i] ? 0f : 0.3f).ToArray(), exit, walked);
        // The walk takes the first open sea it meets: the diagonal beside the last cell of land, not the straight line out to x = 17.
        Assert.True(sea.Sea[built]);
        Assert.True(built % GX <= 17);
        Assert.True(walked.Count >= 1 && walked.Count <= 4);
        // The chain from the exit reaches the new exit cell, which stays sea.
        int c = exit, steps = 0;
        while (receiver[c] >= 0 && steps++ < 20)
            c = receiver[c];
        Assert.Equal(built, c);
        Assert.True(sea.Sea[built]);
    }

    [Fact]
    public void Exit_FollowsTheLowestGroundNotAStraightLine()
    {
        SeaState sea = NewSea();
        var (_, _, receiver, area) = River(sea, 1f);
        int exit = 15 * GX + 12;
        for (int x = 13; x <= 16; x++)
        {
            for (int z = 13; z <= 17; z++)
                Assert.True(sea.Promote(z * GX + x, keep: exit));
        }
        // New land 0.3 high everywhere except a gully one row up (z = 14) at 0.05: the river should take it, not run straight east along z = 15.
        var height = new float[GX * GZ];
        for (int i = 0; i < height.Length; i++)
            height[i] = sea.Sea[i] ? 0f : 0.3f;
        for (int x = 13; x <= 16; x++)
            height[14 * GX + x] = 0.05f;
        var walked = new List<int>();
        int built = SeaExit.Extend(sea, receiver, area, height, exit, walked);
        Assert.True(sea.Sea[built]);
        Assert.Contains(14 * GX + 14, walked);
        Assert.Equal(walked.Count, walked.Distinct().Count());
        int c = exit, steps = 0;
        while (receiver[c] >= 0 && steps++ < 40)
            c = receiver[c];
        Assert.Equal(built, c);
    }

    [Fact]
    public void Exit_NeverClosesALoop_AndLeavesEverythingUntouchedWhenBlocked()
    {
        SeaState sea = NewSea();
        var (_, _, receiver, area) = River(sea, 1f);
        int exit = 15 * GX + 12;
        for (int x = 13; x <= 16; x++)
        {
            for (int z = 14; z <= 16; z++)
                Assert.True(sea.Promote(z * GX + x, keep: exit));
        }
        // A tributary already drains from the new land into the exit: the walk must not step onto it (that would make a loop).
        receiver[15 * GX + 13] = exit;
        area[15 * GX + 13] = 9f;
        var height = new float[GX * GZ];
        for (int i = 0; i < height.Length; i++)
            height[i] = sea.Sea[i] ? 0f : 0.3f;
        height[15 * GX + 13] = 0.0f; // the lowest ground is the tributary itself
        var walked = new List<int>();
        int built = SeaExit.Extend(sea, receiver, area, height, exit, walked);
        Assert.DoesNotContain(15 * GX + 13, walked);
        int c = exit, steps = 0;
        while (receiver[c] >= 0 && steps++ < 100)
            c = receiver[c];
        Assert.True(steps < 100, "the chain from the exit loops");
        Assert.Equal(built, c);

        // Every cell ahead already drains into the exit: no way on without a loop, so nothing changes.
        SeaState blocked = NewSea();
        var (_, _, r2, a2) = River(blocked, 1f);
        for (int z = 14; z <= 16; z++)
        {
            Assert.True(blocked.Promote(z * GX + 13, keep: exit));
            r2[z * GX + 13] = exit;
        }
        int[] before = (int[])r2.Clone();
        float[] areaBefore = (float[])a2.Clone();
        Assert.Equal(exit, SeaExit.Extend(blocked, r2, a2, new float[GX * GZ], exit, new List<int>()));
        Assert.Equal(before, r2);
        Assert.Equal(areaBefore, a2);
    }

    [Fact]
    public void Exit_StaysWhereItIsWhenTheSeaIsOpen()
    {
        SeaState sea = NewSea();
        var (_, _, receiver, area) = River(sea, 1f);
        int exit = 15 * GX + 12;
        Assert.True(sea.Promote(14 * GX + 14, keep: exit));
        Assert.Equal(exit, SeaExit.Extend(sea, receiver, area, new float[GX * GZ], exit, new List<int>()));
    }

    [Fact]
    public void Scale_DoesNotDependOnTheMagnitudeOfTheModelsLoad()
    {
        // The model's load has no physical size: the same river with a thousand times the load lays the same floor.
        float[] Beds(float load)
        {
            SeaState sea = NewSea(0.5f);
            var (delivered, outlet, receiver, area) = River(sea, load);
            SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(0.0, 20f), new float[GX * GZ]);
            Assert.Equal(r.Delivered, r.Retained + r.RemovedByWaves + r.NoRoom, 6);
            return sea.Bed;
        }
        float[] a = Beds(70f), b = Beds(70000f);
        for (int i = 0; i < a.Length; i++)
            Assert.Equal(a[i], b[i], 4);
        Assert.Contains(a, v => v > -0.5f); // and it did lay something
    }

    [Fact]
    public void Run_AWiderRiverOnTheSameCoastBuildsMoreLand()
    {
        // Same coast, same constant, same load in the model: the river whose mouth is twice as wide (four times the discharge) lays more floor.
        (int promoted, double floorRise, double metres) Build(float referenceWidth)
        {
            SeaState sea = NewSea(0.5f);
            var (delivered, outlet, receiver, area) = River(sea, 100f);
            SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(0.3, 1500f, referenceWidth), new float[GX * GZ]);
            return (sea.Promoted, sea.Bed.Sum() - NewSea(0.5f).Bed.Sum(), r.Metres);
        }
        var small = Build(12f); // mouth 6 map cells against a reference of 12: weight 1/4
        var huge = Build(6f);   // weight 1
        Assert.Equal(4.0, huge.metres / small.metres, 6);
        Assert.True(huge.floorRise > small.floorRise, $"floor rise huge {huge.floorRise} small {small.floorRise}");
        Assert.True(huge.promoted >= small.promoted, $"promoted huge {huge.promoted} small {small.promoted}");
        Assert.True(huge.promoted > 0);
    }

    [Theory]
    [InlineData(0f, 6f)]
    [InlineData(float.NaN, 6f)]
    [InlineData(float.PositiveInfinity, 6f)]
    [InlineData(0.5f, 0f)]
    [InlineData(0.5f, float.NaN)]
    public void Run_WithNoScaleEverythingIsNoRoomAndNothingIsLost(float metresPerRiver, float referenceWidth)
    {
        SeaState sea = NewSea(0.5f);
        var (delivered, outlet, receiver, area) = River(sea, 40f);
        float[] before = (float[])sea.Bed.Clone();
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(0.3, metresPerRiver, referenceWidth), new float[GX * GZ]);
        Assert.Equal(40.0, r.NoRoom, 9);
        Assert.Equal(40.0, r.Delivered, 9);
        Assert.Equal(0.0, r.Retained);
        Assert.Equal(0.0, r.RemovedByWaves);
        Assert.Equal(before, sea.Bed);
        Assert.Equal(0, r.PromotedCells);
    }

    [Fact]
    public void Run_TheJetStopsShortOfTheMapEdgeAndTheReachCap()
    {
        SeaState sea = NewSea(0.5f);
        var (delivered, outlet, receiver, area) = River(sea, 400f);
        var s = Settings(0.0, 3000f);
        // The map window ends 14 cells east of the mouth; with an 8 cell margin the lobe can reach 6 cells.
        s.WindowMinX = 0; s.WindowMinZ = 0; s.WindowMaxX = 12 + 14; s.WindowMaxZ = GZ; s.WindowMargin = 8f;
        s.MaxReach = 40f;
        var landHeight = new float[GX * GZ];
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, s, landHeight);
        Assert.True(r.PromotedCells > 0);
        foreach (int c in r.Cells)
            Assert.True(c % GX <= 12 + 6, $"cell at x={c % GX} promoted beyond the reach");
        Assert.True(r.NoRoom > 0.0, "load the jet cannot place within reach is noRoom, not carried on");
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
            SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(), landHeight);
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
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(50.0), landHeight);
        Assert.True(r.Retained < 0.021 * 0.01, $"retained {r.Retained}");
        Assert.Equal(0, r.PromotedCells);
    }

    [Fact]
    public void Run_NothingDeliveredChangesNothing()
    {
        SeaState sea = NewSea();
        var (delivered, outlet, receiver, area) = River(sea, 0f);
        float[] before = (float[])sea.Bed.Clone();
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(), new float[GX * GZ]);
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
            SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 1f, Settings(0.1), new float[GX * GZ]);
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
    public void Ledger_DroppedIsASinkAndTheErodedTermsCloseOnTheirOwn()
    {
        // The world river's yield is a million times the eroded ground, as in the real logs.
        var l = new ErosionLedger { Eroded = 1e6, Yield = 1e12, Kept = 4e5, Lake = 1e5, RetainedAtSea = 3e5, RemovedByWaves = 1e12 - 1e5, NoRoom = 5e4, LandEdgeOutlet = 3e5, Lost = 2e5, Dropped = 1e5 };
        // sinks = 4e5 + 1e5 + 3e5 + (1e12 - 1e5) + 5e4 + 3e5 + 2e5 + 1e5 = 1e12 + 1.35e6 - 1e5... computed here so the test reads the ledger, not a magic number
        double sinks = l.Kept + l.Lake + l.RetainedAtSea + l.RemovedByWaves + l.NoRoom + l.LandEdgeOutlet + l.Lost + l.Dropped;
        l.Eroded = sinks - l.Yield;
        Assert.True(Math.Abs(1.0 - l.Closure) < 1e-12);
        Assert.True(Math.Abs(1.0 - l.ErodedClosure) < 1e-9, $"eroded closure {l.ErodedClosure}");
        // Dropped counts: forget it and the eroded closure shows it.
        double dropped = l.Dropped;
        l.Dropped = 0.0;
        Assert.True(Math.Abs(1.0 - l.ErodedClosure) > 0.05, $"eroded closure {l.ErodedClosure}");
        l.Dropped = dropped;
        // An error of 1% of the eroded ground is invisible to the whole closure and plain to the eroded one.
        l.Lost += 0.01 * l.Eroded;
        Assert.True(Math.Abs(1.0 - l.Closure) < 1e-5);
        Assert.True(Math.Abs(1.0 - l.ErodedClosure) > 0.009);
    }

    [Fact]
    public void Ledger_OfASeaFillPassClosesToTheDelivery()
    {
        SeaState sea = NewSea(0.5f);
        var (delivered, outlet, receiver, area) = River(sea, 7f);
        var landHeight = new float[GX * GZ];
        SeaPassResult r = SeaFillPass.Run(sea, delivered, outlet, receiver, area, Heights(5f), 3f, Settings(0.3), landHeight);
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
