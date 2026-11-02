using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;

namespace LivingLandscapes.Layers;

/// <summary>
/// Upslope contributing area of each cell under multiple-flow-direction routing on the natural landform, as log10(cells):
/// 0 on ridges, rising along gullies and valley floors. Drives wetness and where streams would form. Water is routed
/// over the depression-filled surface (<see cref="Drainage"/>): hollows fill and spill on, so boulders and hummocks don't
/// break up drainage.
/// </summary>
public class DrainageAreaWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        Drainage drainage = Drainage.Of(context);
        if (drainage == null)
            return null;
        var area = new float[drainage.Area.Length];
        for (int i = 0; i < area.Length; i++)
            area[i] = Mathf.Log10(drainage.Area[i]);
        return area;
    }
}

/// <summary>Steepness of the natural landform, in height units per cell (terraces only shape walking and cliffs).</summary>
public class SlopeWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Landform);
        return height == null ? null : GridMath.Slope(height, context.SizeX, context.SizeZ);
    }
}
