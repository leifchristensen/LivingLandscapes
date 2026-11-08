using System;
using System.Collections.Generic;
using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Steps S3a-S3c of the hydrology unification (#186): sediment carried as separate loads per grain class (gravel, sand, fines).
/// Guards: one class is the old lumped load exactly; every class conserves mass (supplied = deposited + left at the end); the capacity
/// is shared among the classes the water can move and never exceeded; coarse settles before fine; joins add and splits share the
/// loads; the jet's washload is booked, not lost.
/// </summary>
public class GrainLoadsTests
{
    private const int SizeX = 24, SizeZ = 11, Row = 5;

    private sealed class Channel
    {
        public readonly List<int> Cells = new List<int>();
        public readonly int[] Receiver = new int[SizeX * SizeZ];
        public readonly float[] Discharge = new float[SizeX * SizeZ];
        public readonly float[] Filled = new float[SizeX * SizeZ];
        public readonly float[] Inflow = new float[SizeX * SizeZ];
        public readonly float[] Width = new float[SizeX * SizeZ];
        public readonly float[] Speed = new float[SizeX * SizeZ];
        public readonly float[] Depth = new float[SizeX * SizeZ];
        public readonly bool[] BaseLevel = new bool[SizeX * SizeZ];

        public Channel(bool endsInBaseLevel, float speed = 1.2f, float depth = 1f)
        {
            for (int i = 0; i < Receiver.Length; i++)
            {
                Receiver[i] = -1;
                Width[i] = 2f;
                Speed[i] = speed;
                Depth[i] = depth;
            }
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                    Filled[z * SizeX + x] = 6f * (float)Math.Exp(-x / 5.0);
            }
            for (int x = 0; x < SizeX; x++)
            {
                int c = Row * SizeX + x;
                Cells.Add(c);
                Discharge[c] = 10f;
                Receiver[c] = x < SizeX - 1 ? c + 1 : -1;
            }
            Inflow[Row * SizeX] = 10f;
            BaseLevel[Row * SizeX + SizeX - 1] = endsInBaseLevel;
        }

        public GrainLoads Grains(SedimentSettings p) => new GrainLoads(p, Speed, Depth);

        public void Deposits(SedimentSettings p, GrainLoads grains, out float[] deposit, out float[] grain, out float total, out float atBase, out float eroded,
            out float exits) =>
            Sediment.Deposits(Cells, Receiver, Discharge, 1f, Filled, Inflow, null, BaseLevel, SizeX, p, out deposit, out grain, out total, out atBase, out eroded,
                out exits, grains: grains);
    }

    private static SedimentSettings Classes(bool settling = false, bool jets = false) =>
        new SedimentSettings { grainClasses = true, settlingLength = settling, jetClasses = jets };

    private static float Sum(float[] values)
    {
        float s = 0f;
        foreach (float v in values)
            s += v;
        return s;
    }

    /// <summary>Mean cell position along the channel of a class's deposit.</summary>
    private static float MeanPosition(float[] deposit)
    {
        float mass = 0f, moment = 0f;
        for (int x = 0; x < SizeX; x++)
        {
            float d = deposit[Row * SizeX + x];
            mass += d;
            moment += d * x;
        }
        return mass > 0f ? moment / mass : float.NaN;
    }

    [Fact]
    public void OneClassIsTheOldLumpedLoad()
    {
        foreach (bool baseLevel in new[] { false, true })
        {
            var channel = new Channel(baseLevel);
            var old = new SedimentSettings();
            channel.Deposits(old, null, out float[] depositOld, out _, out float totalOld, out float atBaseOld, out float erodedOld, out float exitsOld);
            var one = new SedimentSettings
            {
                grainClasses = true,
                grains = new List<GrainClass> { new GrainClass { name = "mixed", index = 0.5f, fallSpeed = 1f, criticalSpeed = 0f } },
            };
            channel.Deposits(one, channel.Grains(one), out float[] deposit, out _, out float total, out float atBase, out float eroded, out float exits);
            Assert.Equal(depositOld, deposit);
            Assert.Equal(totalOld, total);
            Assert.Equal(atBaseOld, atBase);
            Assert.Equal(erodedOld, eroded);
            Assert.Equal(exitsOld, exits);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void EveryClassConservesMass(bool baseLevel, bool settling, bool jets)
    {
        var channel = new Channel(baseLevel);
        var p = Classes(settling, jets);
        GrainLoads grains = channel.Grains(p);
        channel.Deposits(p, grains, out float[] deposit, out _, out float total, out _, out float eroded, out float exits);
        Assert.True(eroded > 0f && total > 0f);
        Assert.Equal(Sum(deposit), total, 3);
        Assert.Equal(eroded, total + exits, 2);
        float supplied = 0f;
        for (int k = 0; k < grains.Count; k++)
        {
            Assert.Equal(grains.Supplied[k], grains.Deposited[k] + grains.Exits[k], 2);
            Assert.Equal(grains.Deposited[k], Sum(grains.Deposit[k]), 3);
            supplied += grains.Supplied[k];
        }
        Assert.Equal(eroded, supplied, 2);
    }

    [Fact]
    public void CoarseSettlesBeforeFine()
    {
        var channel = new Channel(false);
        var p = Classes(settling: true);
        GrainLoads grains = channel.Grains(p);
        channel.Deposits(p, grains, out _, out _, out _, out _, out _, out _);
        float gravel = MeanPosition(grains.Deposit[0]), sand = MeanPosition(grains.Deposit[1]), fines = MeanPosition(grains.Deposit[2]);
        Assert.False(float.IsNaN(gravel) || float.IsNaN(sand) || float.IsNaN(fines));
        Assert.True(gravel < sand, $"gravel {gravel} settles upstream of sand {sand}");
        Assert.True(sand < fines, $"sand {sand} settles upstream of fines {fines}");
    }

    [Fact]
    public void SettlingLengthsOrderByFallSpeedAndSandIsTheOldFourPercent()
    {
        var p = Classes(settling: true);
        var grains = new GrainLoads(p, new float[1], new float[1]);
        Assert.True(grains.Length(0, 1f, 1f) < grains.Length(1, 1f, 1f));
        Assert.True(grains.Length(1, 1f, 1f) < grains.Length(2, 1f, 1f));
        // Sand at u d = 1 settles 4% of its excess over one cell, as settleShare did.
        Assert.Equal(p.settleShare, grains.Settled(1, 1f, 1f, 1f), 3);
        // Faster and deeper water carries a grain farther before it settles.
        Assert.True(grains.Length(1, 2f, 1f) > grains.Length(1, 1f, 1f));
        Assert.True(grains.Length(1, 1f, 2f) > grains.Length(1, 1f, 1f));
        // The length is clamped, so a dry or torrential cell cannot give zero or infinity.
        Assert.Equal(p.ellMin, grains.Length(0, 0f, 0f), 3);
        Assert.Equal(p.ellMax, grains.Length(2, 100f, 100f), 3);
    }

    [Fact]
    public void SlowWaterCannotCarryGravelButCarriesFines()
    {
        var p = Classes(settling: true);
        var grains = new GrainLoads(p, new float[1], new float[1]);
        var load = new[] { 1f, 1f, 1f };
        var excess = new float[3];
        grains.Excess(load, 1.5f, 0.15f, excess);
        Assert.True(excess[0] > 0.95f, "gravel is almost all excess in slow water");
        Assert.True(excess[2] < excess[0]);
        grains.Excess(load, 1.5f, 3f, excess);
        float carried = 0f;
        for (int k = 0; k < 3; k++)
            carried += load[k] - excess[k];
        Assert.True(carried <= 1.5f + 1e-4f, "the shares never add to more than the capacity");
        Assert.True(carried > 1.4f, "fast water uses the whole capacity");
    }

    [Fact]
    public void GravelAloneInSlowWaterIsNotCarriedOnTheWholeCapacity()
    {
        var p = Classes();
        var grains = new GrainLoads(p, new float[1], new float[1]);
        var excess = new float[3];
        grains.Excess(new[] { 1f, 0f, 0f }, 1f, 0.1f, excess);
        Assert.True(excess[0] > 0.9f);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.15f)]
    [InlineData(0.3f)]
    [InlineData(0.42f)]
    [InlineData(0.6f)]
    [InlineData(0.9f)]
    public void SupplyMixKeepsTheMeanGrainAndAddsToOne(float index)
    {
        var p = Classes();
        var grains = new GrainLoads(p, new float[1], new float[1]);
        var shares = new float[3];
        grains.Mix(index, shares);
        Assert.Equal(1f, shares[0] + shares[1] + shares[2], 4);
        float mean = shares[0] * 0.8f + shares[1] * 0.42f + shares[2] * 0.15f;
        Assert.Equal(Mathf.Clamp(index, 0.15f, 0.8f), mean, 3);
    }

    [Fact]
    public void AnEntryCarriesOnlyWhatItsSpeedCanMove()
    {
        var p = Classes(jets: true);
        var grains = new GrainLoads(p, new float[1], new float[1]);
        var fast = new float[3];
        var slow = new float[3];
        grains.EntryMix(0.6f, 3f, fast);
        grains.EntryMix(0.6f, 0.15f, slow);
        Assert.True(fast[0] > 0.1f, "a fast river brings gravel");
        Assert.True(slow[0] < 0.25f && slow[0] < fast[0] * 0.6f, "a slow river brings little gravel");
        Assert.True(slow[1] > fast[1], "a slow river brings a finer mix");
        Assert.Equal(1f, fast[0] + fast[1] + fast[2], 4);
        Assert.Equal(1f, slow[0] + slow[1] + slow[2], 4);
        // Nothing movable falls back to the finest class.
        var none = new float[3];
        new GrainLoads(Classes(jets: true), new float[1], new float[1]).EntryMix(0.8f, 0f, none);
        Assert.Equal(1f, none[0] + none[1] + none[2], 4);
    }

    [Fact]
    public void ASplitAndRejoinConservesEveryClass()
    {
        // Cells 0 -> 1 -> 3 and a second way 0 -> 2 -> 3 (40% of the water), then 3 leaves.
        const int n = 4;
        var cells = new List<int> { 0, 1, 2, 3 };
        var receiver = new[] { 1, 3, 3, -1 };
        var secondary = new[] { 2, -1, -1, -1 };
        var share = new[] { 0.4f, 0f, 0f, 0f };
        var discharge = new[] { 10f, 6f, 4f, 10f };
        var filled = new[] { 4f, 3f, 3f, 2f };
        var inflow = new[] { 10f, 0f, 0f, 0f };
        var p = Classes(settling: true);
        var grains = new GrainLoads(p, new[] { 1.2f, 1.2f, 1.2f, 1.2f }, new[] { 1f, 1f, 1f, 1f });
        Sediment.Deposits(cells, receiver, discharge, 1f, filled, inflow, null, null, n, p, out float[] deposit, out _, out float total, out _, out float eroded,
            out float exits, secondary: secondary, secondaryShare: share, grains: grains);
        Assert.Equal(eroded, total + exits, 3);
        for (int k = 0; k < 3; k++)
            Assert.Equal(grains.Supplied[k], grains.Deposited[k] + grains.Exits[k], 3);
        Assert.True(deposit[2] > 0f || deposit[1] > 0f);
    }

    [Fact]
    public void TheJetBooksItsWashloadAndLaysEachClassOnItsOwnGrain()
    {
        var channel = new Channel(true);
        var p = Classes(settling: true, jets: true);
        p.typicalThickness = 1f;
        p.maxThickness = 100f;
        p.minThickness = 0f;
        GrainLoads grains = channel.Grains(p);
        channel.Deposits(p, grains, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        // Water is slow below the middle of the profile: make sure the base-level cell deposits something.
        Assert.True(deposit[Row * SizeX + SizeX - 1] > 0f);
        var ledger = new DepositLedger();
        LayTotals totals = Sediment.Spread(channel.Cells, channel.Receiver, null, deposit, grain, channel.Width, channel.Filled, channel.Filled, SizeX, SizeZ, p,
            ledger, out float[] alluvium, out float[] grainSize, channel.Discharge, channel.BaseLevel, grains: grains);
        Assert.True(totals.Washload > 0f, "the fines settle beyond the jet's reach and leave");
        Assert.Equal(totals.Offered, totals.Laid + totals.Clipped + totals.Unplaced + totals.Washload, 2);
        Assert.Equal(grains.Washload * ledger.Kappa, totals.Washload, 3);
        foreach (float g in grainSize)
            Assert.InRange(g, 0f, 0.8001f);
        Assert.True(Sum(alluvium) > 0f);
    }

    [Fact]
    public void WithoutTheJetSwitchNothingLeaves()
    {
        var channel = new Channel(true);
        var p = Classes(settling: true, jets: false);
        GrainLoads grains = channel.Grains(p);
        channel.Deposits(p, grains, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        LayTotals totals = Sediment.Spread(channel.Cells, channel.Receiver, null, deposit, grain, channel.Width, channel.Filled, channel.Filled, SizeX, SizeZ, p,
            new DepositLedger(), out _, out _, channel.Discharge, channel.BaseLevel, grains: grains);
        Assert.Equal(0f, totals.Washload);
        Assert.Equal(totals.Offered, totals.Laid + totals.Clipped + totals.Unplaced, 2);
    }

    [Fact]
    public void FinesReachFartherAcrossThePlainThanGravel()
    {
        var channel = new Channel(false);
        var p = Classes(settling: true);
        p.typicalThickness = 1f;
        p.maxThickness = 100f;
        p.minThickness = 0f;
        GrainLoads grains = channel.Grains(p);
        channel.Deposits(p, grains, out float[] deposit, out float[] grain, out _, out _, out _, out _);
        Sediment.Spread(channel.Cells, channel.Receiver, null, deposit, grain, channel.Width, channel.Filled, channel.Filled, SizeX, SizeZ, p, new DepositLedger(),
            out float[] alluvium, out float[] grainSize, grains: grains);
        // Beside the channel the ground is coarser than at the edge of the plain.
        int near = (Row + 1) * SizeX + 6, far = (Row + 4) * SizeX + 6;
        Assert.True(alluvium[near] > 0f && alluvium[far] > 0f);
        Assert.True(grainSize[near] > grainSize[far], $"near {grainSize[near]} coarser than far {grainSize[far]}");
    }

    [Fact]
    public void TheSameInputGivesTheSameDeposit()
    {
        var channel = new Channel(true);
        var p = Classes(settling: true, jets: true);
        GrainLoads a = channel.Grains(p), b = channel.Grains(p);
        channel.Deposits(p, a, out float[] first, out _, out _, out _, out _, out _);
        channel.Deposits(p, b, out float[] second, out _, out _, out _, out _, out _);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Invariant 9 (DevTools): grain along a river's deposits fines downstream. A hand-made 6-cell river whose deposits fine
    /// passes; one whose deposits coarsen by 0.5 grain-index units from source to mouth fails. The rule is the running mean of
    /// the deposit grain index walking downstream: it may not climb above the lowest mean already passed by more than
    /// <see cref="SedimentSettings.coarsenTolerance"/>.
    /// </summary>
    [Fact]
    public void InvariantNinePassesAFiningRiverAndFailsACoarseningOne()
    {
        float tolerance = new SedimentSettings().coarsenTolerance;
        var cells = new List<int>();
        for (int x = 0; x < 6; x++)
            cells.Add(Row * SizeX + x);
        var deposit = new float[SizeX * SizeZ];
        var grain = new float[SizeX * SizeZ];
        foreach (int c in cells)
            deposit[c] = 1f;

        // Fining downstream: gravel at the source, fines at the mouth (the grain table's own indices, coarsest first).
        float[] finer = { 0.8f, 0.65f, 0.5f, 0.42f, 0.3f, 0.15f };
        for (int x = 0; x < 6; x++)
            grain[cells[x]] = finer[x];
        float rise = GrainLoads.WorstDownstreamCoarsening(cells, grain, deposit);
        Assert.True(rise <= tolerance, $"a fining river must pass, measured a running-mean climb of {rise:F3} over the tolerance {tolerance:F2}");

        // Coarsening by 0.5: the mouth's deposit is half a grain-index unit coarser than the source's.
        float[] coarser = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f };
        for (int x = 0; x < 6; x++)
            grain[cells[x]] = coarser[x];
        rise = GrainLoads.WorstDownstreamCoarsening(cells, grain, deposit);
        Assert.True(rise > tolerance, $"a river coarsening by 0.5 must fail, measured only {rise:F3}");

        // A river that deposits in fewer than two cells has nothing to measure.
        var bare = new float[SizeX * SizeZ];
        Assert.True(float.IsNaN(GrainLoads.WorstDownstreamCoarsening(cells, grain, bare)));
    }
}
