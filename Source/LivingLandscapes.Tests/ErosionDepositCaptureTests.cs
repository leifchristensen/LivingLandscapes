using System;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// The pre-move arithmetic of LandscapeGenerator.Deposit (issue #186), kept verbatim as a twin of
/// <see cref="ErosionDepositTests"/> and pinned to the same literals. Together the two tests prove the move into
/// <see cref="LivingLandscapes.Geology.ErosionDeposit"/> changed nothing: if the literals were re-derived to fit a new
/// formula, this test (old code vs the literals) would fail. p.deposition became a float parameter; Mathf here is the
/// test project's shim (identical to System.Math for Min/Max).
/// </summary>
public class ErosionDepositCaptureTests
{
    /// <summary>Verbatim copy of LandscapeGenerator.Deposit as of main (88dda93), LandscapeGenerator.cs:907-940.</summary>
    private static void OldDeposit(float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver, float[] area,
        int[] stack, int count, bool[] outlet, bool[] sink, float[] baseLevel, float[] deposited, float deposition)
    {
        int n = h.Length;
        Array.Clear(load, 0, n);
        float yield = 0f;
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            if (!outlet[i])
            {
                yield += eroded[i];
                land++;
            }
        }
        yield /= Mathf.Max(1, land);
        for (int k = count - 1; k >= 0; k--)
        {
            int c = stack[k];
            load[c] += eroded[c] + inflow[c] * yield;
            int r = receiver[c];
            if (r >= 0)
            {
                float keep = Mathf.Min(load[c], deposition * load[c] / Mathf.Max(area[c], 1f));
                h[c] += keep;
                deposited[c] += keep;
                load[r] += load[c] - keep;
            }
            else if (sink[c])
            {
                float before = h[c];
                h[c] = Mathf.Min(h[c] + load[c], baseLevel[c]);
                deposited[c] += Mathf.Max(0f, h[c] - before);
            }
        }
    }

    [Fact]
    public void OldArithmeticMatchesThePinnedLiterals()
    {
        var h = new[] { 0f, 0.30f, 0.50f, 0.70f, 0.248f };
        var eroded = new[] { 0.01f, 0.02f, 0.03f, 0.04f, 0.005f };
        var load = new float[5];
        var inflow = new[] { 0f, 0f, 0f, 1f, 0f };
        var receiver = new[] { -1, 0, 1, 2, -1 };
        var area = new[] { 12f, 10f, 6f, 1f, 4f };
        var stack = new[] { 0, 1, 2, 3, 4 };
        var outlet = new[] { true, false, false, false, false };
        var sink = new[] { false, false, false, false, true };
        var baseLevel = new[] { 0f, float.NaN, float.NaN, float.NaN, 0.25f };
        var deposited = new float[5];
        OldDeposit(h, eroded, load, inflow, receiver, area, stack, 5, outlet, sink, baseLevel, deposited, 1.5f);
        // The same literals ErosionDepositTests pins ErosionDeposit.Apply to (captured from this code, round 1).
        Assert.Equal(0f, h[0], 6);
        Assert.Equal(0.306375f, h[1], 6);
        Assert.Equal(0.5075f, h[2], 6);
        Assert.Equal(0.76375f, h[3], 6);
        Assert.Equal(0.25f, h[4], 6);
        Assert.Equal(0f, deposited[0], 6);
        Assert.Equal(0.006375f, deposited[1], 6);
        Assert.Equal(0.0075f, deposited[2], 6);
        Assert.Equal(0.06375f, deposited[3], 6);
        Assert.Equal(0.002f, deposited[4], 6);
        Assert.Equal(0.046125f, load[0], 6);
        Assert.Equal(0.0425f, load[1], 6);
        Assert.Equal(0.03f, load[2], 6);
        Assert.Equal(0.06375f, load[3], 6);
        Assert.Equal(0.005f, load[4], 6);
    }
}
