using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="DebrisWorker"/>, set on the layer Def in XML.</summary>
public class DebrisExtension : DefModExtension
{
    /// <summary>Bare rock steeper than this (rise over run) sheds rock fragments.</summary>
    public float faceSlope = 0.5f;

    /// <summary>Fragments roll on while the ground is steeper than this, and come to rest where it eases below it.</summary>
    public float restSlope = 0.25f;

    /// <summary>Furthest a fragment travels (cells) before it stops.</summary>
    public int maxRun = 40;

    /// <summary>Spread (cells) of the resting fragments, so debris forms fans and aprons rather than single cells.</summary>
    public float spread = 1.5f;
}

/// <summary>
/// Rock debris (rule S4): fragments shed by bare rock faces roll down the steepest way until the slope eases, and
/// collect there. The value is the number of faces feeding a cell (after spreading), so scree gathers in fans and
/// aprons at the foot of rock, thick below big faces and absent below gentle ground.
/// </summary>
public class DebrisWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Landform);
        float[] soil = context.Get(EnvironmentLayerDefOf.LL_SoilDepth);
        if (height == null || soil == null)
            return null;
        var p = def.GetModExtension<DebrisExtension>() ?? new DebrisExtension();
        var soilModel = EnvironmentLayerDefOf.LL_SoilDepth.GetModExtension<SoilModelExtension>() ?? new SoilModelExtension();
        float cellSize = (EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension()).cellSize;
        int sizeX = context.SizeX, sizeZ = context.SizeZ, n = height.Length;
        float[] slope = GridMath.Slope(height, sizeX, sizeZ);

        var rest = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (soil[i] >= soilModel.rockWallDepth || slope[i] / cellSize < p.faceSlope)
                continue;
            int c = i;
            for (int step = 0; step < p.maxRun && slope[c] / cellSize >= p.restSlope; step++)
            {
                int next = -1;
                float best = 0f;
                GridMath.ForEachNeighbour(c, sizeX, sizeZ, nb =>
                {
                    float drop = height[c] - height[nb];
                    if (drop > best)
                    {
                        best = drop;
                        next = nb;
                    }
                });
                if (next < 0)
                    break;
                c = next;
            }
            // Fragments resting on bare rock are still in the rock mass, not debris on the ground.
            if (soil[c] >= soilModel.rockWallDepth)
                rest[c] += 1f;
        }

        var ones = new float[n];
        for (int i = 0; i < n; i++)
            ones[i] = 1f;
        float[] spread = GridMath.MaskedBlur(rest, ones, sizeX, sizeZ, p.spread);
        float scale = 2f * Mathf.PI * p.spread * p.spread;
        for (int i = 0; i < n; i++)
            spread[i] *= scale;
        return spread;
    }
}
