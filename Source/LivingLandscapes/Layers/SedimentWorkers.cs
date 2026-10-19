using LivingLandscapes.Environment;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Alluvium thickness (metres) the channels deposited (<see cref="Geology.Sediment"/>); 0 without channels.</summary>
public class AlluviumWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        if (StageFor(context.Map) != GenerationStage.Elevation)
            return null;
        return context.Environment.PlannedRivers?.Alluvium ?? new float[context.CellCount];
    }
}

/// <summary>Grain size of the alluvium, 0 clay and silt to 1 gravel; 0 where there is none.</summary>
public class GrainSizeWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        if (StageFor(context.Map) != GenerationStage.Elevation)
            return null;
        return context.Environment.PlannedRivers?.GrainSize ?? new float[context.CellCount];
    }
}
