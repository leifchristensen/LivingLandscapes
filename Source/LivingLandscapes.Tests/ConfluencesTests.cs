using System.Collections.Generic;
using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Step S5 of the hydrology unification: junction zones read the tributaries' lost cross momentum (<see cref="Hydraulics.JoinPush"/>):
/// strength from f = |push| / Q, zone side from the push, slow-water speed u (1 - f). The old field-angle path stays for the flag off.
/// </summary>
public class ConfluencesTests
{
    private const int SizeX = 21, SizeZ = 11, JoinCell = 5 * SizeX + 10;

    private static int At(int x, int z) => z * SizeX + x;

    /// <summary>A main river east along row 5 (discharge 10) and a tributary falling from the north (row 0..4, x 10) into (10,5).</summary>
    private static (List<int> cells, int[] receiver, int[] main, float[] discharge, Vector2[] direction, float[] width) Tee(float tributaryQ, float arriveDx = 0f)
    {
        int n = SizeX * SizeZ;
        var receiver = new int[n];
        var main = new int[n];
        for (int i = 0; i < n; i++)
            receiver[i] = main[i] = -1;
        var discharge = new float[n];
        var direction = new Vector2[n];
        var cells = new List<int>();
        for (int x = 0; x < SizeX; x++)
        {
            int c = At(x, 5);
            discharge[c] = x < 10 ? 10f : 10f + tributaryQ;
            if (x < SizeX - 1)
            {
                receiver[c] = At(x + 1, 5);
                main[At(x + 1, 5)] = c;
            }
            direction[c] = new Vector2(1f, 0f);
        }
        for (int z = 1; z < 5; z++)
        {
            int c = At(10, z);
            discharge[c] = tributaryQ;
            receiver[c] = z < 4 ? At(10, z + 1) : JoinCell;
            direction[c] = new Vector2(0f, 1f);
        }
        main[JoinCell] = At(9, 5);
        for (int i = 0; i < n; i++)
        {
            if (discharge[i] > 0f)
                cells.Add(i);
        }
        // Highest first: a stand-in height, upstream cells first.
        var order = new Dictionary<int, int>();
        for (int x = 0; x < SizeX; x++)
            order[At(x, 5)] = 100 - x;
        for (int z = 1; z < 5; z++)
            order[At(10, z)] = 110 - z;
        cells.Sort((a, b) => order[b].CompareTo(order[a]));
        var width = new float[n];
        for (int i = 0; i < n; i++)
            width[i] = 2f;
        return (cells, receiver, main, discharge, direction, width);
    }

    private static ConfluenceZones Zones(float tributaryQ, bool fromPush, out Vector2[] push)
    {
        var (cells, receiver, main, discharge, direction, width) = Tee(tributaryQ);
        push = null;
        Vector2[] stem = null;
        if (fromPush)
        {
            Vector2[] total = Hydraulics.Momentum(cells, receiver, discharge, SizeX);
            push = Hydraulics.JoinPush(cells, receiver, main, discharge, SizeX, null, null, total, out stem);
        }
        return Confluences.Find(cells, receiver, main, discharge, direction, width, null, SizeX, new ConfluenceSettings(), push, stem);
    }

    [Fact]
    public void FromPush_TheZoneLiesOnTheTributarysBank_AsTheOldTestPlaced()
    {
        ConfluenceZones now = Zones(6f, true, out _);
        ConfluenceZones old = Zones(6f, false, out _);
        int below = At(11, 5);
        Assert.True(now.SlowShare[below] > 0f);
        // The tributary comes from the north (smaller z): both place the slow side toward it.
        Assert.True(now.SlowSide[below].y < 0f);
        Assert.Equal(old.SlowSide[below].y < 0f, now.SlowSide[below].y < 0f);
    }

    [Fact]
    public void FromPush_StrengthIsTheCrossShare_NotTheShareAlone()
    {
        // The tributary arrives square across the stem: f = Q_t / Q_join = 6 / 16 = 0.375 (above fullShare 0.3: full strength).
        var (cells, receiver, main, discharge, _, _) = Tee(6f);
        Vector2[] total = Hydraulics.Momentum(cells, receiver, discharge, SizeX);
        Vector2[] push = Hydraulics.JoinPush(cells, receiver, main, discharge, SizeX, null, null, total, out _);
        Assert.Equal(6f / 16f, push[JoinCell].magnitude / discharge[JoinCell], 2);
    }

    [Fact]
    public void SlowWaterSpeed_FollowsTheLostCrossMomentum()
    {
        var settings = new ConfluenceSettings();
        ConfluenceZones z = Zones(6f, true, out Vector2[] push);
        var (_, _, _, discharge, _, _) = Tee(6f);
        float f = push[JoinCell].magnitude / discharge[JoinCell];
        float expected = Mathf.Pow(Mathf.Max(settings.slowSpeed, 1f - f), 2f);
        Assert.NotNull(z.SlowCapacityAt);
        Assert.Equal(expected, z.SlowCapacityAt[At(11, 5)], 3);
        // A bigger tributary loses more momentum, so its slow water is slower.
        ConfluenceZones big = Zones(30f, true, out _);
        Assert.True(big.SlowCapacityAt[At(11, 5)] < z.SlowCapacityAt[At(11, 5)]);
    }

