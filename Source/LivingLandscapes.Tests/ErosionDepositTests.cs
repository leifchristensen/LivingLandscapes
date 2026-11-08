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
