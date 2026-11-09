using System;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Pins the deposition arithmetic moved out of LandscapeGenerator.Deposit into <see cref="ErosionDeposit"/> (issue #186).
/// The expected h, deposited and load are the numbers the OLD code produced on this fixture, captured by running it once
/// before the move (loop shore-prereq, round 1). Any change to the arithmetic must change these with a stated reason.
/// </summary>
public class ErosionDepositTests
{
    // 5 cells: 3 -> 2 -> 1 -> 0, with 0 the outlet at base level (the sea); cell 4 is a closed-basin sink whose lake
    // stands at 0.25; a world river brings inflow to cell 3. stack runs downstream first (the walk goes back to front).
    // deposition 1.5: cell 3 (area 1) keeps its whole load, cells 2 and 1 keep a share, the rest leaves at cell 0,
    // and the sink fills to its lake level.
    internal static (float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver, float[] area, int[] stack,
        bool[] outlet, bool[] sink, float[] baseLevel, float[] deposited) Fixture() =>
        (new[] { 0f, 0.30f, 0.50f, 0.70f, 0.248f },
         new[] { 0.01f, 0.02f, 0.03f, 0.04f, 0.005f },
         new float[5],
         new[] { 0f, 0f, 0f, 1f, 0f },
         new[] { -1, 0, 1, 2, -1 },
         new[] { 12f, 10f, 6f, 1f, 4f },
         new[] { 0, 1, 2, 3, 4 },
         new[] { true, false, false, false, false },
         new[] { false, false, false, false, true },
         new[] { 0f, float.NaN, float.NaN, float.NaN, 0.25f },
         new float[5]);

    [Fact]
    public void KeepsTheOldArithmetic()
    {
        (float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver, float[] area, int[] stack,
            bool[] outlet, bool[] sink, float[] baseLevel, float[] deposited) = Fixture();
        ErosionDeposit.Apply(h, eroded, load, inflow, receiver, area, stack, stack.Length,
            outlet, sink, baseLevel, deposited, 1.5f);

        // yield = (0.02+0.03+0.04+0.005)/4 land cells = 0.02375; cell 3 carries 0.04 + 0.02375 and keeps it all (area 1);
        // cell 2 keeps 1.5 x 0.03 / 6; cell 1 keeps 1.5 x 0.0425 / 10; the sink fills 0.248 -> 0.25.
        Assert.Equal(0f, h[0], 6);
        Assert.Equal(0.306375f, h[1], 6);   // 0.30 + 0.006375
        Assert.Equal(0.5075f, h[2], 6);     // 0.50 + 0.0075
        Assert.Equal(0.76375f, h[3], 6);    // 0.70 + 0.06375
        Assert.Equal(0.25f, h[4], 6);       // min(0.248 + 0.005, lake 0.25)
        Assert.Equal(0f, deposited[0], 6);
        Assert.Equal(0.006375f, deposited[1], 6);
        Assert.Equal(0.0075f, deposited[2], 6);
        Assert.Equal(0.06375f, deposited[3], 6);
        Assert.Equal(0.002f, deposited[4], 6);
        // The load each cell carried (the array is not zeroed after passing load on); what is left at the outlet left the model.
        Assert.Equal(0.046125f, load[0], 6);
        Assert.Equal(0.0425f, load[1], 6);
        Assert.Equal(0.03f, load[2], 6);
        Assert.Equal(0.06375f, load[3], 6);
        Assert.Equal(0.005f, load[4], 6);
    }

    [Fact]
    public void LedgerSplitsTheLoadAndConservesIt()
    {
        // Apply and ApplyWithLedger over fresh copies of the same fixture.
        (float[] hA, float[] eA, float[] lA, float[] iA, int[] rA, float[] aA, int[] sA,
            bool[] oA, bool[] skA, float[] bA, float[] dA) = Fixture();
        ErosionDeposit.Apply(hA, eA, lA, iA, rA, aA, sA, sA.Length, oA, skA, bA, dA, 1.5f);

        (float[] hL, float[] eL, float[] lL, float[] iL, int[] rL, float[] aL, int[] sL,
            bool[] oL, bool[] skL, float[] bL, float[] dL) = Fixture();
        float[] delivered = new float[5];
        ErosionDeposit.ApplyWithLedger(hL, eL, lL, iL, rL, aL, sL, sL.Length, oL, skL, bL, dL, 1.5f,
            delivered, out float sinkFill, out float lostAtSink);

        // Same terrain, same deposits, cell for cell.
        Assert.Equal(hA, hL);
        Assert.Equal(dA, dL);

        // The ledger on this fixture: the outlet (cell 0) delivers the load the old-arithmetic test pins (0.046125);
        // the lake (cell 4) gains 0.002 and loses the 0.003 of its load it has no room for; no other cell delivers.
        Assert.Equal(0.046125f, delivered[0], 6);
        Assert.Equal(0f, delivered[1], 6);
        Assert.Equal(0f, delivered[2], 6);
        Assert.Equal(0f, delivered[3], 6);
        Assert.Equal(0f, delivered[4], 6);
        Assert.Equal(0.002f, sinkFill, 6);
        Assert.Equal(0.003f, lostAtSink, 6);

        // Conservation, with the sums read off the arrays and yield as the code computes it (mean eroded over the
        // non-outlet cells): everything the model eroded or brought in from beyond the region ends up deposited
        // (channel keeps + lake fill), delivered to the sea, or lost at a sink.
        float sumEroded = 0f, sumInflow = 0f, yieldSum = 0f;
        int land = 0;
        for (int i = 0; i < 5; i++)
        {
            sumEroded += eL[i];
            sumInflow += iL[i];
            if (!oL[i]) { yieldSum += eL[i]; land++; }
        }
        float total = sumEroded + yieldSum / land * sumInflow; // 0.105 + 0.02375 x 1 = 0.12875
        Assert.Equal(0.12875f, total, 6);

        float sumDeposited = 0f, sumDelivered = 0f, sumChannel = 0f;
        for (int i = 0; i < 5; i++)
        {
            sumDeposited += dL[i];
            sumDelivered += delivered[i];
            if (rL[i] >= 0) sumChannel += dL[i]; // the lake fill sits in deposited too; channel keeps are the rest
        }
        Assert.Equal(total, sumDeposited + sumDelivered + lostAtSink, 4);
        // The same split four ways: channel deposits, sea delivery, lake fill, loss at the sink.
        Assert.Equal(total, sumChannel + sumDelivered + sinkFill + lostAtSink, 4);
    }
}

/// <summary>
/// Pins <see cref="Sediment.SupplyGrain"/>, extracted verbatim from the expression at Sediment.cs:189 (issue #186, C2).
/// The expected value is the old expression written out here; any change to one must show up against the other.
/// </summary>
public class SupplyGrainTests
{
    [Fact]
    public void MatchesTheOldExpression()
    {
        const float coarseSlope = 0.05f;
        foreach (float slope in new[] { 0f, 0.01f, 0.05f, 0.2f })
            foreach (float rock in new[] { 0f, 0.5f, 1f })
            {
                // The expression as it stood in Sediment.Deposits before the extraction (Sediment.cs:189).
                float old = UnityEngine.Mathf.Clamp01(
                    UnityEngine.Mathf.Lerp(0.2f, 1f, slope / UnityEngine.Mathf.Max(coarseSlope, 1e-4f)) *
                    UnityEngine.Mathf.Lerp(0.7f, 1f, rock));
                Assert.Equal(old, Sediment.SupplyGrain(slope, rock, coarseSlope));
            }
    }

    [Fact]
    public void RisesWithSlopeAndRockStrengthAndStaysIn0To1()
    {
        // lerp(0.2,1,t) floors at 0.2 on no slope; lerp(0.7,1,rock) runs 0.7 (any rock) .. 1.0 (bedrock); clamp01 on top.
        Assert.Equal(0.2f, Sediment.SupplyGrain(0f, 1f, 0.05f), 6);   // no slope: slope term at its floor, hard rock
        Assert.Equal(1f, Sediment.SupplyGrain(0.05f, 1f, 0.05f), 6);  // at coarseSlope on bedrock: fully coarse
        Assert.Equal(0.7f, Sediment.SupplyGrain(0.05f, 0f, 0.05f), 6); // same slope, weakest rock: capped by the rock term
        Assert.Equal(1f, Sediment.SupplyGrain(1f, 1f, 0.001f), 6);    // far above coarseSlope: t clamps, still 1
        Assert.True(Sediment.SupplyGrain(0.2f, 1f, 0.05f) > Sediment.SupplyGrain(0.01f, 1f, 0.05f));  // steeper is coarser
        Assert.True(Sediment.SupplyGrain(0.05f, 1f, 0.05f) > Sediment.SupplyGrain(0.05f, 0f, 0.05f)); // stronger rock is coarser
    }
}

/// <summary>
/// Pins the grain tracking of <see cref="ErosionDeposit.ApplyWithGrain"/> (issue #186, C3 part 2): each load carries a
/// mass-weighted mean grain, a cell that keeps part of its load keeps the coarsest grains first (so the deposit is
/// coarser and the load passed on finer than the mean), confluences mix grains by mass, and none of this may change
/// the deposition arithmetic the tests above pin. Grain 1 is the coarsest sediment, 0 the finest.
/// </summary>
public class ErosionGrainTests
{
    // The 5-cell fixture of ErosionDepositTests with a supply grain per cell (0.9 at the sea cell down to 0.2 in the
    // closed basin) and inflow grain 0.42 for the world river's sediment.
    private const float InflowGrain = 0.42f;
    private static readonly float[] Supply = { 0.9f, 0.6f, 0.5f, 0.3f, 0.2f };

    [Fact]
    public void GrainDoesNotChangeTheArithmetic()
    {
        (float[] hA, float[] eA, float[] lA, float[] iA, int[] rA, float[] aA, int[] sA,
            bool[] oA, bool[] skA, float[] bA, float[] dA) = ErosionDepositTests.Fixture();
        float[] deliveredA = new float[5];
        ErosionDeposit.ApplyWithLedger(hA, eA, lA, iA, rA, aA, sA, sA.Length, oA, skA, bA, dA, 1.5f,
            deliveredA, out float fillA, out float lostA);

        (float[] hG, float[] eG, float[] lG, float[] iG, int[] rG, float[] aG, int[] sG,
            bool[] oG, bool[] skG, float[] bG, float[] dG) = ErosionDepositTests.Fixture();
        float[] deliveredG = new float[5];
        ErosionDeposit.ApplyWithGrain(hG, eG, lG, iG, rG, aG, sG, sG.Length, oG, skG, bG, dG, 1.5f,
            deliveredG, Supply, InflowGrain, new float[5], new float[5], out float fillG, out float lostG);

        Assert.Equal(hA, hG);
        Assert.Equal(dA, dG);
        Assert.Equal(deliveredA, deliveredG);
        Assert.Equal(fillA, fillG);
        Assert.Equal(lostA, lostG);
    }

    [Fact]
    public void ACellKeepsTheCoarsestGrainsFirst()
    {
        (float[] h, float[] e, float[] l, float[] i, int[] r, float[] a, int[] s,
            bool[] o, bool[] sk, float[] b, float[] d) = ErosionDepositTests.Fixture();
        float[] depositGrain = new float[5];
        float[] deliveredGrain = new float[5];
        ErosionDeposit.ApplyWithGrain(h, e, l, i, r, a, s, s.Length, o, sk, b, d, 1.5f,
            new float[5], Supply, InflowGrain, depositGrain, deliveredGrain, out _, out _);

        // Cell 2 carries only its own eroded sediment (cell 3, area 1, keeps its whole load and passes nothing on):
        // grain 0.5. It keeps 0.0075 of 0.03 - a quarter - and the quarter kept is the coarsest: 1 + (0.5 - 1) x 0.25.
        Assert.Equal(0.875f, depositGrain[2], 6);
        // The lake (cell 4) keeps 0.002 of the 0.005 that reached it (grain 0.2), coarsest first: 1 + (0.2 - 1) x 0.4.
        // (5 decimals: the fill is 0.25 - 0.248, a float subtraction with a visible rounding error of 2e-9.)
        Assert.Equal(0.68f, depositGrain[4], 5);
        // Cell 3 keeps its whole load, so its deposit has the load's own mean grain: its eroded 0.04 at 0.3 mixed by
        // mass with the world river's 0.02375 at 0.42.
        Assert.Equal(0.3447f, depositGrain[3], 4);
        // The load that reaches the sea is finer than any of its sources' coarse ends: cell 1's outflow (0.403015)
        // mixed by mass with cell 0's own eroded 0.01 at grain 0.9.
        Assert.Equal(0.5108f, deliveredGrain[0], 4);
        Assert.Equal(0f, deliveredGrain[1], 6); // only base-level cells deliver
        Assert.Equal(0f, deliveredGrain[4], 6); // a sink delivers nothing
    }

    [Fact]
    public void AConfluenceMixesGrainsByMass()
    {
        // Two headwater cells (0.03 at grain 0.8, 0.01 at grain 0.15) join at the outlet; deposition 0 so each
        // headwater passes its whole load on.
        float[] h = { 0f, 0.5f, 0.5f };
        float[] eroded = { 0f, 0.03f, 0.01f };
        float[] supply = { 0f, 0.8f, 0.15f };
        int[] receiver = { -1, 0, 0 };
        float[] area = { 100f, 100f, 100f };
        int[] stack = { 0, 1, 2 }; // downstream first
        float[] delivered = new float[3];
        float[] deliveredGrain = new float[3];
        ErosionDeposit.ApplyWithGrain(h, eroded, new float[3], new float[3], receiver, area, stack, 3,
            new[] { true, false, false }, new[] { false, false, false }, new[] { 0f, float.NaN, float.NaN },
            new float[3], 0f, delivered, supply, 0.5f, new float[3], deliveredGrain, out _, out _);

        // Mass-weighted mean, not the plain average: (0.8 x 0.03 + 0.15 x 0.01) / 0.04 = 0.6375, not 0.475.
        Assert.Equal(0.04f, delivered[0], 6);
        Assert.Equal(0.6375f, deliveredGrain[0], 5);
    }

    [Fact]
    public void TheLoadLeavingADepositIsFinerThanTheLoadEnteringIt()
    {
        // One land cell (0.04 eroded at grain 0.8, area 10) draining to the sea; deposition 1.5 keeps 0.15 of the load.
        float[] h = { 0f, 0.5f };
        float[] eroded = { 0f, 0.04f };
        float[] supply = { 0f, 0.8f };
        float[] depositGrain = new float[2];
        float[] deliveredGrain = new float[2];
        ErosionDeposit.ApplyWithGrain(h, eroded, new float[2], new float[2], new[] { -1, 0 }, new[] { 100f, 10f },
            new[] { 0, 1 }, 2, new[] { true, false }, new[] { false, false }, new[] { 0f, float.NaN },
            new float[2], 1.5f, new float[2], supply, 0.5f, depositGrain, deliveredGrain, out _, out _);

        // Keeps 0.006 of 0.04 (share 0.15): the deposit is the coarsest part, 1 + (0.8 - 1) x 0.15 = 0.97; the rest
        // carries the remainder of the mass: (0.8 x 0.04 - 0.97 x 0.006) / 0.034 = 0.77.
        Assert.Equal(0.97f, depositGrain[1], 6);
        Assert.Equal(0.77f, deliveredGrain[0], 6);
        Assert.True(depositGrain[1] > 0.8f);    // coarser than the load it came from
        Assert.True(deliveredGrain[0] < 0.8f);  // the load that moved on is finer
        // The same split read as a mass-weighted mean: the two parts' grains bracket the load's own grain.
        Assert.True(deliveredGrain[0] < depositGrain[1]);
    }
}
