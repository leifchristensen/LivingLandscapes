using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;

namespace LivingLandscapes.Layers;

/// <summary>
/// Upslope contributing area of each cell under multiple-flow-direction routing on the geological height, as log10(cells):
/// 0 on ridges, rising along gullies and valley floors. Drives wetness and where streams would form. Water is routed
/// over the depression-filled surface: hollows fill and spill on, so boulders and hummocks don't break up drainage.
/// </summary>
public class DrainageAreaWorker : EnvironmentLayerWorker
{
    /// <summary>Slope (height units per cell) given to filled hollows so water still crosses them toward the spill point.</summary>
    private const float FillGradient = 1e-3f;

    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Height.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Height);
        if (height == null)
            return null;
        var filled = (float[])height.Clone();
        GridMath.FillDepressions(filled, context.SizeX, context.SizeZ, _ => false, FillGradient);
        float[] area = GridMath.FlowAccumulationMfd(filled, context.SizeX, context.SizeZ);
        for (int i = 0; i < area.Length; i++)
            area[i] = Mathf.Log10(area[i]);
        return area;
    }
}

/// <summary>Steepness of the geological height, in height units per cell.</summary>
public class SlopeWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Height.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Height);
        return height == null ? null : GridMath.Slope(height, context.SizeX, context.SizeZ);
    }
}
