using System;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Geological terrain ("On" mode): after vanilla fills its elevation grid, generate the geological height and soil,
/// then overwrite the grid with bedrock exposure so vanilla's rock, roof, gravel, cave and structure placement
/// follows the geology. Tile mutators (lakes, rivers, coasts...) still run after this, as in vanilla.
/// </summary>
[HarmonyPatch(typeof(GenStep_ElevationFertility), nameof(GenStep_ElevationFertility.Generate))]
public static class Patch_GenStep_ElevationFertility_Generate
{
    public static void Postfix(Map map)
    {
        if (!GeologicalTerrain.AppliesTo(map))
            return;
        try
        {
            var environment = map.GetComponent<MapComponent_Environment>();
            if (environment == null)
                return;
            environment.ComputeLayers(GenerationStage.Elevation);
            if (!environment.TryGetGrid(EnvironmentLayerDefOf.LL_SoilDepth, out EnvironmentGrid soil)
                || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid landform))
                return;

            var generation = EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension();
            var soilModel = EnvironmentLayerDefOf.LL_SoilDepth.GetModExtension<SoilModelExtension>() ?? new SoilModelExtension();
            var terraces = EnvironmentLayerDefOf.LL_Height.GetModExtension<TerraceExtension>() ?? new TerraceExtension();
            environment.PlannedCliffs = VanillaTerrainWriter.Write(map, soil.ToArray(), landform.ToArray(), generation.cellSize, soilModel, terraces);
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Geological terrain failed; this map uses vanilla terrain. {e}");
        }
    }
}
