using HarmonyLib;
using LivingLandscapes.Environment;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Land in the sea: where rivers built ground above the sea (<see cref="Geology.RiverSystem.DeltaLand"/>, and the erosion model's
/// <see cref="MapComponent_Environment.PlannedDeltaLand"/>) and where resistant rock stands as sea stacks
/// (<see cref="MapComponent_Environment.PlannedStacks"/>), the coast mutator leaves the terrain the geology planned instead of
/// painting sea or beach over it. Elsewhere the sea is shallow or deep by the depth of the sea floor.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_Coast), "CoastTerrainAt")]
public static class Patch_TileMutatorWorker_Coast_CoastTerrainAt
{
    public static void Postfix(IntVec3 cell, Map map, ref TerrainDef __result)
    {
        if (__result == null)
            return;
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !cell.InBounds(map))
            return;
        int i = map.cellIndices.CellToIndex(cell);
        // Land the channels built at their mouths, the erosion model's deposition built out over the sea, or a sea stack.
        if ((environment.PlannedRivers?.DeltaLand?[i] ?? false) || (environment.PlannedDeltaLand?[i] ?? false) || (environment.PlannedStacks?[i] ?? false))
        {
            __result = null;
            return;
        }
        // Shallow or deep by the sea floor's own depth: bars, delta fronts and channels show in the water.
        if ((__result == TerrainDefOf.WaterOceanShallow || __result == TerrainDefOf.WaterOceanDeep) && !float.IsNaN(environment.PlannedSeaLevel)
            && !float.IsNaN(environment.PlannedShallowDepth) && environment.HasLayer(EnvironmentLayerDefOf.LL_Landform))
        {
            float below = environment.PlannedSeaLevel - environment.ValueAt(EnvironmentLayerDefOf.LL_Landform, cell, environment.PlannedSeaLevel);
            __result = below < environment.PlannedShallowDepth ? TerrainDefOf.WaterOceanShallow : TerrainDefOf.WaterOceanDeep;
        }
    }
}
