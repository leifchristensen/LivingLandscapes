using System;
using System.Collections.Generic;
using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// The jet kernel extracted from Sediment.Jet (step D2). The reference below is a verbatim copy of the old inline code, so the
/// extraction is proved to return the same numbers.
/// </summary>
public class JetKernelTests
{
    private const int Size = 41;

    /// <summary>The old Sediment.Jet weight loop, copied before the refactor.</summary>
    private static float OldWeights(int c, Vector2 axis, float w, float level, float[] natural, int sizeX, int sizeZ, float settle, float reach,
        float spreadRate, List<(int cell, float weight, float d)> weights)
    {
        var across = new Vector2(-axis.y, axis.x);
        int cx = c % sizeX, cz = c / sizeX, r = Mathf.CeilToInt(reach);
        weights.Clear();
        float sum = 0f;
        for (int dz = -r; dz <= r; dz++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                var offset = new Vector2(dx, dz);
                float along = Vector2.Dot(offset, axis), side = Vector2.Dot(offset, across);
                if (along < -0.5f || along > reach)
                    continue;
                int i = z * sizeX + x;
                if (natural[i] > level)
                    continue;
                float half = 0.5f * w + spreadRate * Mathf.Max(along, 0f);
                float weight = Mathf.Exp(-Mathf.Max(along, 0f) / settle) * Mathf.Exp(-side * side / (2f * half * half));
                if (weight < 1e-4f)
                    continue;
                weights.Add((i, weight, Mathf.Max(along, 0f)));
                sum += weight;
            }
        }
        return sum;
    }

    private static float[] Ground()
    {
        var g = new float[Size * Size];
        for (int i = 0; i < g.Length; i++)
            g[i] = -((i * 7) % 5) * 0.25f; // a few heights, some above the test levels
        return g;
    }

    [Theory]
    [InlineData(1f, 1f, 0f, -0.5f)]
    [InlineData(3f, 0f, 1f, -0.25f)]
    [InlineData(2f, -0.6f, 0.8f, -0.5f)]
    [InlineData(5f, 0.8f, 0.6f, 10f)]
    [InlineData(8f, -0.7071068f, -0.7071068f, -0.75f)]
    public void Weights_EqualTheOldInlineCode(float w, float ax, float az, float level)
    {
        float[] ground = Ground();
        int c = 20 * Size + 20;
        float settle = 4f * w, reach = settle * 3f, spread = 0.12f;
        var expected = new List<(int cell, float weight, float d)>();
        var actual = new List<(int cell, float weight, float d)>();
        float sumOld = OldWeights(c, new Vector2(ax, az), w, level, ground, Size, Size, settle, reach, spread, expected);
        float sumNew = JetKernel.Weights(c, ax, az, w, level, ground, Size, Size, settle, reach, spread, actual);
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(sumOld, sumNew, 12);
        for (int k = 0; k < expected.Count; k++)
        {
            Assert.Equal(expected[k].cell, actual[k].cell);
            Assert.Equal(expected[k].weight, actual[k].weight, 12);
            Assert.Equal(expected[k].d, actual[k].d, 12);
        }
    }

    [Fact]
    public void Weights_FallWithDistanceAlongTheAxis()
    {
        var weights = new List<(int cell, float weight, float d)>();
        var ground = new float[Size * Size];
        JetKernel.Weights(20 * Size + 5, 1f, 0f, 3f, float.PositiveInfinity, ground, Size, Size, 6f, 18f, 0.12f, weights);
        float onAxis0 = 0f, onAxis10 = 0f;
        foreach (var (cell, weight, d) in weights)
        {
            if (cell == 20 * Size + 5)
                onAxis0 = weight;
            if (cell == 20 * Size + 15)
                onAxis10 = weight;
        }
        Assert.Equal(1f, onAxis0, 6);
        Assert.Equal(Mathf.Exp(-10f / 6f), onAxis10, 6);
    }

    [Fact]
    public void Weights_SkipGroundAboveTheLevel_AndCellsBehindTheMouth()
    {
        var weights = new List<(int cell, float weight, float d)>();
        var ground = new float[Size * Size];
        ground[20 * Size + 8] = 1f;
        JetKernel.Weights(20 * Size + 5, 1f, 0f, 3f, 0f, ground, Size, Size, 6f, 18f, 0.12f, weights);
        Assert.DoesNotContain(weights, t => t.cell == 20 * Size + 8);
        Assert.DoesNotContain(weights, t => t.cell == 20 * Size + 3);
    }

    [Fact]
    public void Weights_AtTheMapEdgePointingOut_KeepOnlyTheEdgeColumn_AndClearTheList()
    {
        var weights = new List<(int cell, float weight, float d)> { (999, 1f, 0f) };
        var ground = new float[Size * Size];
        float sum = JetKernel.Weights(20 * Size, -1f, 0f, 2f, float.PositiveInfinity, ground, Size, Size, 4f, 8f, 0.12f, weights);
        Assert.DoesNotContain(weights, t => t.cell == 999);
        Assert.All(weights, t => Assert.Equal(0, t.cell % Size));
        Assert.True(sum > 0f);
    }
}
