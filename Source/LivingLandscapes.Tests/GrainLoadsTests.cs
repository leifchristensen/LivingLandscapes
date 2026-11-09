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

    /// <summary>
    /// The still-water floors are named settings (<see cref="SedimentSettings.uMin"/> m/s and <see cref="SedimentSettings.dMin"/> m,
    /// #186), not literals buried in the two expressions that read a speed or a depth. With the shipped grain table neither floor can
    /// move a number (every class has a critical speed above uMin, and ellMin already clamps a length of 0), so the test first pins
    /// the defaults and shows the numbers are still the ones the inline literals gave, then lifts the clamp and the critical speeds to
    /// show the floors themselves are what keeps standing water usable: a settling length above 0 (which <c>Settled</c> divides by)
    /// and a competence that is a finite number rather than a 0-by-0 NaN for a class that starts moving at no speed at all.
    /// </summary>
    [Fact]
    public void TheStillWaterFloorsKeepStandingWaterUsable()
    {
        var p = Classes(settling: true);
        Assert.Equal(0.05f, p.uMin, 6); // m/s - the literal Mathf.Max(u, 0.05f) used to be
        Assert.Equal(0.05f, p.dMin, 6); // m   - the literal Mathf.Max(d, 0.05f) used to be
        var grains = new GrainLoads(p, new float[1], new float[1]);

        // Standing water is the same water as water exactly at the floors, for every class.
        for (int k = 0; k < grains.Count; k++)
            Assert.Equal(grains.Length(k, 0f, 0f), grains.Length(k, p.uMin, p.dMin), 6);

        // The floor, not the ellMin clamp, is what keeps the length usable: lift the clamp and standing water still has a length,
        // the product of the two floors, above 0 and finite, and the share settling over a cell is a number, not NaN.
        p.ellMin = 0f;
        for (int k = 0; k < grains.Count; k++)
        {
            float still = grains.Length(k, 0f, 0f);
            Assert.False(float.IsNaN(still) || float.IsInfinity(still), $"class {k}: settling length in standing water is {still}");
            Assert.True(still > 0f, $"class {k}: settling length in standing water is {still}, not above 0");
            Assert.Equal(p.settlingCompression * p.uMin * p.dMin / p.grains[k].fallSpeed, still, 6);
            float settled = grains.Settled(k, 0f, 0f, 1f);
            Assert.False(float.IsNaN(settled) || float.IsInfinity(settled), $"class {k}: share settling in standing water is {settled}");
            Assert.Equal(1f - Mathf.Exp(-1f / still), settled, 6);
        }

        // Competence reads the floor too, where the class's speed of first movement is the width of the logistic: without the floor a
        // class that starts moving at no speed at all would divide 0 by 0 in standing water and give NaN. With it the logistic is
        // defined everywhere - half competent in standing water (the logistic sits at its centre, u = uc = 0), not NaN.
        var soft = Classes();
        soft.grains = new List<GrainClass>
        {
            new GrainClass { name = "dust", index = 0.1f, fallSpeed = 0.25f, criticalSpeed = 0f },
            new GrainClass { name = "sand", index = 0.42f, fallSpeed = 1f, criticalSpeed = 0.4f },
        };
        var dust = new GrainLoads(soft, new float[1], new float[1]);
        float dustStill = dust.Competence(0, 0f);
        Assert.False(float.IsNaN(dustStill), "a class with criticalSpeed 0 must not divide 0 by 0 in the competence logistic");
        Assert.Equal(0.5f, dustStill, 6);
        // The floor bounds the logistic's width, not its centre: the centre stays at u = uc = 0, so one floor-speed above it the
        // class is already almost fully competent - 1 / (1 + exp(-sharpness)) - and the number is finite, which is what the floor is for.
        Assert.Equal(1f / (1f + Mathf.Exp(-soft.competenceSharpness)), dust.Competence(0, soft.uMin), 6);

        // And with the shipped table no floor bites, so the competence of gravel in standing water is still exactly what the
        // logistic gives with the class's own critical speed: 1 / (1 + exp(competenceSharpness)).
        var q = Classes();
        float uc = q.grains[0].criticalSpeed;
        Assert.True(uc > q.uMin, "every class must stay above the speed floor, or the floor would move the shipped numbers");
        var table = new GrainLoads(q, new float[1], new float[1]);
        Assert.Equal(1f / (1f + Mathf.Exp(-q.competenceSharpness * (0f - uc) / uc)), table.Competence(0, 0f), 6);
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

    /// <summary>
    /// The join/split fixture for the plan's S3 arithmetic test (HYDRO-UNIFICATION section 10 point 12, #186): two rivers join at
    /// cell 2, which then divides its water again (the main way to cell 3, a second way to cell 4 taking
    /// <paramref name="splitShare"/>). Cells 3 and 4 are base level on solid rock, so they drop exactly the load that arrives at them
    /// (Sediment.Grains.cs:262-266): their per-class deposits read the load on each way before anything else can settle. Each branch's
    /// load is known by construction: the only supply is <c>gouged</c> (rock = 1 erodes nothing, inflow = 0 brings nothing), shared
    /// over the classes by the supply's mix, and the mix follows from the reach's slope (grainSupply = slope / coarseSlope at rock 1,
    /// Sediment.Grains.cs:243, since Lerp(0.7, 1, 1) = 1). Cell 0 (slope (0.15 - 0) / 2 = 0.075 → index 0.6) brings 1.0 as gravel
    /// 0.4737 + sand 0.5263; cell 1 (slope 0.0375 / 2 = 0.01875 → index 0.3) brings 0.5 as sand 0.2778 + fines 0.2222. The water
    /// above the base-level cells is fast and deep (discharge 100), so its capacity is more than the load on it and nothing settles
    /// on the way: the load at the probes is the load at the join.
    /// </summary>
    private static GrainLoads TwoRiversJoinAndSplit(float splitShare, bool steepBranch = true, bool gentleBranch = true)
    {
        var cells = new List<int> { 0, 1, 2, 3, 4 };
        var receiver = new[] { 2, 2, 3, -1, -1 };
        var secondary = new[] { -1, -1, 4, -1, -1 };
        var share = new[] { 0f, 0f, splitShare, 0f, 0f };
        var discharge = new[] { 100f, 100f, 100f, 0f, 0f };
        var filled = new[] { 0.15f, 0.0375f, 0.8f, 0f, 0f };
        var inflow = new float[5];
        var rock = new[] { 1f, 1f, 1f, 1f, 1f };
        var gouged = new[] { steepBranch ? 1f : 0f, gentleBranch ? 0.5f : 0f, 0f, 0f, 0f };
        var baseLevel = new[] { false, false, false, true, true };
        var p = Classes(settling: true);
        var grains = new GrainLoads(p, new[] { 1.2f, 1.2f, 1.2f, 1.2f, 1.2f }, new[] { 1f, 1f, 1f, 1f, 1f });
        Sediment.Deposits(cells, receiver, discharge, 1f, filled, inflow, rock, baseLevel, 5, p, out _, out _, out _, out _, out _, out _,
            gouged: gouged, secondary: secondary, secondaryShare: share, grains: grains);
        return grains;
    }

    /// <summary>What class <paramref name="k"/> laid on the two base-level probes: the whole load that reached them.</summary>
    private static float ArrivedLoad(GrainLoads grains, int k) => grains.Deposit[k][3] + grains.Deposit[k][4];

    /// <summary>
    /// C4 (#186): the per-class loads are extensive, so a confluence adds them. Downstream of the join, before anything settles,
    /// every class's load is the sum of the two rivers' loads: gravel comes only from the steep branch, fines only from the gentle
    /// one, and sand from both, so the join is checked class by class and not only in total.
    /// </summary>
    [Fact]
    public void AJoinSumsEveryClassDownstreamOfTheConfluence()
    {
        GrainLoads both = TwoRiversJoinAndSplit(0.3f);
        GrainLoads steep = TwoRiversJoinAndSplit(0.3f, gentleBranch: false);
        GrainLoads gentle = TwoRiversJoinAndSplit(0.3f, steepBranch: false);
        // The two rivers' own loads, per class: gouged supply x the supply's mix (the hat of Grains.Mix, Sediment.Grains.cs:56).
        var steepLoad = new[] { 1f * 0.4736842f, 1f * 0.5263158f, 0f };
        var gentleLoad = new[] { 0f, 0.5f * 0.5555556f, 0.5f * 0.4444444f };
        for (int k = 0; k < 3; k++)
        {
            AssertClose(steepLoad[k], ArrivedLoad(steep, k), $"the steep branch's own load, class {k}");
            AssertClose(gentleLoad[k], ArrivedLoad(gentle, k), $"the gentle branch's own load, class {k}");
            AssertClose(steepLoad[k] + gentleLoad[k], ArrivedLoad(both, k), $"the joined load below the confluence, class {k}");
            AssertClose(both.Supplied[k], both.Deposited[k] + both.Exits[k], $"class {k} mass closure at the join");
        }
        Assert.True(ArrivedLoad(both, 0) > 0f && ArrivedLoad(both, 1) > 0f && ArrivedLoad(both, 2) > 0f, "all three classes reach the join");
    }

    /// <summary>
    /// C4 (#186): where the water divides, the load divides with it by the same share (the routing's <c>secondaryShare</c>, the rule
    /// of #118): the second way takes exactly that share of every class, no more of the fines than of the gravel.
    /// </summary>
    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(0.5f)]
    public void ASplitSendsExactlyTheSecondaryShareOfEveryClass(float share)
    {
        GrainLoads grains = TwoRiversJoinAndSplit(share);
        for (int k = 0; k < 3; k++)
        {
            float main = grains.Deposit[k][3], second = grains.Deposit[k][4], joined = main + second;
            Assert.True(joined > 0f, $"class {k} must reach the split");
            AssertClose(share * joined, second, $"the second way's share {share} of class {k}");
            AssertClose((1f - share) * joined, main, $"the main way's share {1f - share} of class {k}");
        }
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
    /// the reach above it erodes a supply fine enough (grain index under the sand class's 0.42, Sediment.Grains.cs:243) to carry
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
    /// - settleShare (0.04): Sediment.cs:217 (share of the excess load settling in one cell), Sediment.cs:224 (slow share below a
    ///   junction), Sediment.Grains.cs:130 (the per-class settling length falls back to it when the length switch is off).
    /// - jetSettleWidths (5): Sediment.cs:269 (a mouth jet settles its load over this many channel widths), Sediment.cs:436
    ///   (a class's jet reach scales the settling length by the reference fall speed over its own).
    /// - jetReachLengths (3): Sediment.cs:269 and :437 (the jet reaches this many settling lengths).
    /// - settlingCompression (24.5), ellMin (1), ellMax (400): Sediment.Grains.cs:126, the per-class settling length
    ///   Length = Clamp(settlingCompression x max(u,uMin) x max(d,dMin) / fallSpeed, ellMin, ellMax) cells.
    /// - uMin (0.05 m/s), dMin (0.05 m): the still-water floors in that same length (Sediment.Grains.cs:126) and, for uMin, the
    ///   floor under a class's critical speed where it sets the width of the competence logistic (Sediment.Grains.cs:92). Named
    ///   settings since the still-water-floor step (#186); before that the same two literals sat inline in those expressions.
    /// - competenceSharpness (6): the steepness of the logistic that switches a class on around its critical speed
    ///   (Sediment.Grains.cs:92).
    /// - jetMaxReach (60): Sediment.cs:438 caps the jet at this many cells; what would settle beyond leaves as washload (:439-444).
    /// - the grain table (index / fallSpeed / criticalSpeed): Sediment.cs:105-107, the classes the load is split into.
    /// - mean grain index of a deposit: written at Sediment.cs:231 (lumped path) and Sediment.Grains.cs:303 (per class); read by
    ///   the jet and floodplain spread (Sediment.cs:408/411/446/453) into DepositLedger.cs:105 (grainSize = Clamp01(grainSum/raw)),
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
        Assert.Equal(0.05f, p.uMin, 6);   // m/s
        Assert.Equal(0.05f, p.dMin, 6);   // m
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
        Assert.True(float.IsNaN(GrainLoads.WorstDownstreamCoarsening(cells, grain, deposit)));
    }

    /// <summary>
    /// C1 (#186): the junction probe for invariant 9. A river runs a long gentle plain (row 1, <see cref="PlainCells"/> cells, soft
    /// gouged source at x=0 on flat ground, so its own supply is fines by <see cref="Sediment.SupplyGrain"/>) and fines downstream;
    /// a steep gravel tributary (cell <see cref="JunctionX"/>, gouged on steep ground, mix ~0.7 = gravel) joins it there. The joined
    /// load is the model's own arithmetic (the per-class loads add at the receiver, Sediment.Grains.cs:321-322), so below the
    /// junction the deposit is coarser than above it - physically right: a gravel tributary onto a fines plain makes the bed gravel
    /// there, as in the failing flat-map cases. Returns the whole-stem climb of the running mean; the caller also measures each
    /// reach between inflows on its own, and walks both with <see cref="GrainLoads.WorstDownstreamCoarsening"/>.
    /// </summary>
    private const int PlainCells = 40;
    private const int JunctionX = 6;

    private static float FinePlainWithGravelTributary(bool tributary, out float[] deposit, out float[] grain, out List<int> mainStem)
    {
        const int SX = PlainCells, SZ = 3, N = SX * SZ;
        // Topological walk: the main stem above the junction, then the tributary, then the joined stem below it.
        var cells = new List<int>();
        for (int x = 0; x < JunctionX; x++)
            cells.Add(SX + x);
        cells.Add(JunctionX);
        for (int x = JunctionX; x < SX; x++)
            cells.Add(SX + x);
        var receiver = new int[N];
        for (int i = 0; i < N; i++)
            receiver[i] = -1;
        for (int x = 0; x < SX - 1; x++)
            receiver[SX + x] = SX + x + 1;
        receiver[JunctionX] = SX + JunctionX; // the tributary joins the plain at x = JunctionX
        var filled = new float[N];
        for (int x = 0; x < SX; x++)
        {
            float t = (float)x / (SX - 1);
            filled[SX + x] = 0.40f * (1f - t) * (1f - t); // a flat concave plain: ~0.02 m per cell at the head, less below
        }
        filled[JunctionX] = 1.5f; // steep ground above the plain
        var discharge = new float[N];
        for (int x = 0; x < SX; x++)
            discharge[SX + x] = 10f;
        discharge[JunctionX] = tributary ? 4f : 0f;
        var gouged = new float[N];
        gouged[SX] = 0.6f; // the plain's own soft source: flat ground, so fines
        if (tributary)
            gouged[JunctionX] = 1.5f; // the mountain's gravel
        var p = Classes(settling: true);
        var speed = new float[N];
        var depth = new float[N];
        for (int i = 0; i < N; i++)
        {
            speed[i] = 1.2f;
            depth[i] = 1f;
        }
        Sediment.Deposits(cells, receiver, discharge, 1f, filled, new float[N], null, null, SX, p,
            out deposit, out grain, out _, out _, out _, out _, gouged: gouged, grains: new GrainLoads(p, speed, depth));
        mainStem = new List<int>();
        for (int x = 0; x < SX; x++)
            mainStem.Add(SX + x);
        return GrainLoads.WorstDownstreamCoarsening(mainStem, grain, deposit);
    }

    /// <summary>
    /// C1 (#186): invariant 9 as written measures a legitimate junction as a violation. The lone fines plain river fines and passes
    /// (climb 0); the same river with a gravel tributary joining mid-plain climbs above <see cref="SedimentSettings.coarsenTolerance"/>
    /// below the junction, yet each reach between inflows still fines (both climbs ~0): the whole climb is the one step of coarser
    /// grain AT the junction cell, where the tributary's gravel joins the load - correct sedimentology, not a river coarsening
    /// towards its own mouth. So the check must account for the inflow at junctions instead of blaming the settling order
    /// (the fix is C2, #186). Measured here: lone 0.000, joined 0.296 against the tolerance 0.15, each reach 0.000, deposit grain
    /// 0.339 above the junction, 0.775 at it, 0.454 at the mouth; the same mechanism gives the flat-map cases 0.18-0.37.
    /// </summary>
    [Fact]
    public void InvariantNineFlagsALegitimateGravelTributaryOnAFinePlain()
    {
        float tolerance = new SedimentSettings().coarsenTolerance;
        float lone = FinePlainWithGravelTributary(false, out float[] depositLone, out float[] grainLone, out List<int> stem);
        float joined = FinePlainWithGravelTributary(true, out float[] depositJoined, out float[] grainJoined, out _);
        // The same joined walk, cut at the junction: what each reach between inflows does on its own.
        float above = GrainLoads.WorstDownstreamCoarsening(stem.GetRange(0, JunctionX), grainJoined, depositJoined);
        float below = GrainLoads.WorstDownstreamCoarsening(stem.GetRange(JunctionX, PlainCells - JunctionX), grainJoined, depositJoined);
        Assert.True(lone <= tolerance,
            $"the lone fine river must pass: measured a running-mean climb of {lone:F3} over the tolerance {tolerance:F2}");
        Assert.True(joined > tolerance,
            $"the joined river must show the junction's rise to prove the point: measured only {joined:F3}");
        Assert.True(above <= tolerance && below <= tolerance,
            $"each reach between inflows fines, so the whole-channel climb is the junction step, not coarsening: " +
            $"above {above:F3}, below {below:F3}");
        Assert.True(grainJoined[PlainCells + JunctionX] > grainLone[PlainCells + JunctionX] + 0.2f,
            $"the tributary's gravel must reach the plain below the junction: " +
            $"{grainJoined[PlainCells + JunctionX]:F2} against {grainLone[PlainCells + JunctionX]:F2}");
        Assert.True(depositJoined[PlainCells + JunctionX] > 0f && depositJoined[2 * PlainCells - 1] > 0f,
            "the joined river deposits to its mouth");
    }
}
