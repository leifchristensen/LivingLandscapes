using System;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Where the rain runs off (#216): infiltration capacity falls with rock hardness and slope, runoff is what the ground's
/// absorption leaves of the climate's wetness, and the tile's mean stays what the climate decided. Properties of the
/// equations, not snapshots: the loss and half-slope are tuning values.
/// </summary>
public class GroundRunoffTests
{
    private const float Loss = 0.6f, Half = 0.6f;

    [Fact]
    public void Infiltration_IsFullOnSoftGentleGround_AndFallsWithRockAndSlope()
    {
        Assert.Equal(1f, GroundRunoff.Infiltration(0f, 0f, Loss, Half), 5);
        Assert.True(GroundRunoff.Infiltration(1f, 0f, Loss, Half) < GroundRunoff.Infiltration(0.3f, 0f, Loss, Half));
        Assert.True(GroundRunoff.Infiltration(0f, 1.2f, Loss, Half) < GroundRunoff.Infiltration(0f, 0.2f, Loss, Half));
        Assert.Equal(0.5f, GroundRunoff.Infiltration(0f, Half, Loss, Half), 5);   // infiltration halves at the half slope
        Assert.Equal(1f - Loss, GroundRunoff.Infiltration(1f, 0f, Loss, Half), 5);  // the hardest rock loses its share
    }

    [Fact]
    public void CellRunoff_IsHigherWhereTheGroundAbsorbsLess_AndNeverOutsideZeroToOne()
    {
        float soil = GroundRunoff.CellRunoff(0.5f, 0.3f, 1f), rock = GroundRunoff.CellRunoff(0.5f, 0.3f, 0.2f);
        Assert.True(rock > soil);
        // Where the climate is no wetter than the ground absorbs, nothing runs off; the wettest climate sheds all of it.
        Assert.Equal(0f, GroundRunoff.CellRunoff(0.2f, 0.3f, 1f), 5);
        Assert.InRange(GroundRunoff.CellRunoff(1f, 0.9f, 1f), 0f, 1f);
        Assert.Equal(GroundRunoff.CellRunoff(0.6f, 0f, 1f), 0.6f, 5);             // no absorption: runoff is the climate's wetness
    }

    [Fact]
    public void Distribute_KeepsTheClimatesMean_AndMovesRunoffToHardSteepGround()
    {
        int n = 100;
        var strength = new float[n];
        var slope = new float[n];
        for (int i = 0; i < n; i++)
        {
            strength[i] = i < 50 ? 0.1f : 0.9f;   // soft half, hard half
            slope[i] = i < 50 ? 0.05f : 1.2f;
        }
        float mean = 0.4f;
        float[] r = GroundRunoff.Distribute(mean, 0.5f, 0.3f, strength, slope, Loss, Half);
        float sum = 0f;
        foreach (float v in r)
            sum += v;
        Assert.Equal(mean, sum / n, 2);                                // the ground moves water about, it does not make any
        Assert.True(r[75] > r[25]);                                    // hard steep ground sheds more than soft gentle ground
        Assert.All(r, v => Assert.InRange(v, 0.01f, 1f));
    }

    [Fact]
    public void Distribute_IsUniformOnUniformGround_AndNullWithoutRock()
    {
        var strength = new float[40];
        var slope = new float[40];
        Array.Fill(strength, 0.5f);
        Array.Fill(slope, 0.3f);
        float[] r = GroundRunoff.Distribute(0.3f, 0.5f, 0.3f, strength, slope, Loss, Half);
        Assert.All(r, v => Assert.Equal(0.3f, v, 3));
        Assert.Null(GroundRunoff.Distribute(0.3f, 0.5f, 0.3f, null, slope, Loss, Half));
    }
}
