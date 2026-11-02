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

/// <summary>
/// How deep each cell lies below the spill level of its closed hollow (metres; 0 outside hollows): the water a hollow could
/// hold before it spills on (<see cref="Drainage.Spill"/>). Whether it does is the lakes' water balance.
/// </summary>
public class SpillDepthWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        Drainage drainage = Drainage.Of(context);
        if (drainage == null)
            return null;
        var depth = new float[drainage.Ground.Length];
        for (int i = 0; i < depth.Length; i++)
            depth[i] = Mathf.Max(0f, drainage.Spill[i] - drainage.Ground[i]);
        return depth;
    }
}

/// <summary>
/// Height above the nearest drainage line (HAND, metres; <see cref="Drainage.LineHeight"/>): how far above the stream,
/// river or full hollow its water reaches a cell stands. Low ground beside drainage lines has its water table near the surface.
/// </summary>
public class HeightAboveDrainageWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        Drainage drainage = Drainage.Of(context);
        if (drainage == null)
            return null;
        var p = EnvironmentLayerDefOf.LL_Wetness.GetModExtension<WetnessExtension>() ?? new WetnessExtension();
        RiverSystem rivers = context.Environment.PlannedRivers;
        float[] line = drainage.LineHeight(p.channelArea, rivers?.WaterLevel, rivers?.ChannelOf, out _);
        var above = new float[line.Length];
        for (int i = 0; i < above.Length; i++)
            above[i] = Mathf.Max(0f, drainage.Ground[i] - line[i]);
        return above;
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
