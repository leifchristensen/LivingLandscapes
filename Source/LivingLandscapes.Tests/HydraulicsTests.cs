using System;
using System.Collections.Generic;
using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Step S2a of the hydrology unification: <see cref="Hydraulics"/> is the old inline code moved, so each function must equal
/// the copy of the old code kept here (the "legacy" methods), exactly, on a branching network.
/// </summary>
public class HydraulicsTests
{
    internal const int SizeX = 24, SizeZ = 20;

    /// <summary>A tilted, bumpy surface drained by steepest descent: a tree with joins and the occasional diagonal step.</summary>
    internal static (List<int> cells, int[] receiver, int[] main, float[] discharge, float[] filled, float[] depth, float[] width) Network()
    {
        int n = SizeX * SizeZ;
        var h = new float[n];
        for (int z = 0; z < SizeZ; z++)
        {
            for (int x = 0; x < SizeX; x++)
                h[z * SizeX + x] = 0.6f * (SizeX - x) + 0.35f * Math.Abs(z - 9) + 0.8f * (float)Math.Sin(x * 0.9 + z * 1.7) + 0.5f * (float)Math.Cos(z * 0.6 - x * 0.4);
        }
        var receiver = new int[n];
        for (int c = 0; c < n; c++)
        {
            receiver[c] = -1;
            float best = 0f;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = c % SizeX + dx, z = c / SizeX + dz;
                    if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= SizeX || z >= SizeZ)
                        continue;
                    float drop = (h[c] - h[z * SizeX + x]) / (float)Math.Sqrt(dx * dx + dz * dz);
                    if (drop > best)
                    {
                        best = drop;
                        receiver[c] = z * SizeX + x;
                    }
                }
            }
        }
        var order = new List<int>();
        for (int i = 0; i < n; i++)
            order.Add(i);
        order.Sort((a, b) => h[b].CompareTo(h[a]));
        var discharge = new float[n];
        for (int i = 0; i < n; i++)
            discharge[i] = 1f;
        foreach (int c in order)
        {
            if (receiver[c] >= 0)
                discharge[receiver[c]] += discharge[c];
        }
        var cells = order.FindAll(c => discharge[c] >= 3f);
        var main = new int[n];
        for (int i = 0; i < n; i++)
            main[i] = -1;
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r >= 0 && discharge[r] >= 3f && (main[r] < 0 || discharge[c] > discharge[main[r]]))
                main[r] = c;
        }
        var depth = new float[n];
        var width = new float[n];
        for (int i = 0; i < n; i++)
        {
            depth[i] = 0.2f + 0.05f * (float)Math.Sqrt(discharge[i]);
            width[i] = 0.5f + 0.3f * (float)Math.Sqrt(discharge[i]);
        }
        return (cells, receiver, main, discharge, h, depth, width);
    }

    // The old Sediment.Slope.
    private static float LegacySlope(int c, int[] receiver, float[] filled, int sizeX)
    {
        int d = c;
        float run = 0f;
        for (int s = 0; s < 5 && receiver[d] >= 0; s++)
        {
            int r = receiver[d];
            run += (r % sizeX != d % sizeX && r / sizeX != d / sizeX) ? 1.414f : 1f;
            d = r;
        }
        return run > 0f ? Mathf.Max(0f, filled[c] - filled[d]) / run : 0f;
    }

    // The old Sediment.MeanSlope.
    private static float LegacyMeanSlope(int c, int[] receiver, float[] filled, bool[] baseLevel, int sizeX)
    {
        int d = c;
        float run = 0f;
        for (int s = 0; s < filled.Length && receiver[d] >= 0 && (baseLevel == null || !baseLevel[d]); s++)
        {
            int r = receiver[d];
            run += (r % sizeX != d % sizeX && r / sizeX != d / sizeX) ? 1.414f : 1f;
            d = r;
        }
        return run > 0f ? Mathf.Max(0f, filled[c] - filled[d]) / run : 0f;
    }

    // The old momentum inside Sediment.Spread.
    internal static Vector2[] LegacyMomentum(List<int> cells, int[] receiver, float[] discharge, int sizeX, int n)
    {
        var momentum = new Vector2[n];
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            var step = new Vector2(r % sizeX - c % sizeX, r / sizeX - c / sizeX).normalized;
            Vector2 carried = momentum[c] == Vector2.zero ? step : momentum[c].normalized;
            momentum[r] += (carried + step).normalized * discharge[c];
        }
        return momentum;
    }

    // The old per-cell speeds inside Impact.
    internal static float[] LegacySpeeds(List<int> cells, int[] main, float[] filled, float[] depth, float[] width, int sizeX, int momentumSteps,
        float manningN, float jetWidths)
    {
        Vector2 At(int c) => new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
        var speeds = new float[filled.Length];
        foreach (int c in cells)
        {
            int up = c;
            float run = 0f;
            for (int s = 0; s < momentumSteps && main[up] >= 0; s++)
            {
                run += (At(main[up]) - At(up)).magnitude;
                up = main[up];
            }
            float slope = Mathf.Max(0f, filled[up] - filled[c]) / Mathf.Max(run, 1f);
            float d = Mathf.Max(depth[c], 0.01f);
            float speed = Mathf.Min(Mathf.Pow(d, 2f / 3f) * Mathf.Sqrt(Mathf.Max(slope, 1e-6f)) / manningN, Mathf.Sqrt(9.81f * d));
            int donor = main[c];
            if (donor >= 0)
                speed = Mathf.Max(speed, speeds[donor] * Mathf.Exp(-(At(c) - At(donor)).magnitude / (jetWidths * Mathf.Max(width[c], 1f))));
            speeds[c] = speed;
        }
        return speeds;
    }

    [Fact]
    public void Fixture_HasJoinsAndDiagonals()
    {
        var (cells, receiver, main, discharge, _, _, _) = Network();
        int joins = 0, diagonals = 0;
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            if (main[r] >= 0 && main[r] != c)
                joins++;
            if (Hydraulics.StepLength(c, r, SizeX) > 1f)
                diagonals++;
        }
        Assert.True(cells.Count > 40);
        Assert.True(joins >= 3, $"{joins} joins");
        Assert.True(diagonals >= 3, $"{diagonals} diagonal steps");
    }

    [Fact]
    public void ReachSlope_EqualsTheOldTransportSlope()
    {
        var (cells, receiver, _, _, filled, _, _) = Network();
        for (int c = 0; c < filled.Length; c++)
            Assert.Equal(LegacySlope(c, receiver, filled, SizeX), Hydraulics.ReachSlope(c, receiver, filled, SizeX));
    }

    [Fact]
    public void ReachSlope_ToBaseLevel_EqualsTheOldMeanSlope()
    {
        var (cells, receiver, _, _, filled, _, _) = Network();
        var baseLevel = new bool[filled.Length];
        for (int c = 0; c < filled.Length; c += 7)
            baseLevel[c] = true;
        foreach (bool[] stop in new[] { null, baseLevel })
        {
            for (int c = 0; c < filled.Length; c++)
                Assert.Equal(LegacyMeanSlope(c, receiver, filled, stop, SizeX), Hydraulics.ReachSlope(c, receiver, filled, SizeX, filled.Length, stop));
        }
    }

    [Fact]
    public void Momentum_EqualsTheOldSpreadMomentum()
    {
        var (cells, receiver, _, discharge, filled, _, _) = Network();
        Vector2[] old = LegacyMomentum(cells, receiver, discharge, SizeX, filled.Length);
        Vector2[] now = Hydraulics.Momentum(cells, receiver, discharge, SizeX);
        Assert.Equal(old.Length, now.Length);
        int nonZero = 0;
        for (int i = 0; i < old.Length; i++)
        {
            Assert.Equal(old[i].x, now[i].x);
            Assert.Equal(old[i].y, now[i].y);
            if (now[i] != Vector2.zero)
                nonZero++;
        }
        Assert.True(nonZero > 20);
    }

    [Fact]
    public void Speeds_EqualTheOldImpactSpeeds()
    {
        var (cells, _, main, _, filled, depth, width) = Network();
        const int steps = 8;
        const float manningN = 0.035f, jetWidths = 5f;
        float[] old = LegacySpeeds(cells, main, filled, depth, width, SizeX, steps, manningN, jetWidths);
        var now = new float[filled.Length];
        Vector2 At(int c) => new Vector2(c % SizeX + 0.5f, c / SizeX + 0.5f);
        foreach (int c in cells)
        {
            float slope = Hydraulics.UpstreamSlope(c, main, filled, SizeX, steps, out _, out _);
            float speed = Hydraulics.Speed(depth[c], slope, manningN);
            int donor = main[c];
            if (donor >= 0)
                speed = Hydraulics.WithJet(speed, now[donor], (At(c) - At(donor)).magnitude, jetWidths * Mathf.Max(width[c], 1f));
            now[c] = speed;
        }
        foreach (int c in cells)
            Assert.Equal(old[c], now[c]);
    }

    [Fact]
    public void Speed_IsCappedAtTheCriticalSpeed_AndNeverNaNOnFlatOrAdverseSlope()
    {
        float steep = Hydraulics.Speed(0.5f, 10f, 0.035f);
        Assert.Equal(Mathf.Sqrt(Hydraulics.Gravity * 0.5f), steep);
        Assert.True(Hydraulics.Speed(0.5f, 0f, 0.035f) > 0f);
        Assert.True(Hydraulics.Speed(0.5f, -1f, 0.035f) > 0f);
        Assert.True(Hydraulics.Speed(0f, 0.01f, 0.035f) > 0f);
    }

    [Fact]
    public void Directions_UnitOrZero_AndAlongTheFlow()
    {
        var (cells, receiver, main, _, _, _, _) = Network();
        Vector2[] direction = Hydraulics.Directions(cells, receiver, main, SizeX, 4);
        foreach (int c in cells)
        {
            float length = direction[c].magnitude;
            Assert.True(length == 0f || Math.Abs(length - 1f) < 1e-5f);
        }
    }
}
