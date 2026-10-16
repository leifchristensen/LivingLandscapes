using LivingLandscapes.Environment;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Strength (0..1) of the rock beneath each cell, from the tilted beds and joints the geology generated
/// (<see cref="Geology.RockStrata"/>). Only exists with geological terrain on.
/// </summary>
public class RockStrengthWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        if (context.Get(EnvironmentLayerDefOf.LL_Landform) == null)
            return null;
        return context.Map.GetComponent<MapComponent_Environment>()?.PlannedRockStrength;
    }
}
