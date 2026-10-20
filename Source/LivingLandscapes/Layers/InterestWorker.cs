using System;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="InterestWorker"/>: how much each kind of distinctive ground counts.</summary>
public class InterestExtension : DefModExtension
{
    /// <summary>Gaussian radius (cells) of the local relief measure (blurred slope).</summary>
    public float reliefSigma = 8f;
    public float reliefWeight = 1f;

    /// <summary>Gaussian radius of the edge measure: where rock or water meets open ground.</summary>
    public float edgeSigma = 5f;
    public float edgeWeight = 1f;

    /// <summary>Wavelength (cells) of the regional noise that clusters discoveries, and its weight.</summary>
    public float noiseScale = 70f;
    public float noiseWeight = 1f;
}

/// <summary>
/// How interesting each cell is for discoveries, 0..1, as a rank (a cell at 0.8 beats 80% of the map). Ground that stands
/// out (local relief, the edges of rock and water) and a regional noise that clusters it: ruins, shrines and other finds
/// gather in recognisable places instead of spreading evenly (Docs/GAMEPLAY-PLAN.md, G3). Computed at the Siting stage.
/// </summary>
public class InterestWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => GenerationStage.Siting;

    public override float[] Compute(EnvironmentContext context)
    {
        GroundCover[] cover = context.Environment.PlannedCover;
        float[] slope = context.Get(EnvironmentLayerDefOf.LL_Slope);
        if (cover == null || slope == null)
            return null;
        var p = def.GetModExtension<InterestExtension>() ?? new InterestExtension();
        int sizeX = context.SizeX, sizeZ = context.SizeZ, n = context.CellCount;
        MapGenFloatGrid elevation = MapGenerator.Elevation;
        var rock = new float[n];
        var water = new float[n];
        for (int i = 0; i < n; i++)
        {
            rock[i] = elevation[context.Map.cellIndices.IndexToCell(i)] > 0.7f ? 1f : 0f;
            water[i] = GroundCovers.IsWater(cover[i]) ? 1f : 0f;
        }
        float[] relief = GridMath.Blur(slope, sizeX, sizeZ, p.reliefSigma);
        float[] rockBlur = GridMath.Blur(rock, sizeX, sizeZ, p.edgeSigma);
        float[] waterBlur = GridMath.Blur(water, sizeX, sizeZ, p.edgeSigma);
        var noise = new Perlin(1.0 / p.noiseScale, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);

        var edge = new float[n];
        var regional = new float[n];
        for (int i = 0; i < n; i++)
        {
            // 4 b (1 - b) peaks where half the neighbourhood is rock (or water): an edge.
            edge[i] = Mathf.Max(4f * rockBlur[i] * (1f - rockBlur[i]), 4f * waterBlur[i] * (1f - waterBlur[i]));
            regional[i] = (float)noise.GetValue(i % sizeX, 0.0, i / sizeX);
        }
        float[] reliefRank = Rank(relief), edgeRank = Rank(edge), noiseRank = Rank(regional);
        float total = p.reliefWeight + p.edgeWeight + p.noiseWeight;
        var result = new float[n];
        for (int i = 0; i < n; i++)
            result[i] = (p.reliefWeight * reliefRank[i] + p.edgeWeight * edgeRank[i] + p.noiseWeight * noiseRank[i]) / total;
        return Rank(result);
    }

    /// <summary>Each value's rank among all, 0..1.</summary>
    private static float[] Rank(float[] values)
    {
        int n = values.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => values[a].CompareTo(values[b]));
        var rank = new float[n];
        for (int r = 0; r < n; r++)
            rank[order[r]] = r / (float)(n - 1);
        return rank;
    }
}
