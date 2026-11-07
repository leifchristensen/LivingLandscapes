using System.Collections.Generic;
using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Step S2b of the hydrology unification: Impact reads the one reach slope and follows both donors at a confluence
/// (<see cref="Hydraulics.Speeds"/>, <see cref="Hydraulics.Momentum"/> with splits). The flag-off path is the S2a code, still
/// guarded by <see cref="HydraulicsTests"/> (equality with the old Impact, Spread and budget code).
/// </summary>
public class HydraulicsOneSlopeTests
{
    private const int JoinX = 7, JoinZ = 3;
    private const float ManningN = 0.035f, JetWidths = 5f;

    /// <summary>
    /// A hand-made join: a slow main river along the middle row (0,1) to (6,1) and a steep tributary falling from (3,0) into the
    /// join cell (3,1). Cells are ordered highest first.
    /// </summary>
    private static (List<int> cells, int[] receiver, float[] discharge, float[] filled, float[] depth, float[] width, int join, int tributary, int mainDonor) Join()
    {
        int n = JoinX * JoinZ;
        int At(int x, int z) => z * JoinX + x;
        var receiver = new int[n];
        for (int i = 0; i < n; i++)
            receiver[i] = -1;
        for (int x = 0; x < 6; x++)
            receiver[At(x, 1)] = At(x + 1, 1);
        receiver[At(3, 0)] = At(3, 1);
        var filled = new float[n];
        for (int i = 0; i < n; i++)
            filled[i] = -100f;
        for (int x = 0; x < JoinX; x++)
            filled[At(x, 1)] = 10f - 0.002f * x;
        filled[At(3, 0)] = 14f;
        var discharge = new float[n];
        for (int x = 0; x < JoinX; x++)
            discharge[At(x, 1)] = x < 3 ? 3f : 4f;
        discharge[At(3, 0)] = 1f;
        var depth = new float[n];
        var width = new float[n];
        for (int i = 0; i < n; i++)
        {
            depth[i] = 0.5f;
            width[i] = 2f;
        }
        var cells = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (discharge[i] > 0f)
                cells.Add(i);
        }
        cells.Sort((a, b) => filled[b].CompareTo(filled[a]));
        return (cells, receiver, discharge, filled, depth, width, At(3, 1), At(3, 0), At(2, 1));
    }

    [Fact]
    public void Speeds_KeepTheFasterTributarysJet_NotOnlyTheMainDonors()
    {
        var (cells, receiver, _, filled, depth, width, join, tributary, mainDonor) = Join();
        float[] speeds = Hydraulics.Speeds(cells, receiver, null, filled, depth, width, JoinX, ManningN, JetWidths);
        float length = JetWidths * Mathf.Max(width[join], 1f);
        float fromTributary = Hydraulics.Jet(speeds[tributary], Hydraulics.StepLength(tributary, join, JoinX), length);
        float fromMain = Hydraulics.Jet(speeds[mainDonor], Hydraulics.StepLength(mainDonor, join, JoinX), length);
        float own = Hydraulics.Speed(depth[join], Hydraulics.ReachSlope(join, receiver, filled, JoinX), ManningN);
        Assert.True(speeds[tributary] > speeds[mainDonor], "the tributary falls steeper, so it runs faster");
        Assert.True(fromTributary > fromMain && fromTributary > own, "fixture: the tributary's jet is what the join keeps");
        Assert.Equal(fromTributary, speeds[join]);
    }

    [Fact]
    public void Speeds_AreManningOnTheReachSlopeOrTheFastestDonorsJet()
    {
        var (cells, receiver, _, _, filled, depth, width) = HydraulicsTests.Network();
        float[] speeds = Hydraulics.Speeds(cells, receiver, null, filled, depth, width, HydraulicsTests.SizeX, ManningN, JetWidths);
        var donors = new Dictionary<int, List<int>>();
        foreach (int c in cells)
        {
            if (receiver[c] < 0)
                continue;
            if (!donors.TryGetValue(receiver[c], out List<int> list))
                donors[receiver[c]] = list = new List<int>();
            list.Add(c);
        }
        int joins = 0;
        foreach (int c in cells)
        {
            float expected = Hydraulics.Speed(depth[c], Hydraulics.ReachSlope(c, receiver, filled, HydraulicsTests.SizeX), ManningN);
            if (donors.TryGetValue(c, out List<int> from))
            {
                joins += from.Count > 1 ? 1 : 0;
                foreach (int d in from)
                    expected = Mathf.Max(expected, Hydraulics.Jet(speeds[d], Hydraulics.StepLength(d, c, HydraulicsTests.SizeX), JetWidths * Mathf.Max(width[c], 1f)));
            }
            Assert.Equal(expected, speeds[c]);
        }
        Assert.True(joins >= 3);
    }

    [Fact]
    public void Speeds_AreDeterministic_AndHandTheJetToASplitsSecondWay()
    {
        var (cells, receiver, _, filled, depth, width, join, tributary, _) = Join();
        var secondary = new int[filled.Length];
        for (int i = 0; i < secondary.Length; i++)
            secondary[i] = -1;
        int beside = 1 * JoinX + 4;
        secondary[tributary] = beside;
        float[] a = Hydraulics.Speeds(cells, receiver, secondary, filled, depth, width, JoinX, ManningN, JetWidths);
        float[] b = Hydraulics.Speeds(cells, receiver, secondary, filled, depth, width, JoinX, ManningN, JetWidths);
        Assert.Equal(a, b);
        float[] without = Hydraulics.Speeds(cells, receiver, null, filled, depth, width, JoinX, ManningN, JetWidths);
        float jet = Hydraulics.Jet(a[tributary], Hydraulics.StepLength(tributary, beside, JoinX), JetWidths * Mathf.Max(width[beside], 1f));
        Assert.True(a[beside] >= jet);
        Assert.True(a[beside] >= without[beside]);
        Assert.Equal(without[join], a[join]);
    }

    [Fact]
    public void Momentum_WithoutSplits_StillEqualsTheOldSpreadMomentum()
    {
        var (cells, receiver, _, discharge, filled, _, _) = HydraulicsTests.Network();
        var secondary = new int[filled.Length];
        for (int i = 0; i < secondary.Length; i++)
            secondary[i] = -1;
        Vector2[] old = HydraulicsTests.LegacyMomentum(cells, receiver, discharge, HydraulicsTests.SizeX, filled.Length);
        Vector2[] now = Hydraulics.Momentum(cells, receiver, discharge, HydraulicsTests.SizeX, secondary, new float[filled.Length]);
        for (int i = 0; i < old.Length; i++)
        {
            Assert.Equal(old[i].x, now[i].x);
            Assert.Equal(old[i].y, now[i].y);
        }
    }

    [Fact]
    public void Momentum_AtAJoin_IsTheDischargeWeightedSumOfBothDonors()
    {
        var (cells, receiver, discharge, _, _, _, join, _, _) = Join();
        Vector2[] p = Hydraulics.Momentum(cells, receiver, discharge, JoinX);
        // The main river (Q 3) arrives running east; the tributary (Q 1) arrives running along the grid's z.
        Assert.Equal(3f, p[join].x, 3);
        Assert.Equal(1f, p[join].y, 3);
    }

    [Fact]
    public void Momentum_OfOpposingDonors_CancelsAndTheNextCellFallsBackToItsOwnStep()
    {
        // Row 0: 0 -> 1 -> 2 <- 3 <- 4, two equal rivers meeting head on; the join drains down the grid (2 -> 7).
        const int sizeX = 5;
        var receiver = new[] { 1, 2, 7, 2, 3, -1, -1, -1, -1, -1 };
        var discharge = new float[10];
        for (int i = 0; i < 5; i++)
            discharge[i] = 1f;
        discharge[2] = 4f;
        Vector2[] p = Hydraulics.Momentum(new List<int> { 0, 4, 1, 3, 2 }, receiver, discharge, sizeX);
        Assert.True(p[2].magnitude < 1e-4f, "the opposing momenta cancel");
        Assert.Equal(0f, p[7].normalized.x, 4);
        Assert.Equal(1f, p[7].normalized.y, 4);
    }

    [Fact]
    public void Momentum_AtASplit_SendsEachWayItsShare_ReAimedAlongItsOwnStep()
    {
        const int sizeX = 4;
        // Cell 0 runs east into 1; cell 1 splits: its receiver is 2 (east), its second way 5 (south-east); 25% takes the second way.
        var receiver = new[] { 1, 2, -1, -1, -1, -1, -1, -1 };
        var secondary = new[] { -1, 5, -1, -1, -1, -1, -1, -1 };
        var share = new float[8];
        share[1] = 0.25f;
        var discharge = new float[8];
        discharge[0] = 1f;
        discharge[1] = 8f;
        var cells = new List<int> { 0, 1 };
        Vector2[] p = Hydraulics.Momentum(cells, receiver, discharge, sizeX, secondary, share);
        Assert.Equal(6f, p[2].magnitude, 3);
        Assert.Equal(2f, p[5].magnitude, 3);
        Assert.True(p[5].y > 0f && p[2].y == 0f, "the second way is re-aimed along its own (diagonal) step");
        // Without the split the one way keeps all of its water.
        Vector2[] whole = Hydraulics.Momentum(cells, receiver, discharge, sizeX);
        Assert.Equal(8f, whole[2].magnitude, 3);
        Assert.Equal(Vector2.zero, whole[5]);
    }

    [Fact]
    public void Momentum_IsDeterministic()
    {
        var (cells, receiver, _, discharge, filled, _, _) = HydraulicsTests.Network();
        var secondary = new int[filled.Length];
        var share = new float[filled.Length];
        for (int i = 0; i < secondary.Length; i++)
            secondary[i] = -1;
        Vector2[] a = Hydraulics.Momentum(cells, receiver, discharge, HydraulicsTests.SizeX, secondary, share);
        Vector2[] b = Hydraulics.Momentum(cells, receiver, discharge, HydraulicsTests.SizeX, secondary, share);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Speeds_WithTheOldPartsSwitchedOn_EqualTheOldImpactSpeeds()
    {
        var (cells, receiver, main, _, filled, depth, width) = HydraulicsTests.Network();
        float[] old = HydraulicsTests.LegacySpeeds(cells, main, filled, depth, width, HydraulicsTests.SizeX, 8, ManningN, JetWidths);
        float[] now = Hydraulics.Speeds(cells, receiver, null, filled, depth, width, HydraulicsTests.SizeX, ManningN, JetWidths, main, 8, reachSlope: false, allDonors: false);
        foreach (int c in cells)
            Assert.Equal(old[c], now[c]);
    }
}
