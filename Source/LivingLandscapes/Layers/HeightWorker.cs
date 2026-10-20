using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// The walkable height, metres above sea level, that drainage, slope, moisture and plants read. With geological
/// terrain on, steep parts of the landform are stepped into terraces (<see cref="Terraces"/>) whose edges get cliffs;
/// gentle ground and the "off" mode keep the landform unchanged.
/// </summary>
public class HeightWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] landform = context.Get(EnvironmentLayerDefOf.LL_Landform);
        if (landform == null)
            return null;
        if (!GeologicalTerrain.AppliesTo(context.Map))
            return landform;
        var p = def.GetModExtension<TerraceExtension>() ?? new TerraceExtension();
        float cellSize = (EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension()).cellSize;
        // The one terrace plan of the map, from the full-precision landform; the cliffs are placed from it too.
        TerracePlan plan = Terraces.Plan(landform, context.SizeX, context.SizeZ, cellSize, p, context.Environment.PlannedRockStage);
        context.Environment.PlannedTerraces = plan;
        return Terraces.Apply(landform, plan, p);
    }
}
