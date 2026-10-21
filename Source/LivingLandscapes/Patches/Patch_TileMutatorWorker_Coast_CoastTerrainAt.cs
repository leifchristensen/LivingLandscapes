using HarmonyLib;
using LivingLandscapes.Environment;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Delta land: where rivers built ground above the sea (<see cref="Geology.RiverSystem.DeltaLand"/>, and the erosion model's
/// <see cref="MapComponent_Environment.PlannedDeltaLand"/>), the coast mutator leaves the
/// terrain the geology planned instead of painting sea or beach over it.
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
        // Land the channels built at their mouths, or the erosion model's deposition built out over the sea.
        if ((environment.PlannedRivers?.DeltaLand?[i] ?? false) || (environment.PlannedDeltaLand?[i] ?? false))
            __result = null;
    }
}
