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
    private static (float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver, float[] area, int[] stack,
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
