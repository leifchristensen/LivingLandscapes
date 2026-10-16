using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="MoistureWorker"/>, set on the layer Def in XML.</summary>
public class MoistureExtension : DefModExtension
{
    /// <summary>Distance (cells) over which water proximity falls to 1/e.</summary>
    public float waterReachCells = 10f;

    /// <summary>Rainfall (mm) treated as fully wet for the regional baseline.</summary>
    public float wetRainfallMm = 2000f;

    /// <summary>Weight of the regional rainfall baseline against local water proximity.</summary>
    public float rainfallWeight = 0.6f;

    /// <summary>Baseline used when the tile reports no rainfall (pocket maps, mod-made tiles).</summary>
    public float unknownRainfallBaseline = 0.4f;
}

/// <summary>
/// 0..1 moisture: a regional baseline from tile rainfall, raised towards 1 near water.
/// MVP model; height above drainage and wetness index will refine it once elevation is realistic.
/// </summary>
public class MoistureWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        float[] distance = context.Get(EnvironmentLayerDefOf.LL_WaterDistance);
        if (distance == null)
            return null;

        var p = def.GetModExtension<MoistureExtension>() ?? new MoistureExtension();
        float rainfall = context.Map.TileInfo?.rainfall ?? 0f;
        float baseline = rainfall > 0f ? Mathf.Clamp01(rainfall / p.wetRainfallMm) : p.unknownRainfallBaseline;
        float dry = 1f - p.rainfallWeight * baseline;

        var moisture = new float[distance.Length];
        for (int i = 0; i < moisture.Length; i++)
        {
            float proximity = Mathf.Exp(-distance[i] / p.waterReachCells);
            moisture[i] = 1f - dry * (1f - proximity);
        }
        return moisture;
    }
}