    [Fact]
    public void ATrickle_ChangesNothing()
    {
        ConfluenceZones z = Zones(0.2f, true, out _);
        Assert.Equal(0, z.Junctions);
        foreach (float s in z.Scour)
            Assert.Equal(1f, s);
    }

    [Fact]
    public void FromPush_IsDeterministic()
    {
        ConfluenceZones a = Zones(6f, true, out _), b = Zones(6f, true, out _);
        Assert.Equal(a.SlowShare, b.SlowShare);
        Assert.Equal(a.SlowCapacityAt, b.SlowCapacityAt);
        Assert.Equal(a.Scour, b.Scour);
        Assert.Equal(a.SlowSide, b.SlowSide);
    }

    [Fact]
    public void WithoutPush_ThePerCellCapacityIsAbsent_SoTheOldFixedFloorApplies()
    {
        ConfluenceZones z = Zones(6f, false, out _);
        Assert.Null(z.SlowCapacityAt);
        Assert.Equal(new ConfluenceSettings().slowSpeed * new ConfluenceSettings().slowSpeed, z.SlowCapacity, 5);
    }

    [Fact]
    public void ScourIsDeepestAtTheJunction_AndNeverBelowOne()
    {
        ConfluenceZones z = Zones(6f, true, out _);
        float max = 0f;
        int at = -1;
        for (int i = 0; i < z.Scour.Length; i++)
        {
            Assert.True(z.Scour[i] >= 1f);
            if (z.Scour[i] > max)
            {
                max = z.Scour[i];
                at = i;
            }
        }
        Assert.Equal(JoinCell, at);
        Assert.True(max <= new ConfluenceSettings().maxScour + 1e-4f);
    }

    [Fact]
    public void Audit_FlagsAChannelNarrowerThanItsArms_AndPassesOneThatIsNot()
    {
        var (cells, receiver, main, discharge, direction, width) = Tee(6f);
        var moving = new bool[width.Length];
        foreach (int c in cells)
            moving[c] = true;
        Vector2[] total = Hydraulics.Momentum(cells, receiver, discharge, SizeX);
        Vector2[] push = Hydraulics.JoinPush(cells, receiver, main, discharge, SizeX, null, null, total, out Vector2[] stem);
        ConfluenceZones zones = Confluences.Find(cells, receiver, main, discharge, direction, width, null, SizeX, new ConfluenceSettings(), push, stem);
        var deposit = new float[width.Length];
        deposit[At(11, 5)] = 1f;
        JunctionAudit fine = Confluences.Audit(zones, cells, receiver, width, moving, deposit, SizeX);
        Assert.Equal(1, fine.Joins);
        Assert.Equal(0, fine.Narrowed);
        Assert.Equal(0, fine.ScourOffCentre);
        Assert.Equal(0, fine.NoDeposit);
        // A thread below the join (the #220 picture): width 1 for the cells below an arm of width 2.
        for (int x = 10; x < SizeX; x++)
            width[At(x, 5)] = 0.5f;
        JunctionAudit thread = Confluences.Audit(zones, cells, receiver, width, moving, deposit, SizeX);
        Assert.Equal(1, thread.Narrowed);
        Assert.True(thread.WorstWidthRatio < 0.8f);
        // No deposit in the zone is reported.
        Assert.Equal(1, Confluences.Audit(zones, cells, receiver, width, moving, new float[width.Length], SizeX).NoDeposit);
    }

    [Fact]
    public void WidthContinuity_HoldsTheJoinedWidthBelowAJoin_AndEasesBack_WithoutWideningElsewhere()
    {
        var (cells, receiver, _, _, _, width) = Tee(6f);
        var moving = new bool[width.Length];
        foreach (int c in cells)
            moving[c] = true;
        for (int x = 0; x < SizeX; x++)
            width[At(x, 5)] = x < 10 ? 4f : 1f;
        float[] before = (float[])width.Clone();
        Confluences.WidthContinuity(cells, receiver, width, moving, 0.04f);
        // The joined width is the root of the arms' squares (4 and the tributary's 2).
        Assert.Equal(Mathf.Sqrt(4f * 4f + 2f * 2f), width[JoinCell], 3);
        for (int x = 11; x < SizeX; x++)
            Assert.True(width[At(x, 5)] >= width[At(x - 1, 5)] - 0.2f && width[At(x, 5)] >= before[At(x, 5)]);
        // Upstream of the join nothing changed.
        for (int x = 0; x < 10; x++)
            Assert.Equal(before[At(x, 5)], width[At(x, 5)]);
        Assert.True(Confluences.Audit(Zones(6f, true, out _), cells, receiver, width, moving, new float[width.Length], SizeX).Narrowed == 0);
    }
}
