using System;
using LivingLandscapes.Layers;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// The one water formula (#132, plan decision D4): every source wets what the others leave dry, so a cell holds the
/// union of what rain (C), terrain (W) and open water (P) each give it - G = 1 - (1-C)(1-W) for the ground itself,
/// M = 1 - (1-C)(1-W)(1-P) for the moisture plants read. Tested as properties of the equation plus one bit-exact
/// comparison against the expression the mod used before the layers were merged: the merge must not move a single
/// plant's moisture, and the ground term must stay between its sources.
/// </summary>
public class WaterFieldTests
{
    private const float Tolerance = 1e-6f;

    /// <summary>Inputs spanning dry to saturated, including the values a float writes badly (1/3, 0.987654321).</summary>
    private static readonly float[] Values =
    {
        0f, 0.001f, 0.01f, 0.05f, 1f / 3f, 0.12345678f, 0.25f, 0.5f, 0.75f, 0.9f, 0.95f, 0.987654321f, 0.99f, 1f,
    };

    /// <summary>The expression MoistureWorker used before the water layers were merged (main, Layers/MoistureWorker.cs).</summary>
    private static float LegacyMoisture(float climate, float wetness, float proximity) =>
        1f - (1f - climate) * (1f - wetness) * (1f - proximity);

    [Fact]
    public void Moisture_IsBitForBitTheExpressionTheModUsed()
    {
        // Flag off must be identical to main, so this is compared bit by bit, not to a tolerance: a different operand
        // order (G first, then P) rounds differently and would show up here.
        foreach (float c in Values)
            foreach (float w in Values)
                foreach (float p in Values)
                {
                    float legacy = LegacyMoisture(c, w, p);
                    float actual = WaterFieldMath.Moisture(c, w, p);
                    Assert.Equal(BitConverter.SingleToInt32Bits(legacy), BitConverter.SingleToInt32Bits(actual));
                }
    }

    [Fact]
    public void Moisture_InTwoSteps_IsTheSameMoisture()
    {
        // M = 1 - (1-G)(1-P) with G = 1 - (1-C)(1-W) is the same law as the flat form; the two round differently, so
        // this holds the two-step reading (what the design's G/M notation says) against what plants actually get.
        foreach (float c in Values)
            foreach (float w in Values)
                foreach (float p in Values)
                {
                    float ground = WaterFieldMath.GroundTerm(w, c);
                    Assert.True(Math.Abs(WaterFieldMath.Moisture(ground, p) - WaterFieldMath.Moisture(c, w, p)) <= Tolerance,
                        $"two steps differs from the flat form at c {c}, w {w}, p {p}");
                }
    }

    [Fact]
    public void GroundTerm_IsAtLeastEachSource_AndNeverWetterThanOne()
    {
        foreach (float c in Values)
            foreach (float w in Values)
            {
                float ground = WaterFieldMath.GroundTerm(w, c);
                Assert.True(ground >= w - Tolerance, $"ground {ground} drier than the terrain wetness {w} at climate {c}");
                Assert.True(ground >= c - Tolerance, $"ground {ground} drier than the rain baseline {c} at wetness {w}");
                Assert.True(ground <= 1f + Tolerance, $"ground {ground} wetter than saturated");
                Assert.True(ground >= 0f);
            }
    }

    [Theory]
    [InlineData(0.42f)]
    [InlineData(0f)]
    [InlineData(1f)]
    public void GroundTerm_WithOnlyOneSource_IsThatSource(float value)
    {
        Assert.Equal(value, WaterFieldMath.GroundTerm(value, 0f), 6);  // no rain: the terrain alone
        Assert.Equal(value, WaterFieldMath.GroundTerm(0f, value), 6);  // no terrain: the rain alone
    }

    [Fact]
    public void Moisture_WithNoOpenWater_IsTheGroundTerm()
    {
        // Multiplying by (1 - 0) is exact, so away from open water the flat form and the ground term agree bit for bit.
        foreach (float c in Values)
            foreach (float w in Values)
                Assert.Equal(BitConverter.SingleToInt32Bits(WaterFieldMath.GroundTerm(w, c)),
                             BitConverter.SingleToInt32Bits(WaterFieldMath.Moisture(c, w, 0f)));
    }

    [Fact]
    public void Moisture_NeverDriesAsAnySourceGrows()
    {
        foreach (float c in Values)
            foreach (float w in Values)
                foreach (float p in Values)
                {
                    AssertWetterOrEqual(WaterFieldMath.Moisture(c, w, p), WaterFieldMath.Moisture(c, w, 0f), "open water");
                    AssertWetterOrEqual(WaterFieldMath.Moisture(c, w, p), WaterFieldMath.Moisture(c, 0f, p), "terrain wetness");
                    AssertWetterOrEqual(WaterFieldMath.Moisture(c, w, p), WaterFieldMath.Moisture(0f, w, p), "rain");
                    Assert.True(WaterFieldMath.Moisture(c, w, p) >= 0f && WaterFieldMath.Moisture(c, w, p) <= 1f + Tolerance);
                }
    }

    private static void AssertWetterOrEqual(float wetter, float drier, string source) =>
        Assert.True(wetter >= drier - Tolerance, $"adding {source} dried the cell: {wetter} < {drier}");

    [Fact]
    public void TerrainValue_OffReadsTheWetnessGrid_OnReadsTheGroundTerm()
    {
        foreach (float c in Values)
            foreach (float w in Values)
            {
                // Off is the default and must hand the reader the wetness grid's own value, bit for bit.
                Assert.Equal(BitConverter.SingleToInt32Bits(w),
                             BitConverter.SingleToInt32Bits(WaterFieldMath.TerrainValue(w, c, false)));
                Assert.Equal(BitConverter.SingleToInt32Bits(WaterFieldMath.GroundTerm(w, c)),
                             BitConverter.SingleToInt32Bits(WaterFieldMath.TerrainValue(w, c, true)));
            }
    }
}
