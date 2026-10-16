using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="MoistureWorker"/>, set on the layer Def in XML.</summary>
public class MoistureExtension : DefModExtension
{
    /// <summary>Distance (cells) over which water proximity falls to 1/e.</summary>
    public float waterReachCells = 10f;
}

/// <summary>
/// 0..1 moisture: topographic wetness (<see cref="WetnessWorker"/>: drainage, water table, climate), raised toward 1
/// near surface water.
/// </summary>
public class MoistureWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        float[] distance = context.Get(EnvironmentLayerDefOf.LL_WaterDistance);
        float[] wetness = context.Get(EnvironmentLayerDefOf.LL_Wetness);
        if (distance == null)
            return null;

        var p = def.GetModExtension<MoistureExtension>() ?? new MoistureExtension();
        var moisture = new float[distance.Length];
        for (int i = 0; i < moisture.Length; i++)
        {
            float proximity = Mathf.Exp(-distance[i] / p.waterReachCells);
            float ground = wetness?[i] ?? 0f;
            moisture[i] = 1f - (1f - ground) * (1f - proximity);
        }
        return moisture;
    }
}
