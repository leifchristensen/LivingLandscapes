using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;

namespace LivingLandscapes.Layers;

/// <summary>
/// Upslope contributing area of each cell under multiple-flow-direction routing on the geological height, as log10(cells):
/// 0 on ridges, rising along gullies and valley floors. Drives wetness and where streams would form.
/// </summary>
public class DrainageAreaWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Height);
        if (height == null)
            return null;
        float[] area = GridMath.FlowAccumulationMfd(height, context.SizeX, context.SizeZ);
        for (int i = 0; i < area.Length; i++)
            area[i] = Mathf.Log10(area[i]);
        return area;
    }
}

/// <summary>Steepness of the geological height, in height units per cell.</summary>
public class SlopeWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Height);
        return height == null ? null : GridMath.Slope(height, context.SizeX, context.SizeZ);
    }
}
