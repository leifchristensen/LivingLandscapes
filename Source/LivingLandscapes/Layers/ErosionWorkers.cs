using LivingLandscapes.Environment;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Erodibility K the erosion model cut each cell with (P2): the landform's factor times the exposed bed's, 1 for average rock,
/// higher for weak rock. Only with geological terrain on. Hydrology reads it for how fast a channel can cut or scour.
/// </summary>
public class ErodibilityWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        if (context.Get(EnvironmentLayerDefOf.LL_Landform) == null)
            return null;
        return context.Environment.PlannedErosion?.Erodibility;
    }
}

/// <summary>
/// Metres of deposit the erosion model left standing (fluvial deposition, Davy and Lague 2009, less what later incision removed).
/// The regional, model-scale deposit already part of the landform; <see cref="AlluviumWorker"/> is the channel-scale layer the
/// map pass lays on top (a different quantity: the two are never the same material).
/// </summary>
public class DepositionWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        if (context.Get(EnvironmentLayerDefOf.LL_Landform) == null)
            return null;
        return context.Environment.PlannedErosion?.Deposition;
    }
}

/// <summary>Hard beds below each cell's surface (<see cref="Geology.RockStrata.HardBedsBelow"/>): the terrace stages follow it.</summary>
public class RockStageWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        int[] stage = context.Environment.PlannedRockStage;
        if (stage == null || context.Get(EnvironmentLayerDefOf.LL_Landform) == null)
            return null;
        var values = new float[stage.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = stage[i];
        return values;
    }
}
