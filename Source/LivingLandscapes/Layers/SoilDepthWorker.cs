using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Soil depth in metres, from the natural landform (<see cref="SoilModel"/>). Computed at the same stage as the
/// height: with geological terrain on it decides where bedrock is exposed; with it off, vanilla's rock already
/// stands, so those cells read 0.
/// </summary>
public class SoilDepthWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Landform);
        if (height == null)
            return null;
        var p = def.GetModExtension<SoilModelExtension>() ?? new SoilModelExtension();
        float cellSize = (EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension()).cellSize;
        float[] soil = SoilModel.SoilDepth(height, context.SizeX, context.SizeZ, cellSize, p, context.Get(EnvironmentLayerDefOf.LL_RockStrength));

        if (StageFor(context.Map) == GenerationStage.Plants)
        {
            TerrainFeature[] features = TerrainFeatures.Classify(context.Map);
            for (int i = 0; i < soil.Length; i++)
            {
                if (features[i] == TerrainFeature.Rock)
                    soil[i] = 0f;
            }
        }
        return soil;
    }
}
