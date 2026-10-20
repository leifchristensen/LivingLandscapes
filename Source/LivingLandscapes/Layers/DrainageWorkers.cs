using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;

namespace LivingLandscapes.Layers;

/// <summary>
/// Upslope contributing area of each cell under multiple-flow-direction routing on the natural landform, as log10(cells):
/// 0 on ridges, rising along gullies and valley floors. Drives wetness and where streams would form. Water is routed
/// over the depression-filled surface: hollows fill and spill on, so boulders and hummocks don't break up drainage.
/// </summary>
public class DrainageAreaWorker : EnvironmentLayerWorker
{
    /// <summary>Slope (height units per cell) given to filled hollows so water still crosses them toward the spill point.</summary>
    private const float FillGradient = 1e-3f;

    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Landform);
        if (height == null)
            return null;
        var filled = (float[])height.Clone();
        GridMath.FillDepressions(filled, context.SizeX, context.SizeZ, _ => false, FillGradient);
        float[] area = GridMath.FlowAccumulationMfd(filled, context.SizeX, context.SizeZ, inflow: RiverInflow(context));
        for (int i = 0; i < area.Length; i++)
            area[i] = Mathf.Log10(area[i]);
        return area;
    }

    /// <summary>
    /// Channels entering from beyond the map bring the water of all the land upstream: the drainage area the erosion
    /// model gathered for them enters where each crosses into the map.
    /// </summary>
    private static float[] RiverInflow(EnvironmentContext context)
    {
        RiverSystem rivers = context.Map.GetComponent<MapComponent_Environment>()?.PlannedRivers;
        if (rivers == null)
            return null;
        var inflow = new float[context.SizeX * context.SizeZ];
        foreach (RiverPlan channel in rivers.Channels)
        {
            int entry = channel.Main.FindIndex(v => v.x >= 0f && v.y >= 0f && v.x < context.SizeX && v.y < context.SizeZ);
            if (entry >= 0 && channel.EntryArea > 0f)
                inflow[RiverGrid.Index(channel.Main[entry], context.SizeX, context.SizeZ)] += channel.EntryArea;
        }
        return inflow;
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
