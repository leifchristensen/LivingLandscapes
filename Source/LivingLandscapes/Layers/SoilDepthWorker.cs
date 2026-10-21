using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
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

        FloatRange share = p.RockShareFor(context.Map.TileInfo.HillinessForElevationGen);
        if (StageFor(context.Map) == GenerationStage.Elevation && share.max > 0f)
        {
            bool[] sea = Coastline.SeaCells(context.Map);
            float target = share.RandomInRange;
            float before = Share(soil, context, cellSize, p, sea);
            string how;
            if (before > share.max)
            {
                // Too steep for the tile: soil would have to stay on steeper ground. Raise the bare-rock slope until the share fits.
                // (An even shift cannot do it: bare faces have no soil to lose, and they all turn to soil at once.)
                float[] rockStrength = context.Get(EnvironmentLayerDefOf.LL_RockStrength);
                float low = 1f, high = 8f;
                for (int step = 0; step < 12; step++)
                {
                    float mid = (low + high) / 2f;
                    float[] trial = SoilModel.SoilDepth(height, context.SizeX, context.SizeZ, cellSize, p, rockStrength, mid);
                    if (Share(trial, context, cellSize, p, sea) > target)
                        low = mid;
                    else
                        high = mid;
                }
                soil = SoilModel.SoilDepth(height, context.SizeX, context.SizeZ, cellSize, p, rockStrength, high);
                how = $"bare-rock slope x{high:0.0#}";
            }
            else if (before < share.min)
            {
                float[] exposure = SoilModel.Exposure(height, context.Get(EnvironmentLayerDefOf.LL_RockStrength), context.SizeX, context.SizeZ, p);
                float shift = SoilModel.CalibrateRock(soil, context.SizeX, context.SizeZ, cellSize, p, target, sea, exposure);
                how = $"soil shifted {shift:+0.00;-0.00} m";
            }
            else
                how = "kept";
            Log.Message($"[LivingLandscapes] Rock share on {context.Map} ({context.Map.TileInfo.HillinessForElevationGen}): the erosion model gave {before:P0}, the tile's range is {share.min:P0} to {share.max:P0}; {how} for {target:P0}.");
        }
        // Alluvium is soil the rivers laid down: floodplains, levees and bars are deep ground.
        float[] alluvium = context.Get(EnvironmentLayerDefOf.LL_Alluvium);
        if (alluvium != null && StageFor(context.Map) == GenerationStage.Elevation)
        {
            for (int i = 0; i < soil.Length; i++)
                soil[i] += alluvium[i];
        }
        // Delta land is fresh sediment the river built: deep ground, never bare rock (its front is under the sea).
        bool[] deltaLand = context.Environment?.PlannedRivers?.DeltaLand;
        if (deltaLand != null && StageFor(context.Map) == GenerationStage.Elevation)
        {
            for (int i = 0; i < soil.Length; i++)
            {
                if (deltaLand[i])
                    soil[i] = Mathf.Max(soil[i], p.deltaSoilDepth);
            }
        }
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

    private static float Share(float[] soil, EnvironmentContext context, float cellSize, SoilModelExtension p, bool[] sea)
    {
        float[] depth = GridMath.DistanceTo(i => soil[i] >= p.rockWallDepth, context.SizeX, context.SizeZ);
        return SoilModel.RockShare(depth, cellSize, p, sea);
    }
}
