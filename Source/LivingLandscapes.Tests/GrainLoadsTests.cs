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
/// loads; the jet's washload is booked, not lost; the mouth jet thins monotonically along its axis and books every class to its supply.
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

    /// <summary>
    /// The jet test the plan asks for (HYDRO-UNIFICATION section 10 point 14, #186): monotone decay along the axis plus mass
    /// closure, not equality with the old kernel. The fixture river ends inland (mouth at x = 19) so the jet's axis lies on the
    /// map: row 5, x = 19..23, straight downstream (the momentum at the mouth of a straight channel is exactly +x). The mouth sits
    /// where the profile 6 e^(-x/5) has gone gentle (slope under ~0.055 = the coarseSlope 0.15 threshold times the mix factor):
    /// the reach above it erodes a supply fine enough (grain index under the sand class's 0.42, Sediment.Grains.cs:237) to carry
    /// fines at all. Two checks:
    /// (1) walking away from the mouth, each class's deposit per cell never grows; (2) fed only one class's load, the jet lays
    /// that class on the map or lets it leave past its reach as washload, and the two add back to the supply Deposits booked.
    /// Only the fines' settling length (jetSettleWidths x w x reference/fall x jetReachLengths = 120 cells at w = 2) exceeds
    /// jetMaxReach (60), so only they leave; gravel (7.5) and sand (30) are laid in full.
    /// </summary>
    [Fact]
    public void TheJetDecaysAlongItsAxisAndBooksEveryClassToItsSupply()
    {
        const int Mouth = 19;
        int n = SizeX * SizeZ;
        var cells = new List<int>();
        var receiver = new int[n];
        var discharge = new float[n];
        var filled = new float[n];
        var inflow = new float[n];
        var width = new float[n];
        var speed = new float[n];
        var depth = new float[n];
        var baseLevel = new bool[n];
        for (int i = 0; i < n; i++)
        {
            receiver[i] = -1;
            width[i] = 2f;
            speed[i] = 1.2f;
            depth[i] = 1f;
            filled[i] = 6f * (float)Math.Exp(-(i % SizeX) / 5.0); // falls downstream, so the jet's ground lies below its level
        }
        for (int x = 0; x <= Mouth; x++)
        {
            int c = Row * SizeX + x;
            cells.Add(c);
            discharge[c] = 10f;
            receiver[c] = x < Mouth ? c + 1 : -1;
        }
        inflow[Row * SizeX] = 10f;
        baseLevel[Row * SizeX + Mouth] = true;

        var p = Classes(settling: true, jets: true);
        p.typicalThickness = 1f;
        p.maxThickness = 1e6f; // no clipping: the closure must be the jet's, not the ledger's cap
        p.minThickness = 0f;
        GrainLoads grains = new GrainLoads(p, speed, depth);
        Sediment.Deposits(cells, receiver, discharge, 1f, filled, inflow, null, baseLevel, SizeX, p,
            out float[] deposit, out float[] grain, out _, out _, out _, out _, grains: grains);
        Assert.True(deposit[Row * SizeX + Mouth] > 0f, "the base-level mouth deposits the load the jet carries");

        // All three classes at once: the ledger closes on itself and the axis only thins away from the mouth.
        var combinedLedger = new DepositLedger();
        LayTotals combined = Sediment.Spread(cells, receiver, null, deposit, grain, width, filled, filled, SizeX, SizeZ, p,
            combinedLedger, out float[] alluviumAll, out _, discharge, baseLevel, grains: grains);
        Assert.Equal(0f, combined.Unplaced, 2);
        Assert.Equal(combined.Offered, combined.Laid + combined.Clipped + combined.Unplaced + combined.Washload, 2);
        AssertThinsAlongAxis(alluviumAll, Mouth, "all classes together");

        // One class at a time: the same jet fed only that class's load. Then the volume laid on the map plus the washload that
        // leaves past the reach is exactly the supply Deposits booked for the class (within 1e-4).
        for (int k = 0; k < grains.Count; k++)
        {
            var only = new GrainLoads(p, speed, depth) { Deposit = new float[grains.Count][] };
            for (int j = 0; j < grains.Count; j++)
                only.Deposit[j] = j == k ? grains.Deposit[j] : new float[n];
            var classDeposit = (float[])grains.Deposit[k].Clone();
            var classGrain = new float[n];
            for (int i = 0; i < n; i++)
                classGrain[i] = p.grains[k].index;
            var ledger = new DepositLedger();
            LayTotals t = Sediment.Spread(cells, receiver, null, classDeposit, classGrain, width, filled, filled, SizeX, SizeZ, p,
                ledger, out float[] alluvium, out _, discharge, baseLevel, grains: only);
            string cls = p.grains[k].name;
            Assert.Equal(0f, t.Unplaced, 4);
            Assert.Equal(0f, t.Clipped, 4);
            // The jet was offered exactly the class's supply, and laid it on the map or let it leave: map + washload = supply,
            // within 1e-4 (the plan's closure tolerance).
            AssertClose(grains.Supplied[k], t.Offered / ledger.Kappa, $"{cls}: offered vs supplied");
            AssertClose(grains.Supplied[k], (t.Laid + t.Washload) / ledger.Kappa, $"{cls}: map + washload vs supplied");
            AssertClose(only.Washload, t.Washload / ledger.Kappa, $"{cls}: grains washload vs ledger washload");
            float fullReach = p.jetSettleWidths * 2f * grains.ReferenceFall / p.grains[k].fallSpeed * p.jetReachLengths;
            Assert.True((fullReach > p.jetMaxReach) == (t.Washload > 0f),
                $"{cls}: reach {fullReach} vs jetMaxReach {p.jetMaxReach}, washload {t.Washload}");
            AssertThinsAlongAxis(alluvium, Mouth, cls);
            Assert.True(alluvium[Row * SizeX + Mouth + 1] > 0f, $"{cls} lays along the axis past the mouth");
        }
    }

    /// <summary>Two budget-unit volumes agree within 1e-4 (absolute near zero, relative above): the plan's mass-closure tolerance.</summary>
    private static void AssertClose(float expected, float actual, string what)
    {
        Assert.True(Math.Abs(expected - actual) <= 1e-4f * Math.Max(1f, Math.Abs(expected)),
            $"{what}: expected {expected:E4}, got {actual:E4}");
    }

    /// <summary>The jet's deposit per cell along the axis (the mouth's row, from the mouth to the map edge) never grows downstream.</summary>
    private static void AssertThinsAlongAxis(float[] alluvium, int mouth, string what)
    {
        for (int x = mouth; x < SizeX - 1; x++)
        {
            float a = alluvium[Row * SizeX + x], b = alluvium[Row * SizeX + x + 1];
            Assert.True(b - a <= 1e-4f * Math.Max(1f, a),
                $"{what}: the jet's deposit grows from x = {x} ({a:E4}) to x = {x + 1} ({b:E4}) away from the mouth");
        }
        Assert.True(alluvium[Row * SizeX + mouth + 4] < alluvium[Row * SizeX + mouth],
            $"{what}: the jet's deposit must fall along its axis");
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
    /// The settling law's prerequisite (HYDRO-UNIFICATION section 10, #186): every consumer of the settling constants and of a
    /// deposit's mean grain index is listed here and pinned. If one of these defaults moves, every consumer below moves with it,
    /// so the change must be deliberate and this test updated on purpose. No XML sets any of these values (grep over *.xml finds
    /// none), so the code defaults are the live values.
    ///
    /// Consumers found by grep (2026-11-09):
    /// - settleShare (0.04): Sediment.cs:201 (share of the excess load settling in one cell), Sediment.cs:208 (slow share below a
    ///   junction), Sediment.Grains.cs:124 (the per-class settling length falls back to it when the length switch is off).
    /// - jetSettleWidths (5): Sediment.cs:253 (a mouth jet settles its load over this many channel widths), Sediment.cs:420
    ///   (a class's jet reach scales the settling length by the reference fall speed over its own).
    /// - jetReachLengths (3): Sediment.cs:253 and :421 (the jet reaches this many settling lengths).
    /// - settlingCompression (24.5), ellMin (1), ellMax (400): Sediment.Grains.cs:120, the per-class settling length
    ///   Length = Clamp(settlingCompression x max(u,0.05) x max(d,0.05) / fallSpeed, ellMin, ellMax) cells.
    /// - competenceSharpness (6): the steepness of the logistic that switches a class on around its critical speed.
    /// - jetMaxReach (60): Sediment.cs:422 caps the jet at this many cells; what would settle beyond leaves as washload (:423-428).
    /// - the grain table (index / fallSpeed / criticalSpeed): Sediment.cs:105-107, the classes the load is split into.
    /// - mean grain index of a deposit: written at Sediment.cs:215 (lumped path) and Sediment.Grains.cs:297 (per class); read by
    ///   the jet and floodplain spread (Sediment.cs:392/395/430/437) into DepositLedger.cs:105 (grainSize = Clamp01(grainSum/raw)),
    ///   stored as the layer's GrainSize (SedimentLayers.cs:20), published as LL_GrainSize (SedimentWorkers.cs:20-28), which the
    ///   floor seepage rules threshold at grainSilt 0.25 / grainGravel 0.7 (FloorSeepageRules.cs:29-30, used by
    ///   FloorSeepageGrid.cs:19/33), and checked downstream by invariant 9 (Invariants.cs:205).
    ///
    /// The fixture numbers are main's (88dda93): with grains = null (both switches off) Deposits and Spread are the old lumped
    /// path unchanged, so these literals pin what the switches must never alter.
    /// </summary>
    [Fact]
    public void EveryConsumerOfTheSettlingConstantsIsPinned()
    {
        var p = new SedimentSettings();
        Assert.Equal(0.04f, p.settleShare, 6);
        Assert.Equal(5f, p.jetSettleWidths, 6);
        Assert.Equal(3f, p.jetReachLengths, 6);
        Assert.Equal(24.5f, p.settlingCompression, 6);
        Assert.Equal(1f, p.ellMin, 6);
        Assert.Equal(400f, p.ellMax, 6);
        Assert.Equal(6f, p.competenceSharpness, 6);
        Assert.Equal(60f, p.jetMaxReach, 6);

        Assert.Equal(3, p.grains.Count);
        Assert.Equal("gravel", p.grains[0].name);
        Assert.Equal(0.8f, p.grains[0].index, 6);
        Assert.Equal(4f, p.grains[0].fallSpeed, 6);
        Assert.Equal(0.9f, p.grains[0].criticalSpeed, 6);
        Assert.Equal("sand", p.grains[1].name);
        Assert.Equal(0.42f, p.grains[1].index, 6);
        Assert.Equal(1f, p.grains[1].fallSpeed, 6);
        Assert.Equal(0.4f, p.grains[1].criticalSpeed, 6);
        Assert.Equal("fines", p.grains[2].name);
        Assert.Equal(0.15f, p.grains[2].index, 6);
        Assert.Equal(0.25f, p.grains[2].fallSpeed, 6);
        Assert.Equal(0.1f, p.grains[2].criticalSpeed, 6);

        var rules = new FloorSeepageRules();
        Assert.Equal(0.25f, rules.grainSilt, 6);
        Assert.Equal(0.7f, rules.grainGravel, 6);

        // The fixture river, off path: deposits/grain at channel cells x = 6, 12, 18, 23; alluvium/grainSize beside the
        // channel (in the channel at x=6, one row over, four rows over) and at the mouth x = 23.
        PinOffPathFixture(baseLevel: false, total: 1.89f, atBase: 0f, eroded: 4.24f, exits: 2.35f, kappa: 77.57f,
            dep: new[] { 0.0605f, 0.1140f, 0.1093f, 0.0980f }, grn: new[] { 0.9976f, 0.9941f, 0.9911f, 0.9883f },
            all: new[] { 0.5937f, 0.5604f, 0.3356f, 0.7279f }, gs: new[] { 0.4871f, 0.4293f, 0.2025f, 0.5556f },
            offered: 146.72f, laid: 146.63f, clipped: 0.09f, unplaced: 0f);
        PinOffPathFixture(baseLevel: true, total: 4.24f, atBase: 2.45f, eroded: 4.24f, exits: 0f, kappa: 77.06f,
            dep: new[] { 0.0605f, 0.1140f, 0.1093f, 2.4507f }, grn: new[] { 0.9976f, 0.9941f, 0.9911f, 0.7072f },
            all: new[] { 0.5898f, 0.5567f, 0.3334f, 3.0000f }, gs: new[] { 0.4871f, 0.4293f, 0.2025f, 0.7051f },
            offered: 327.04f, laid: 152.31f, clipped: 174.73f, unplaced: 0f);
    }

    private static void PinOffPathFixture(bool baseLevel, float total, float atBase, float eroded, float exits, float kappa,
        float[] dep, float[] grn, float[] all, float[] gs, float offered, float laid, float clipped, float unplaced)
    {
        var channel = new Channel(baseLevel);
        var p = new SedimentSettings(); // both switches off: exactly main's lumped path
        Sediment.Deposits(channel.Cells, channel.Receiver, channel.Discharge, 1f, channel.Filled, channel.Inflow, null, channel.BaseLevel,
            SizeX, p, out float[] deposit, out float[] grain, out float gotTotal, out float gotAtBase, out float gotEroded, out float gotExits);
        var ledger = new DepositLedger();
        LayTotals t = Sediment.Spread(channel.Cells, channel.Receiver, null, deposit, grain, channel.Width, channel.Filled, channel.Filled,
            SizeX, SizeZ, p, ledger, out float[] alluvium, out float[] grainSize, channel.Discharge, channel.BaseLevel);
        Assert.Equal(total, gotTotal, 2);
        Assert.Equal(atBase, gotAtBase, 2);
        Assert.Equal(eroded, gotEroded, 2);
        Assert.Equal(exits, gotExits, 2);
        Assert.Equal(kappa, ledger.Kappa, 2);
        int[] cells = { Row * SizeX + 6, Row * SizeX + 12, Row * SizeX + 18, Row * SizeX + 23 };
        int[] spots = { Row * SizeX + 6, (Row + 1) * SizeX + 6, (Row + 4) * SizeX + 6, Row * SizeX + 23 };
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(dep[i], deposit[cells[i]], 4);
            Assert.Equal(grn[i], grain[cells[i]], 4);
            Assert.Equal(all[i], alluvium[spots[i]], 4);
            Assert.Equal(gs[i], grainSize[spots[i]], 4);
        }
        Assert.Equal(offered, t.Offered, 2);
        Assert.Equal(laid, t.Laid, 2);
        Assert.Equal(clipped, t.Clipped, 2);
        Assert.Equal(unplaced, t.Unplaced, 2);
        Assert.Equal(0f, t.Washload, 6); // the lumped path has no jet: nothing leaves the map as washload
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
